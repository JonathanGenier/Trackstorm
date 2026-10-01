using System.Numerics;
using Trackstorm.Core.Input;

namespace Trackstorm.Core.Vehicles;

/// <summary>Pure fixed-step arcade movement decisions. A runtime supplies collision-resolved body observations.</summary>
public sealed class VehicleMovement
{
    /// <summary>Creates a movement owner with validated immutable tuning and initial pose.</summary>
    /// <param name="configuration">Shared fixed-step tuning.</param>
    /// <param name="initial">Initial body observation.</param>
    public VehicleMovement(VehicleConfiguration configuration, VehiclePhysicsState initial)
    {
        configuration.Validate();
        Configuration = configuration;
        State = new VehicleState(0, initial, false, false, 0, 0);
    }

    /// <summary>Validated movement tuning.</summary>
    public VehicleConfiguration Configuration { get; }
    /// <summary>Latest pre-solver snapshot.</summary>
    public VehicleState State { get; private set; }

    /// <summary>Bounds external velocity without changing direction.</summary>
    /// <param name="value">Finite vector.</param>
    /// <param name="maximum">Positive magnitude bound.</param>
    /// <returns>Clamped vector.</returns>
    public static Vector3 Limit(Vector3 value, float maximum)
    {
        if (!VehiclePhysicsState.IsFinite(value) || !float.IsFinite(maximum) || maximum <= 0)
        {
            throw new ArgumentException("Velocity bounds require finite inputs.");
        }

        return value.LengthSquared() > maximum * maximum ? Vector3.Normalize(value) * maximum : value;
    }

    /// <summary>Advances exactly one input tick, with no render delta, device polling, or native body access.</summary>
    /// <param name="input">Next sequential logical frame.</param>
    /// <param name="observed">Collision-resolved pose and velocities.</param>
    /// <param name="groundNormal">Unit support normal, or zero while airborne.</param>
    /// <param name="driveEnabled">Authority-controlled drive permission.</param>
    /// <param name="surface">Fixed-step supporting surface identifier.</param>
    /// <param name="wheels">Optional independent spring observations.</param>
    /// <param name="nitro">New authoritative activation; prediction only continues existing state.</param>
    /// <param name="clearNitro">Explicit match boundary expiry.</param>
    /// <param name="oilContact">Host-confirmed overlap refreshes recovery; prediction continues restored handling memory.</param>
    /// <param name="contacts">Raw chassis contacts, preserved independently of movement damping.</param>
    /// <param name="waterDepth">Immersion from the shared native observation.</param>
    /// <returns>Next movement snapshot and commanded velocities.</returns>
    public VehicleState Step(InputFrame input, VehiclePhysicsState observed, Vector3 groundNormal, bool driveEnabled = true, SurfaceType surface = SurfaceType.Concrete, WheelSupport? wheels = null, bool oilContact = false, NitroState nitro = default, bool clearNitro = false, float waterDepth = 0, IReadOnlyList<VehicleContact>? contacts = null)
    {
        if (input.Tick != checked(State.Tick + 1))
        {
            throw new ArgumentException("Movement input must target the next tick.", nameof(input));
        }

        _ = new VehiclePhysicsState(observed.Position, observed.Orientation, observed.LinearVelocity, observed.AngularVelocity);
        if (!VehiclePhysicsState.IsFinite(groundNormal) || (groundNormal != Vector3.Zero && Math.Abs(groundNormal.LengthSquared() - 1) > 0.001f))
        {
            throw new ArgumentException("Ground support must be a unit normal or zero.", nameof(groundNormal));
        }

        int recoveryTicks = (int)MathF.Ceiling(Configuration.OilRecoverySeconds * Configuration.TicksPerSecond);
        int oilTicks = !driveEnabled ? 0 : oilContact ? recoveryTicks : Math.Max(0, State.OilTicks - 1);
        int wheelCount = wheels.HasValue ? VehicleCrash.WheelCount(wheels) :
            groundNormal.Y >= Configuration.SupportNormalMinimum && VehicleLanding.Landable(observed.Orientation, groundNormal) ? 4 : 0;
        bool bodyContact = contacts is not null && contacts.Any(contact => !VehicleLanding.SafeContact(observed.Orientation, contact, wheels));
        bool impact = contacts is not null && contacts.Any(contact => !VehicleLanding.SafeContact(observed.Orientation, contact, wheels) &&
            VehicleCrash.Severity(contact, groundNormal, Configuration.Mass) > 4);
        // The portable timer also latches the crash through unsupported bounces. A
        // real wheel contact immediately returns control; body support alone cannot.
        bool stranded = bodyContact && groundNormal.Y >= Configuration.SupportNormalMinimum &&
            !VehicleLanding.Landable(observed.Orientation, groundNormal);
        float crashSeconds = wheelCount > 0 ? 0 : State.CrashSeconds > 0 || impact || stranded
            ? Math.Min(60, State.CrashSeconds + 1f / Configuration.TicksPerSecond) : 0;
        bool crashing = crashSeconds > 0;
        driveEnabled &= !crashing;
        nitro.Validate();
        bool usingNitro = (input.Held & InputButtons.UseItem) != 0 && !clearNitro;
        NitroState boost = !driveEnabled ? default : usingNitro && nitro.Active ? nitro :
            usingNitro ? State.Nitro.Advance() : State.Nitro.Active || State.Nitro.Recovering ? NitroState.Recovery : default;
        VehicleConfiguration c = Configuration;
        float forwardSpeed = boost.Active ? Math.Min(c.MaximumPhysicsSpeed, c.ForwardSpeed * boost.SpeedMultiplier) : c.ForwardSpeed;
        float acceleration = c.Acceleration;
        if (!float.IsFinite(waterDepth) || waterDepth is < 0 or > 1000) { throw new ArgumentOutOfRangeException(nameof(waterDepth)); }
        if (waterDepth > 0) { surface = SurfaceType.Water; }
        _ = c.ResolveSurface(surface);
        float dt = 1f / c.TicksPerSecond;
        bool grounded = groundNormal.Y >= c.SupportNormalMinimum;
        SurfaceType currentSurface = grounded ? surface : State.CurrentSurface;
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, observed.Orientation);
        Vector3 rocketForward = forward;
        Vector3 right = Vector3.Transform(Vector3.UnitX, observed.Orientation);
        Vector3 up = Vector3.Transform(Vector3.UnitY, observed.Orientation);
        Vector3 tireNormal = grounded ? groundNormal : up;
        if (grounded)
        {
            // Tire forces lie in the contact plane even while the sprung chassis rolls or pitches.
            Vector3 tangent = forward - (groundNormal * Vector3.Dot(forward, groundNormal));
            forward = tangent.LengthSquared() > 0.0001f ? Vector3.Normalize(tangent) : Vector3.Normalize(Vector3.Cross(groundNormal, right));
            right = Vector3.Normalize(Vector3.Cross(forward, groundNormal));
        }

