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
    /// <returns>Next movement snapshot and commanded velocities.</returns>
    public VehicleState Step(InputFrame input, VehiclePhysicsState observed, Vector3 groundNormal, bool driveEnabled = true, SurfaceType surface = SurfaceType.Concrete, WheelSupport? wheels = null)
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

        VehicleConfiguration c = Configuration;
        SurfaceModifiers detected = c.ResolveSurface(surface);
        float dt = 1f / c.TicksPerSecond;
        bool grounded = groundNormal.Y >= 0.55f;
        SurfaceType currentSurface = grounded ? surface : State.CurrentSurface;
        SurfaceModifiers modifiers = grounded ? detected : new SurfaceModifiers(1, 1, 1);
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, observed.Orientation);
        Vector3 right = Vector3.Transform(Vector3.UnitX, observed.Orientation);
        Vector3 up = Vector3.Transform(Vector3.UnitY, observed.Orientation);
        Vector3 velocity = Limit(observed.LinearVelocity, c.MaximumPhysicsSpeed);
        Vector3 angular = Limit(observed.AngularVelocity, c.MaximumAngularSpeed);
        float longitudinal = Vector3.Dot(velocity, forward);
        float lateral = Vector3.Dot(velocity, right);
        float steerIntent = driveEnabled ? input.Steering / 32767f : 0;
        float wheelLimit = c.SteeringAngle / (1 + MathF.Pow(Math.Abs(longitudinal) / c.SteeringSpeed, 2));
        float wheel = DrivingInputShaping.Approach(State.SteeringAngle, steerIntent * wheelLimit, c.SteeringResponse, dt);
        float handbrakeTarget = driveEnabled && (input.Held & InputButtons.Drift) != 0 ? 1 : 0;
        float handbrake = driveEnabled ? DrivingInputShaping.Approach(State.Handbrake, handbrakeTarget, handbrakeTarget > State.Handbrake ? c.HandbrakeResponse : c.TractionRecovery, dt) : 0;
        float throttle = driveEnabled ? input.Accelerate / 65535f : 0;
        float brake = driveEnabled ? input.Brake / 65535f : 0;
        float longAcceleration = 0;
        float sideAcceleration = 0;
        float frontSlip = 0;
        float rearSlip = 0;
        if (grounded)
        {
            // Pedals brake opposing motion to zero before allowing a direction reversal on a later tick.
            float drive = 0;
            float stopping = 0;
            if (longitudinal > 0 && brake > 0)
            {
                stopping = brake * c.Braking;
            }
            else if (longitudinal < 0 && throttle > 0)
            {
                stopping = throttle * c.Braking;
            }
            else if (throttle > 0)
            {
                drive = Math.Min(c.Acceleration * throttle * modifiers.Acceleration, Math.Max(0, c.ForwardSpeed - longitudinal) / dt);
            }
            else if (brake > 0)
            {
                drive = -Math.Min(c.ReverseAcceleration * brake * modifiers.Acceleration, Math.Max(0, c.ReverseSpeed + longitudinal) / dt);
            }

            float forceScale = c.ReferenceMass / c.Mass;
            stopping = Math.Min(stopping * forceScale, Math.Abs(longitudinal) / dt);
            float handbrakeStop = Math.Min(c.HandbrakeBraking * handbrake * forceScale, Math.Max(0, (Math.Abs(longitudinal) / dt) - stopping));
            float frontLong = -Math.Sign(longitudinal) * stopping * 0.65f;
            float driveAcceleration = Math.Clamp(drive * forceScale, -Math.Max(0, c.ReverseSpeed + longitudinal) / dt, Math.Max(0, c.ForwardSpeed - longitudinal) / dt);
            // A locked rear axle cannot transmit engine drive against its handbrake.
            float rearLong = (driveAcceleration * (1 - handbrake)) - (Math.Sign(longitudinal) * ((stopping * 0.35f) + handbrakeStop));
            float halfAxle = c.Wheelbase / 2;
            // Load transfer changes the traction budget; tire demands generate both translation and yaw.
            float frontLoad = Math.Clamp(0.5f - (State.LongitudinalAcceleration * c.LoadHeight / (c.Gravity * c.Wheelbase)), 0.2f, 0.8f);
            float supportedFraction = 1;
            if (wheels is WheelSupport tireSupport)
            {
                System.Numerics.Vector4 compression = tireSupport.Compression;
                float total = compression.X + compression.Y + compression.Z + compression.W;
                supportedFraction = ((compression.X > 0 ? 1 : 0) + (compression.Y > 0 ? 1 : 0) + (compression.Z > 0 ? 1 : 0) + (compression.W > 0 ? 1 : 0)) / 4f;
                if (total > 0)
                {
                    frontLoad = Math.Clamp((frontLoad + ((compression.X + compression.Y) / total)) / 2, 0.1f, 0.9f);
                }
            }

            float totalGrip = c.TireFriction * c.Gravity * modifiers.Grip * supportedFraction;
            float frontCapacity = totalGrip * frontLoad;
            float rearCapacity = totalGrip * (1 - frontLoad);
            float frontSideSpeed = lateral - (angular.Y * halfAxle) - (longitudinal * MathF.Tan(wheel));
            float rearSideSpeed = lateral + (angular.Y * halfAxle);
            float response = Math.Min(c.Grip * forceScale, 1 / dt);
            float frontDemand = -frontSideSpeed * response * 0.5f;
            float rearDemand = -rearSideSpeed * response * 0.5f;
            (float frontForce, float frontDrive, float frontSaturation) = Tire(frontDemand, frontLong, frontCapacity);
            (float rearForce, float rearDrive, float rearSaturation) = Tire(rearDemand, rearLong, rearCapacity, 1 - (handbrake * (1 - c.HandbrakeGrip)));
            frontSlip = frontSaturation;
            rearSlip = rearSaturation;
            longAcceleration = frontDrive + rearDrive;
            sideAcceleration = frontForce + rearForce;
            velocity += ((forward * longAcceleration) + (right * sideAcceleration)) * dt;
            float nextLongitudinal = Vector3.Dot(velocity, forward);
            if (((brake > 0 && longitudinal > 0) || (throttle > 0 && longitudinal < 0)) && Math.Abs(nextLongitudinal) < c.StopSpeed)
            {
                velocity -= forward * nextLongitudinal;
            }

            float coast = throttle == 0 && brake == 0 ? modifiers.Drag : Math.Max(0, modifiers.Drag - 1);
            velocity -= forward * (Vector3.Dot(velocity, forward) * (1 - MathF.Exp(-c.CoastDrag * coast * dt)));
            float inertiaPerMass = c.Wheelbase * c.Wheelbase / 3;
            angular.Y += halfAxle * (rearForce - frontForce) / inertiaPerMass * dt;
            angular.Y *= MathF.Exp(-c.StabilityDamping * dt);
        }

        // Chassis load response acts on the physical body, using the same forces that consume tire grip.
        Vector3 desiredUp = grounded ? groundNormal : Vector3.UnitY;
        if (grounded)
        {
            desiredUp -= forward * Math.Clamp(longAcceleration * c.ChassisCompliance, -c.MaximumChassisTilt, c.MaximumChassisTilt);
            desiredUp -= right * Math.Clamp(sideAcceleration * c.ChassisCompliance, -c.MaximumChassisTilt, c.MaximumChassisTilt);
        }

        Vector3 spring = Vector3.Cross(up, Vector3.Normalize(desiredUp));
        angular += spring * (grounded ? c.SuspensionSpring : 3) * dt;
        float damping = MathF.Exp(-(grounded ? c.SuspensionDamping : 0.5f) * dt);
        angular.X *= damping;
        angular.Z *= damping;
        if (wheels is WheelSupport supports)
        {
            float[] compression = [supports.Compression.X, supports.Compression.Y, supports.Compression.Z, supports.Compression.W];
            for (int index = 0; index < 4; index++)
            {
                if (compression[index] <= 0)
                {
                    continue;
                }

                Vector3 offset = (right * (index % 2 == 0 ? -0.85f : 0.85f)) + (forward * (index < 2 ? c.Wheelbase / 2 : -c.Wheelbase / 2));
                float wheelVelocity = observed.LinearVelocity.Y + Vector3.Cross(observed.AngularVelocity, offset).Y;
                float force = Math.Clamp((compression[index] * c.WheelSpring) - (wheelVelocity * c.WheelDamping), 0, c.Gravity * 6) / 4;
                velocity += Vector3.UnitY * force * dt;
                Vector3 torque = Vector3.Cross(offset, Vector3.UnitY * force);
                angular += torque / (c.Wheelbase * c.Wheelbase / 3) * dt;
            }
        }

        velocity -= Vector3.UnitY * (c.Gravity * dt);
        float landing = grounded && !State.Grounded ? Math.Clamp(-State.Physics.LinearVelocity.Y / 12, 0, 1) : Math.Max(0, State.LandingIntensity - (dt * 3));
        bool sliding = grounded && Math.Abs(lateral) > 1 && rearSlip > 0.35f;
        var physics = new VehiclePhysicsState(observed.Position, observed.Orientation, Limit(velocity, c.MaximumPhysicsSpeed), Limit(angular, c.MaximumAngularSpeed));
        State = new VehicleState(input.Tick, physics, grounded, sliding, wheel, handbrake, currentSurface, frontSlip, rearSlip, longAcceleration, sideAcceleration, landing, wheels ?? default);
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

    private static (float Side, float Drive, float Slip) Tire(float lateral, float longitudinal, float capacity, float lateralFraction = 1)
    {
        float demand = MathF.Sqrt((lateral * lateral) + (longitudinal * longitudinal));
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
        return (lateral * scale * lateralFraction, longitudinal * scale, Math.Clamp(1 - (scale * lateralFraction), 0, 1));
    }
}