        Vector3 velocity = Limit(observed.LinearVelocity, c.MaximumPhysicsSpeed);
        Vector3 angular = Limit(observed.AngularVelocity, c.MaximumAngularSpeed);
        float normalLoad = 0;
        Vector3 suspensionTorque = Vector3.Zero;
        if (grounded && wheels is WheelSupport supports)
        {
            float[] compression = [supports.Compression.X, supports.Compression.Y, supports.Compression.Z, supports.Compression.W];
            for (int index = 0; index < 4; index++)
            {
                if (compression[index] <= 0)
                {
                    continue;
                }

                Vector3 offset = Vector3.Transform(new Vector3(index % 2 == 0 ? -VehicleDimensions.WheelTrack / 2 : VehicleDimensions.WheelTrack / 2, 0, index < 2 ? -c.Wheelbase / 2 : c.Wheelbase / 2), observed.Orientation);
                float wheelVelocity = Vector3.Dot(observed.LinearVelocity + Vector3.Cross(observed.AngularVelocity, offset), groundNormal);
                float bump = Math.Max(0, compression[index] - c.WheelBumpStart);
                float deepTravel = bump / (1 - c.WheelBumpStart);
                float damper = (wheelVelocity < 0 ? c.WheelDamping : c.WheelReboundDamping) * (1 + c.WheelDeepDamping * deepTravel * deepTravel);
                // Point-velocity damping avoids injecting a velocity impulse at a terrain seam.
                // Progressive end resistance remains bounded; excessive landings reach native chassis contact.
                float force = Math.Clamp((compression[index] * c.WheelSpring) + (bump * bump * c.WheelBumpSpring) -
                    wheelVelocity * damper, 0, c.Gravity * 30) / 4;
                // A nearly sideways chassis cannot turn a short oblique ray into a vertical launch.
                force *= MathF.Pow(Math.Clamp(Vector3.Dot(up, groundNormal), 0, 1), 4);
                normalLoad += force;
                suspensionTorque += Vector3.Cross(offset, groundNormal * force);
            }
        }

        float longitudinal = Vector3.Dot(velocity, forward);
        float lateral = Vector3.Dot(velocity, right);
        float steerIntent = driveEnabled ? input.Steering / 32767f : 0;
        float steeringSpeed = MathF.Sqrt(longitudinal * longitudinal + lateral * lateral);
        bool holding = driveEnabled && grounded && waterDepth == 0 && wheelCount >= 2 &&
            (input.Held & InputButtons.Drift) != 0 && steeringSpeed <= c.HandbrakeHoldSpeed;
        float holdCapacity = 0;
        float dirtCorner = grounded ? c.DirtCornering * Math.Clamp((c.DirtCornerFadeSpeed - steeringSpeed) / (c.DirtCornerFadeSpeed - c.DirtCornerFullSpeed), 0, 1) : 0;
        // Change the usable wheel range, never the body's heading or velocity. Air control
        // consumes the original frame independently of this ground-only speed envelope.
        float steeringLimit = grounded ? c.SteeringLimit(steeringSpeed) : c.SteeringAngle;
        float smoothedWheel = State.SteeringAngle + (steerIntent * steeringLimit - State.SteeringAngle) *
            (1 - MathF.Exp(-dt / c.SteeringSmoothing));
        float wheelRate = State.SteeringAngle * steerIntent < 0 ? c.SteeringCounterResponse : c.SteeringResponse;
        float wheel = DrivingInputShaping.Approach(State.SteeringAngle, smoothedWheel, wheelRate, dt);
        float handbrakeTarget = driveEnabled && (input.Held & InputButtons.Drift) != 0 ? 1 : 0;
        float handbrake = driveEnabled ? DrivingInputShaping.Approach(State.Handbrake, handbrakeTarget, handbrakeTarget > State.Handbrake ? c.HandbrakeResponse : c.TractionRecovery, dt) : 0;
        float pedal = driveEnabled ? input.Accelerate / 65535f : 0;
        float brake = driveEnabled ? input.Brake / 65535f : 0;
        // Engine demand has one portable response state. Brakes/disable cut power immediately;
        // aerial controls retain the raw pedals and their existing independent input response.
        float throttleTime = pedal > State.Throttle ? c.ThrottleRiseTime : c.ThrottleFallTime;
        float throttle = !driveEnabled || brake > 0 ? 0 : State.Throttle + (pedal - State.Throttle) * (1 - MathF.Exp(-dt / throttleTime));
        BrakeMode brakeMode = State.BrakeMode;
        bool newBrakePress = (input.Pressed & input.Held & InputButtons.Brake) != 0;
        if (!driveEnabled || brake == 0) { brakeMode = BrakeMode.Ready; }
        else if ((input.Released & InputButtons.Brake) != 0) { brakeMode = BrakeMode.ReleaseTail; }
        if (grounded && brake > 0 && (brakeMode == BrakeMode.Ready || newBrakePress))
        {
            // A new press accepts mild slope creep as a stop.
            brakeMode = longitudinal > c.ReverseEngagementSpeed ? BrakeMode.Stopping : BrakeMode.Reversing;
        }
        // Native gravity can reintroduce forward creep after every braking solve.
        // Engage within one available braking step, rather than waiting for an
        // exact-zero observation that may never arrive on a downhill grade.
        float reverseThreshold = c.StopSpeed + brake * c.Braking * c.ReferenceMass / c.Mass * dt;
        if (grounded && brakeMode == BrakeMode.Stopping && longitudinal <= reverseThreshold)
        {
            brakeMode = BrakeMode.Reversing;
        }
        float longAcceleration = 0;
        float sideAcceleration = 0;
        float frontSlip = 0;
        float rearSlip = 0;
        float powerSlip = State.PowerSlip * MathF.Exp(-c.PowerSlipRecovery * dt);
        if (grounded)
        {
            WheelSupport contact = wheels ?? default;
            SurfaceType[] materials = [contact.FrontLeft ?? surface, contact.FrontRight ?? surface, contact.RearLeft ?? surface, contact.RearRight ?? surface];
            SurfaceModifiers[] profiles = materials.Select(material => c.ResolveSurface(waterDepth > 0 ? SurfaceType.Water : material)).ToArray();
            SurfaceBraking[] braking = materials.Select(material => c.ResolveBraking(waterDepth > 0 ? SurfaceType.Water : material)).ToArray();
            Vector4 compression = contact.Compression;
            float frontTotal = compression.X + compression.Y;
            float rearTotal = compression.Z + compression.W;
            float frontLeftShare = frontTotal > 0 ? compression.X / frontTotal : 0.5f;
            float rearLeftShare = rearTotal > 0 ? compression.Z / rearTotal : 0.5f;
            float driveModifier = profiles[2].Acceleration * rearLeftShare + profiles[3].Acceleration * (1 - rearLeftShare);
            // Engage drive within one braking step of rest. Requiring exact zero can trap a
            // vehicle in perpetual braking when gravity adds downhill velocity between ticks.
            float forceScale = c.ReferenceMass / c.Mass;
            float engagementSpeed = c.StopSpeed + c.Braking * forceScale * dt;
            float drive = 0;
            float stopping = 0;
            if (brake > 0 && (brakeMode is BrakeMode.Stopping or BrakeMode.ReleaseTail || longitudinal > engagementSpeed))
            {
                stopping = brake * c.Braking;
            }
            else if (longitudinal < -engagementSpeed && pedal > 0)
            {
                // The accelerator acts as the service brake while reversing.
                stopping = pedal * c.Braking;
            }
            else if (throttle > 0)
            {
                drive = Math.Min(acceleration * throttle * driveModifier * Math.Clamp(1 - MathF.Pow(Math.Max(0, longitudinal) / forwardSpeed, 4), 0, 1), Math.Max(0, forwardSpeed - longitudinal) / dt);
            }
            else if (brake > 0 && brakeMode == BrakeMode.Reversing)
            {
                drive = -Math.Min(c.ReverseAcceleration * brake * driveModifier, Math.Max(0, c.ReverseSpeed + longitudinal) / dt);
            }

            stopping = Math.Min(stopping * forceScale, Math.Abs(longitudinal) / dt);
            // One progressive application controls rear braking, engine interruption and grip on hold/release.
            float brakeApplication = holding ? 1 : handbrake;
            float handbrakeStop = Math.Min(c.HandbrakeBraking * brakeApplication * forceScale, Math.Max(0, (Math.Abs(longitudinal) / dt) - stopping));
            float frontLong = -Math.Sign(longitudinal) * stopping * c.FrontBrakeShare;
            float driveAcceleration = Math.Clamp(drive * forceScale, -Math.Max(0, c.ReverseSpeed + longitudinal) / dt, Math.Max(0, forwardSpeed - longitudinal) / dt);
            // Configurable surface multipliers can be extreme; keep accepted force diagnostics
            // inside the portable handling-state contract as well as the velocity safety bound.
            driveAcceleration = Math.Clamp(driveAcceleration, -1000, 1000);
            // Production drive is rear-only; the existing configurable axle split remains available.
            // The handbrake interrupts engine torque against a deliberately locked rear.
            float engine = driveAcceleration * (1 - brakeApplication);
            frontLong += engine * c.FrontDriveShare;
            float rearLong = engine * (1 - c.FrontDriveShare) - (Math.Sign(longitudinal) * ((stopping * (1 - c.FrontBrakeShare)) + handbrakeStop));
            float halfAxle = c.Wheelbase / 2;
            // Load transfer changes the traction budget; tire demands generate both translation and yaw.
            float frontLoad = Math.Clamp(0.5f - (State.LongitudinalAcceleration * c.LoadHeight / (c.Gravity * c.Wheelbase)), 0.2f, 0.8f);
            if (wheels.HasValue)
            {
                float total = compression.X + compression.Y + compression.Z + compression.W;
                if (total > 0)
                {
                    frontLoad = Math.Clamp((frontLoad + ((compression.X + compression.Y) / total)) / 2, 0.1f, 0.9f);
                    if (frontTotal == 0) { frontLoad = 0; }
                    if (rearTotal == 0) { frontLoad = 1; }
                }
            }

            float tireLoad = wheels.HasValue ? normalLoad : c.Gravity * groundNormal.Y;
            // Missing wheel forces already reduce normalLoad; do not discount their absence twice.
            // Arcade corner authority fades with speed. Extra tire capacity turns the travel
            // direction as well as the nose, so tight steering does not become a stationary spin.
            float totalGrip = c.TireFriction * tireLoad;
            float[] tireGrip = profiles.Select((profile, index) => profile.Grip * (waterDepth == 0 && materials[index] == SurfaceType.Dirt ? 1 + c.DirtCornerGrip * dirtCorner * Math.Abs(steerIntent) : 1)).ToArray();
            float oilGrip = 1 - c.OilGripReduction * Math.Clamp((float)oilTicks / recoveryTicks, 0, 1);
            float frontCapacity = totalGrip * frontLoad;
            float rearCapacity = totalGrip * (1 - frontLoad);
            // Ordinary throttle/steering never manufactures rear slip. Retained legacy
            // slip decays through the portable recovery state; tire saturation below
            // and the deliberate handbrake still determine physical traction loss.
            float yaw = Vector3.Dot(angular, tireNormal);
            float wheelSin = MathF.Sin(wheel), wheelCos = MathF.Cos(wheel);
            float frontSideSpeed = (lateral - yaw * halfAxle) * wheelCos - longitudinal * wheelSin;
            float rearSideSpeed = lateral + (yaw * halfAxle);
            float response = Math.Min(c.Grip * forceScale, 1 / dt);
            float frontDemand = -frontSideSpeed * response * 0.5f;
            float rearDemand = -rearSideSpeed * response * 0.5f;
            float driveReserve = driveAcceleration != 0 ? c.DriveTractionReserve * (1 - handbrake) : 0;
            float brakeGrip = 1 + (c.BrakeGrip - 1) * (stopping > 0 ? Math.Max(pedal, brake) : 0);
            float rearLongGrip = rearLong * longitudinal >= 0 && engine != 0 ? c.RearDriveGrip * (1 - c.SpinDriveLoss * powerSlip) : brakeGrip;
            float service = stopping > 0 ? Math.Max(pedal, brake) : 0;
            float LateralGrip(int index, bool rear)
            {
                if (holding) { return 1; }
                float retained = rear ? braking[index].BrakeLateralGrip : MathF.Sqrt(braking[index].BrakeLateralGrip);
                float serviceGrip = 1 - service * (1 - retained);
                return serviceGrip * (rear ? (1 - handbrake * (1 - Math.Min(1, c.HandbrakeGrip * braking[index].HandbrakeLateralGrip))) * (1 - powerSlip) : 1);
            }
            float BrakeScale(int index, float demand) => demand * longitudinal < 0 ? braking[index].Deceleration : 1;
            float flBrake = BrakeScale(0, frontLong), frBrake = BrakeScale(1, frontLong);
            float rlBrake = BrakeScale(2, rearLong), rrBrake = BrakeScale(3, rearLong);
            var fl = Tire(frontDemand * frontLeftShare, frontLong * frontLeftShare * flBrake, frontCapacity * frontLeftShare * tireGrip[0], LateralGrip(0, false), driveReserve, brakeGrip * flBrake);
            var fr = Tire(frontDemand * (1 - frontLeftShare), frontLong * (1 - frontLeftShare) * frBrake, frontCapacity * (1 - frontLeftShare) * tireGrip[1], LateralGrip(1, false), driveReserve, brakeGrip * frBrake);
            // Reserve part of saturated front dirt traction for the filtered wheel direction.
            // This changes force allocation, not the surface's friction budget or drive demand.
            // Half authority near a 22-degree slide; the speed floor calms parking-speed input.
            float slide = lateral * lateral / (0.16f * longitudinal * longitudinal + lateral * lateral + 4);
            float frontDirtShare = (materials[0] == SurfaceType.Dirt ? frontLeftShare : 0) + (materials[1] == SurfaceType.Dirt ? 1 - frontLeftShare : 0);
            float powerTurn = dirtCorner * frontDirtShare * throttle * Math.Abs(steerIntent);
            float steeringReserve = Math.Max(slide, Math.Max(Math.Min(1, powerTurn * 0.7f), handbrake * Math.Abs(steerIntent)));
            // Rear-lock rotation uses the actual front contact velocity, including lateral
            // motion and yaw. The powered dirt reserve must not keep steering from unsigned
            // road speed after a handbrake slide has rotated past broadside.
            steeringReserve *= 1 - handbrake;
            float steeringTravel = longitudinal;
            // The steered contact already moves laterally as the chassis yaws. Account
            // for that velocity in the reserved demand, just as the ordinary tire does;
            // otherwise a held turn keeps adding front torque after the nose follows.
            float steeringDemand = (steeringTravel * MathF.Sin(wheel) + yaw * halfAxle * MathF.Cos(wheel)) * response * 0.5f;
            if (driveEnabled && waterDepth == 0 && (!wheels.HasValue || frontTotal > 0))
            {
                // With the wheel centered, grass must spend its front purchase correcting
                // lateral travel rather than reserving it for a direction nobody requested.
                float grassCommitment = Math.Clamp(Math.Abs(wheel) / steeringLimit, 0, 1);
                float Reserve(int index) => materials[index] == SurfaceType.Dirt ? c.DirtSteeringReserve : materials[index] == SurfaceType.Grass ? c.GrassSteeringReserve * grassCommitment : 0;
                fl = SlideFront(fl, frontLong * frontLeftShare * flBrake, steeringDemand * frontLeftShare, frontCapacity * frontLeftShare * tireGrip[0], steeringReserve * Reserve(0), LateralGrip(0, false), brakeGrip * flBrake);
                fr = SlideFront(fr, frontLong * (1 - frontLeftShare) * frBrake, steeringDemand * (1 - frontLeftShare), frontCapacity * (1 - frontLeftShare) * tireGrip[1], steeringReserve * Reserve(1), LateralGrip(1, false), brakeGrip * frBrake);
            }
            var rl = Tire(rearDemand * rearLeftShare, rearLong * rearLeftShare * rlBrake, rearCapacity * rearLeftShare * tireGrip[2], LateralGrip(2, true), driveReserve, rearLongGrip * rlBrake);
            var rr = Tire(rearDemand * (1 - rearLeftShare), rearLong * (1 - rearLeftShare) * rrBrake, rearCapacity * (1 - rearLeftShare) * tireGrip[3], LateralGrip(3, true), driveReserve, rearLongGrip * rrBrake);
            // Static friction acts in the support plane after gravity. Its impulse is bounded
            // by supported tire load, surface purchase and the ordinary mechanical brake.
            holdCapacity = Math.Min(c.HandbrakeBraking * forceScale,
                (frontCapacity * (tireGrip[0] * frontLeftShare * braking[0].Deceleration + tireGrip[1] * (1 - frontLeftShare) * braking[1].Deceleration) +
                rearCapacity * (tireGrip[2] * rearLeftShare * braking[2].Deceleration + tireGrip[3] * (1 - rearLeftShare) * braking[3].Deceleration)) * oilGrip);
            float frontForce = ((fl.Side + fr.Side) * wheelCos + (fl.Drive + fr.Drive) * wheelSin) * oilGrip;
            float rearForce = (rl.Side + rr.Side) * oilGrip;
            float frontDrive = (fl.Drive + fr.Drive) * wheelCos - (fl.Side + fr.Side) * wheelSin * oilGrip;
            float rearDrive = rl.Drive + rr.Drive;
            frontSlip = fl.Slip * frontLeftShare + fr.Slip * (1 - frontLeftShare);
            rearSlip = rl.Slip * rearLeftShare + rr.Slip * (1 - rearLeftShare);
            longAcceleration = frontDrive + rearDrive;
            sideAcceleration = frontForce + rearForce;
            velocity += ((forward * longAcceleration) + (right * sideAcceleration)) * dt;
            float nextLongitudinal = Vector3.Dot(velocity, forward);
            if (stopping > 0 && Math.Abs(nextLongitudinal) < c.StopSpeed)
            {
                velocity -= forward * nextLongitudinal;
            }

            float drag = ((profiles[0].Drag * frontLeftShare + profiles[1].Drag * (1 - frontLeftShare)) * frontLoad) +
                ((profiles[2].Drag * rearLeftShare + profiles[3].Drag * (1 - rearLeftShare)) * (1 - frontLoad));
            float coast = throttle == 0 && brake == 0 ? drag : Math.Max(0, drag - 1);
            velocity -= forward * (Vector3.Dot(velocity, forward) * (1 - MathF.Exp(-c.CoastDrag * coast * dt)));
            float inertiaPerMass = c.Wheelbase * c.Wheelbase / 3;
            angular += tireNormal * (halfAxle * (rearForce - frontForce) / inertiaPerMass * dt);
            // Split material contact creates torque through the existing track-width lever arm.
            angular += tireNormal * (VehicleDimensions.WheelTrack / 2 * (fr.Drive + rr.Drive - fl.Drive - rl.Drive) / inertiaPerMass * dt);
            angular -= tireNormal * (Vector3.Dot(angular, tireNormal) * (1 - MathF.Exp(-c.StabilityDamping * dt)));
            float Recovery(int index) => materials[index] == SurfaceType.Dirt ? c.DirtRecovery : materials[index] == SurfaceType.Grass ? c.GrassRecovery : 0;
            float recoveryRate = waterDepth > 0 || (wheels.HasValue && frontTotal == 0) ? 0 : Recovery(0) * frontLeftShare + Recovery(1) * (1 - frontLeftShare);
            float recoveryGrip = (Recovery(0) > 0 ? tireGrip[0] * frontLeftShare * LateralGrip(0, false) : 0) + (Recovery(1) > 0 ? tireGrip[1] * (1 - frontLeftShare) * LateralGrip(1, false) : 0);
            if (driveEnabled && !holding && recoveryRate > 0 && tireLoad > 0)
            {
                // A bounded arcade correction arrests runaway yaw while retaining tire-driven
                // translation and handbrake initiation. No heading, velocity or drift-mode snap.
                float yawLimit = totalGrip * recoveryGrip / Math.Max(steeringSpeed, 2);
                float intendedYaw = Math.Clamp(-steeringTravel * MathF.Tan(wheel) / c.Wheelbase, -yawLimit, yawLimit);
                float currentYaw = Vector3.Dot(angular, tireNormal);
                // Countersteering gets full recovery authority even while the rear is locked;
                // only rotation already following the wheel retains the loose drift response.
                float recovery = intendedYaw * currentYaw < 0 ? 1 : 1 - 0.75f * handbrake;
                float correction = (intendedYaw - currentYaw) * (1 - MathF.Exp(-recoveryRate * Math.Max(slide, powerTurn) * recovery * dt));
                float authority = frontCapacity * oilGrip * recoveryGrip * halfAxle / inertiaPerMass * dt;
                angular += tireNormal * Math.Clamp(correction, -authority, authority);
            }
        }

        // Rocket thrust is a central force along the chassis, independent of pedals and tire contact.
        // Only its added forward velocity is bounded; existing momentum is never clamped to the drive cap.
        if (boost.Active)
        {
            float thrust = boost.ForwardThrust / c.Mass * (grounded ? 1 : boost.AirborneThrustScale);
            float addition = Math.Min(thrust * dt, Math.Max(0, forwardSpeed - Vector3.Dot(velocity, rocketForward)));
            velocity += rocketForward * addition;
        }

        // Remove only excess road speed at a bounded rate, preserving direction and vertical motion.
        float roadSpeed = new Vector2(velocity.X, velocity.Z).Length();
        if (boost.Recovering && roadSpeed > forwardSpeed)
        {
            float reduction = Math.Min(roadSpeed - forwardSpeed, c.OverspeedDeceleration * dt);
            float scale = (roadSpeed - reduction) / roadSpeed;
            velocity.X *= scale;
            velocity.Z *= scale;
        }

        if (boost.Recovering && roadSpeed <= forwardSpeed) { boost = default; }

        if (crashing && bodyContact && grounded)
        {
            // Body contact absorbs separation and sliding energy. Preserve rolling momentum
            // instead of arresting the crash and then rotating a stationary chassis upright.
            float separating = Vector3.Dot(velocity, groundNormal);
            if (separating > 0) { velocity -= groundNormal * separating; }
            Vector3 scraping = velocity - groundNormal * Vector3.Dot(velocity, groundNormal);
            velocity -= scraping * (1 - MathF.Exp(-c.CrashSlideDamping * dt));
            angular *= MathF.Exp(-c.CrashRollDamping * dt);
        }
        if (crashing)
        {
            // Keep early high-energy flips. Gentle airborne decay replaces the much
            // stronger released-player-axis damping for the duration of the crash.
            if (!bodyContact) { angular *= MathF.Exp(-c.CrashRollDamping * 0.25f * dt); }
            if (crashSeconds >= c.CrashRecoveryDelay && c.CrashRecoveryRate > 0)
            {
                Vector3 recoveryUp = grounded ? groundNormal : Vector3.UnitY;
                Vector3 rolling = angular - recoveryUp * Vector3.Dot(angular, recoveryUp);
                // Continue the existing roll/flip, even past inversion. A stranded body has
                // no momentum to preserve: choose the shortest tip, with a stable roof tie-break.
                Vector3 axis = rolling.LengthSquared() > 0.0625f ? rolling : Vector3.Cross(up, recoveryUp);
                if (axis.LengthSquared() < 0.01f) { axis = rocketForward; }
                axis = Vector3.Normalize(axis);
                // Build a rate floor over time: tiny per-step torque alone is canceled by
                // resting roof contacts in the native solver. Orientation remains integrated.
                float rate = c.CrashRecoveryRate * Math.Clamp((crashSeconds - c.CrashRecoveryDelay) / c.CrashRecoveryRamp, 0, 1);
                // Once facing the tires, let gravity/suspension catch the truck. Do
                // not power a new flip or add linear/vertical recovery impulses.
                if (Vector3.Dot(up, recoveryUp) < 0.65f)
                {
                    angular += axis * Math.Max(0, rate - Vector3.Dot(angular, axis));
                }
            }
        }
        AirControlState air = default;
        if (grounded)
        {
            // Chassis contact on a roof/bumper is not suspension support. Preserve the crash
            // attitude; only landable wheel support receives the ordinary ground attitude spring.
            float supportedFraction = wheels is WheelSupport supportWheels
                ? new[] { supportWheels.Compression.X, supportWheels.Compression.Y, supportWheels.Compression.Z, supportWheels.Compression.W }.Count(value => value > 0) / 4f
                : 1;
            float stability = VehicleLanding.Landable(observed.Orientation, groundNormal) ? supportedFraction : 0;
            Vector3 desiredUp = groundNormal;
            desiredUp -= forward * Math.Clamp(longAcceleration * c.ChassisCompliance, -c.MaximumChassisTilt, c.MaximumChassisTilt);
            desiredUp -= right * Math.Clamp(sideAcceleration * c.ChassisCompliance, -c.MaximumChassisTilt, c.MaximumChassisTilt);
            angular += Vector3.Cross(up, Vector3.Normalize(desiredUp)) * c.SuspensionSpring * stability * dt;
            Vector3 tiltVelocity = angular - (tireNormal * Vector3.Dot(angular, tireNormal));
            angular -= tiltVelocity * (1 - MathF.Exp(-c.SuspensionDamping * stability * dt));
        }
        else if (!crashing)
        {
            float seconds = Math.Min(60, State.Air.Seconds + dt);
            air = new AirControlState(seconds, Vector3.Zero, Vector3.Zero);
            if (driveEnabled && seconds + 0.000001f >= c.AirDelay)
            {
                float pitch = InputAxis.Normalize(brake - pedal, c.AirDeadZone);
                float turn = InputAxis.Normalize(steerIntent, c.AirDeadZone);
                bool roll = (input.Held & InputButtons.AirRoll) != 0;
                Vector3 target = new(pitch, roll ? 0 : -turn, roll ? -turn : 0);
                Vector3 intent = Vector3.Lerp(State.Air.Input, target, 1 - MathF.Exp(-dt / c.AirInputResponse));
                Vector3 stabilization = Vector3.Lerp(State.Air.Stabilization, Vector3.One - Vector3.Abs(target), 1 - MathF.Exp(-dt / c.AirStabilizationResponse));
                Vector3 local = Vector3.Transform(angular, Quaternion.Conjugate(observed.Orientation));
                local = new Vector3(
                    AirAxis(local.X, intent.X, target.X, stabilization.X, c.AirPitchRate, c.AirPitchAcceleration, c, dt),
                    AirAxis(local.Y, intent.Y, target.Y, stabilization.Y, c.AirYawRate, c.AirYawAcceleration, c, dt),
                    AirAxis(local.Z, intent.Z, target.Z, stabilization.Z, c.AirRollRate, c.AirRollAcceleration, c, dt));
                angular = Vector3.Transform(local, observed.Orientation);
                air = new AirControlState(seconds, intent, stabilization);
            }
        }
        velocity += groundNormal * normalLoad * dt;
        angular += suspensionTorque / (c.Wheelbase * c.Wheelbase / 3) * dt;

        velocity -= Vector3.UnitY * (c.Gravity * dt);
        if (holding && holdCapacity > 0)
        {
            Vector3 tangent = velocity - groundNormal * Vector3.Dot(velocity, groundNormal);
            float speed = tangent.Length();
            if (speed > 0) { velocity -= tangent * Math.Min(1, holdCapacity * dt / speed); }
            float yaw = Vector3.Dot(angular, groundNormal);
            float holdTorque = holdCapacity * (c.Wheelbase / 2) / (c.Wheelbase * c.Wheelbase / 3);
            angular -= groundNormal * Math.Clamp(yaw, -holdTorque * dt, holdTorque * dt);
        }
        float landing = grounded && !State.Grounded ? Math.Clamp(-State.Physics.LinearVelocity.Y / 12, 0, 1) : Math.Max(0, State.LandingIntensity - (dt * c.LandingReboundDecay));
        // A clean landing dissipates the first compression/release cycle. Reuse the
        // portable landing envelope; ordinary ramp loading and airborne input are untouched.
        if (landing > 0 && grounded && wheelCount >= 3 && VehicleLanding.Landable(observed.Orientation, groundNormal))
        {
            float rebound = Math.Max(0, Vector3.Dot(velocity, groundNormal));
            velocity -= groundNormal * rebound * (1 - MathF.Exp(-c.WheelReboundDamping * landing * dt));
        }
        bool sliding = grounded && Math.Abs(lateral) > 1 && rearSlip > 0.35f;
        var physics = new VehiclePhysicsState(observed.Position, observed.Orientation, Limit(velocity, c.MaximumPhysicsSpeed), Limit(angular, c.MaximumAngularSpeed));
        State = new VehicleState(input.Tick, physics, grounded, sliding, wheel, handbrake, currentSurface, frontSlip, rearSlip, longAcceleration, sideAcceleration, landing, wheels ?? default, oilTicks, boost, powerSlip, air, crashSeconds, throttle, brakeMode);
        return State;
    }

    /// <summary>Restores every movement memory field for replay/reconciliation without hidden timers.</summary>
    /// <param name="state">Validated saved snapshot.</param>
    public void Restore(VehicleState state)
    {
        state.Validate();
        if (Math.Abs(state.SteeringAngle) > Configuration.SteeringAngle)
        {
            throw new ArgumentException("Snapshot steering exceeds this vehicle's tuning.", nameof(state));
        }

        State = state;
    }

    private static float AirAxis(float velocity, float intent, float target, float stabilization, float rate, float acceleration, VehicleConfiguration c, float dt)
    {
        // Only a held command seeks an angular rate. Release damps velocity, never orientation.
        if (target != 0)
        {
            return velocity + Math.Clamp(intent * rate - velocity, -acceleration * dt, acceleration * dt);
        }
        return velocity * MathF.Exp(-c.AirStabilization * stabilization * dt);
    }
    private static (float Side, float Drive, float Slip) SlideFront((float Side, float Drive, float Slip) tire, float braking, float steering, float capacity, float reserve, float lateralGrip, float longitudinalGrip)
    {
        if (reserve == 0 || longitudinalGrip <= 0) { return tire; }
        var directed = Tire(steering, braking, capacity, lateralGrip, longitudinalGrip: longitudinalGrip);
        float available = MathF.Sqrt(Math.Max(0, capacity * capacity - tire.Drive * tire.Drive / (longitudinalGrip * longitudinalGrip))) * lateralGrip;
        float side = tire.Side + (Math.Clamp(directed.Side, -available, available) - tire.Side) * reserve;
        return (side, tire.Drive, tire.Slip);
    }

    private static (float Side, float Drive, float Slip) Tire(float lateral, float longitudinal, float capacity, float lateralFraction = 1, float driveReserve = 0, float longitudinalGrip = 1)
    {
        if (longitudinalGrip <= 0) { longitudinal = 0; longitudinalGrip = 1; }
        float longitudinalDemand = longitudinal / longitudinalGrip;
        float demand = MathF.Sqrt((lateral * lateral) + (longitudinalDemand * longitudinalDemand));
        if (demand < 0.00001f)
        {
            return (0, 0, 0);
        }

        if (capacity <= 0)
        {
            return (0, 0, 1);
        }

        // Smooth saturation avoids a grip/drift switch and couples acceleration/braking to cornering.
        float ratio = demand / capacity;
        float scale = MathF.Tanh(ratio) / ratio;
        float drive = longitudinal * scale;
        float side = lateral * scale;
        if (driveReserve > 0)
        {
            // Allocate propulsion inside the same friction ellipse, never above pedal demand or pure longitudinal traction.
            float availableDrive = capacity * longitudinalGrip * MathF.Tanh(Math.Abs(longitudinal) / (capacity * longitudinalGrip));
            drive = Math.Sign(longitudinal) * Math.Max(Math.Abs(drive), Math.Min(capacity * longitudinalGrip * driveReserve, availableDrive));
            float remainingSide = MathF.Sqrt(Math.Max(0, (capacity * capacity) - (drive * drive / (longitudinalGrip * longitudinalGrip))));
            side = Math.Clamp(side, -remainingSide, remainingSide);
        }

        float lateralScale = Math.Abs(lateral) > 0.00001f ? Math.Abs(side / lateral) : scale;
        return (side * lateralFraction, drive, Math.Clamp(1 - (lateralScale * lateralFraction), 0, 1));
    }
}
