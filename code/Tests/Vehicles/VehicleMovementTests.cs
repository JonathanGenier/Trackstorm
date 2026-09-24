using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

/// <summary>Boundary, force-response, and deterministic replay checks for authoritative movement helpers.</summary>
[TestFixture]
internal sealed class VehicleMovementTests
{
    /// <summary>Forward and reverse drive limits hold over sustained input; braking first stops forward motion.</summary>
    [Test]
    public void Drive_ObeysLimitsAndBrakesBeforeReverse()
    {
        VehicleMovement movement = Create();
        for (int index = 0; index < 1800; index++)
        {
            GroundStep(movement, throttle: 65535);
        }

        Assert.That(-movement.State.Physics.LinearVelocity.Z, Is.EqualTo(44.44f).Within(0.001));
        float before = -movement.State.Physics.LinearVelocity.Z;
        GroundStep(movement, brake: 65535);
        Assert.That(-movement.State.Physics.LinearVelocity.Z, Is.LessThan(before).And.GreaterThan(before - 1));
        for (int index = 0; index < 1800; index++)
        {
            GroundStep(movement, brake: 65535);
        }

        Assert.That(movement.State.Physics.LinearVelocity.Z, Is.EqualTo(11).Within(0.001));
        GroundStep(movement, throttle: 65535);
        Assert.That(movement.State.Physics.LinearVelocity.Z, Is.LessThan(11).And.GreaterThan(0));
    }

    /// <summary>Nondefault rate and drive tuning control the simulation rather than hardcoded defaults.</summary>
    [Test]
    public void Drive_UsesConfiguredRateAccelerationBrakingAndLimits()
    {
        var configuration = new VehicleConfiguration { TicksPerSecond = 120, ForwardSpeed = 10, ReverseSpeed = 4, Acceleration = 6, Braking = 12, MaximumPhysicsSpeed = 20 };
        var movement = new VehicleMovement(configuration, new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        GroundStep(movement, throttle: 65535);
        Assert.That(-movement.State.Physics.LinearVelocity.Z, Is.InRange(0.01f, 0.05f));
        for (int index = 0; index < 1800; index++)
        {
            GroundStep(movement, throttle: 65535);
        }

        Assert.That(-movement.State.Physics.LinearVelocity.Z, Is.EqualTo(10).Within(0.001));
        GroundStep(movement, brake: 65535);
        Assert.That(-movement.State.Physics.LinearVelocity.Z, Is.InRange(9.8f, 10f));
        for (int index = 0; index < 1800; index++)
        {
            GroundStep(movement, brake: 65535);
        }

        Assert.That(movement.State.Physics.LinearVelocity.Z, Is.EqualTo(4).Within(0.001));
    }

    /// <summary>Authority-disabled vehicles retain gravity but ignore driving controls.</summary>
    [Test]
    public void DisabledDrive_IgnoresDrivingControls()
    {
        VehicleMovement movement = Create();
        VehicleState state = movement.Step(Frame(1, throttle: 65535, steering: 32767, drift: true), movement.State.Physics, Vector3.UnitY, false);
        Assert.That(state.Physics.LinearVelocity.Y, Is.EqualTo(-9.81f / 60).Within(0.000001));
        Assert.That(state.Physics.LinearVelocity.X, Is.Zero);
        Assert.That(state.Physics.LinearVelocity.Z, Is.Zero);
        Assert.That(state.Physics.AngularVelocity, Is.EqualTo(Vector3.Zero));
        Assert.That(state.Drifting, Is.False);
        Assert.That(state.Handbrake, Is.Zero);
    }

    /// <summary>Actual wheel commands are progressive, precise and calmer at speed.</summary>
    [Test]
    public void Steering_IsProgressiveAndSpeedSensitive()
    {
        VehicleMovement low = Create(2);
        VehicleMovement high = Create(40);
        VehicleMovement analog = Create(2);
        GroundStep(low, steering: 32767);
        Assert.That(low.State.SteeringAngle, Is.InRange(0.035f, 0.045f));
        for (int index = 0; index < 60; index++)
        {
            low.Step(Frame(low.State.Tick + 1, steering: 32767), Create(2).State.Physics, Vector3.UnitY);
            high.Step(Frame(high.State.Tick + 1, steering: 32767), Create(40).State.Physics, Vector3.UnitY);
            analog.Step(Frame(analog.State.Tick + 1, steering: 8192), Create(2).State.Physics, Vector3.UnitY);
        }

        Assert.That(low.State.SteeringAngle, Is.GreaterThan(0.5f));
        Assert.That(high.State.SteeringAngle, Is.LessThan(low.State.SteeringAngle / 3));
        Assert.That(analog.State.SteeringAngle, Is.EqualTo(low.State.SteeringAngle / 4).Within(0.0001f));
    }

    /// <summary>Handbrake consumes speed and releases progressively without an acceleration reward.</summary>
    [Test]
    public void Handbrake_ScrubsSpeedAndRecoversProgressively()
    {
        VehicleMovement movement = Create(15);
        VehicleMovement coast = Create(15);
        for (int index = 0; index < 90; index++)
        {
            GroundStep(movement, drift: true);
            GroundStep(coast);
        }

        Assert.That(movement.State.Handbrake, Is.EqualTo(1));
        Assert.That(movement.State.CommandSpeed, Is.LessThan(coast.State.CommandSpeed - 3));
        float speed = movement.State.CommandSpeed;
        GroundStep(movement);
        Assert.That(movement.State.Handbrake, Is.InRange(0.9f, 0.99f));
        Assert.That(movement.State.CommandSpeed, Is.LessThanOrEqualTo(speed));
        for (int index = 0; index < 30; index++)
        {
            GroundStep(movement);
        }

        Assert.That(movement.State.Handbrake, Is.Zero);
    }

    /// <summary>Mass changes engine and brake response without discarding external sideways or yaw momentum.</summary>
    [Test]
    public void MassAndImpulses_RemainPhysical()
    {
        var body = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(8, 0, -20), new Vector3(0, 2, 0));
        var light = new VehicleMovement(new(), body);
        var heavy = new VehicleMovement(new() { Mass = 1800 }, body);
        VehicleState a = light.Step(Frame(1, brake: 65535), body, Vector3.UnitY);
        VehicleState b = heavy.Step(Frame(1, brake: 65535), body, Vector3.UnitY);
        Assert.That(-b.Physics.LinearVelocity.Z, Is.GreaterThan(-a.Physics.LinearVelocity.Z));
        Assert.That(a.Physics.LinearVelocity.X, Is.GreaterThan(7));
        Assert.That(a.Physics.AngularVelocity.Y, Is.GreaterThan(1.5f));
        VehicleState airborne = Create().Step(Frame(1, throttle: 65535, steering: 32767), body, Vector3.Zero);
        Assert.That(airborne.Physics.LinearVelocity.X, Is.EqualTo(8));
        Assert.That(airborne.Physics.LinearVelocity.Z, Is.EqualTo(-20));
        Assert.That(airborne.Physics.AngularVelocity.Y, Is.EqualTo(2));
    }

    /// <summary>Tire force cannot instantly redirect velocity; braking/drive share lateral grip and excess demand saturates progressively.</summary>
    [Test]
    public void Tires_BoundCorneringAndCombinedDemand()
    {
        VehicleMovement coast = Create(25);
        VehicleMovement drive = Create(25);
        var body = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(5, 0, -25), Vector3.Zero);
        VehicleState a = coast.Step(Frame(1, steering: 32767), body, Vector3.UnitY);
        VehicleState b = drive.Step(Frame(1, throttle: 65535, steering: 32767), body, Vector3.UnitY);
        Assert.That(a.Physics.LinearVelocity.X, Is.GreaterThanOrEqualTo(5 - (coast.Configuration.TireFriction * coast.Configuration.Gravity / 60)));
        Assert.That(Math.Abs(a.LateralAcceleration), Is.LessThanOrEqualTo(coast.Configuration.TireFriction * coast.Configuration.Gravity));
        Assert.That(b.RearSlip, Is.GreaterThan(a.RearSlip));
        Assert.That(a.FrontSlip, Is.InRange(0.01f, 1f));
        Assert.That(b.Physics.LinearVelocity.X, Is.GreaterThan(a.Physics.LinearVelocity.X));
    }

    /// <summary>Each spring reacts independently, damps landing motion, and contributes real body torque.</summary>
    [Test]
    public void Suspension_ReactsToIndividualWheelsAndLanding()
    {
        var body = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(0, -3, 0), Vector3.Zero);
        VehicleState flat = Create().Step(Frame(1), body, Vector3.UnitY, wheels: new WheelSupport(new Vector4(0.1f)));
        VehicleState bump = Create().Step(Frame(1), body, Vector3.UnitY, wheels: new WheelSupport(new Vector4(0.3f, 0, 0, 0)));
        Assert.That(flat.Physics.LinearVelocity.Y, Is.GreaterThan(-3));
        Assert.That(flat.Physics.AngularVelocity.Length(), Is.LessThan(0.00001f));
        Assert.That(bump.Physics.AngularVelocity.X, Is.GreaterThan(0));
        Assert.That(bump.Physics.AngularVelocity.Z, Is.LessThan(0));
        Assert.That(bump.Wheels.Compression.X, Is.EqualTo(0.3f));
        Assert.Throws<ArgumentException>(() => new WheelSupport(new Vector4(float.NaN)));
    }

    /// <summary>Ground observations drive grounded/airborne/landing transitions; gravity remains fixed-step.</summary>
    [Test]
    public void Grounding_HandlesTakeoffAndLanding()
    {
        VehicleMovement movement = Create();
        GroundStep(movement);
        Assert.That(movement.State.Grounded, Is.True);
        movement.Step(Frame(2), movement.State.Physics, Vector3.Zero);
        Assert.That(movement.State.Grounded, Is.False);
        Assert.That(movement.State.Physics.LinearVelocity.Y, Is.EqualTo(-9.81f / 30).Within(0.00001));
        GroundStep(movement);
        Assert.That(movement.State.Grounded, Is.True);
        Assert.That(movement.State.Physics.LinearVelocity.Y, Is.EqualTo(-9.81f / 60).Within(0.00001));
    }

    /// <summary>Grip dissipates lateral motion; drifting retains more side slip.</summary>
    [Test]
    public void Grip_DriftRetainsLateralVelocity()
    {
        var observation = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(5, 0, -15), Vector3.Zero);
        VehicleState normal = Create().Step(Frame(1, steering: 20000), observation, Vector3.UnitY);
        VehicleState drift = Create().Step(Frame(1, steering: 20000, drift: true), observation, Vector3.UnitY);
        Assert.That(drift.Physics.LinearVelocity.X, Is.GreaterThan(normal.Physics.LinearVelocity.X).And.LessThan(5));
    }

    /// <summary>Identical input and initial pure state yield identical snapshots; restoration has no hidden timer state.</summary>
    [Test]
    public void Replay_AndRestoreProduceIdenticalOutputs()
    {
        VehicleMovement first = Create(15);
        VehicleMovement second = Create(15);
        for (ulong tick = 1; tick <= 600; tick++)
        {
            InputFrame input = Frame(tick, throttle: 65535, steering: tick % 100 < 70 ? (short)20000 : (short)-10000, drift: tick % 100 < 60);
            VehiclePhysicsState observed = Integrate(first.State.Physics);
            VehicleState left = first.Step(input, observed, tick % 100 < 90 ? Vector3.UnitY : Vector3.Zero);
            VehicleState right = second.Step(input, Integrate(second.State.Physics), tick % 100 < 90 ? Vector3.UnitY : Vector3.Zero);
            Assert.That(right, Is.EqualTo(left));
            if (tick == 200)
            {
                second = Create();
                second.Restore(first.State);
            }
        }
    }

    /// <summary>Invalid ticks, observations, and tuning cannot partially mutate movement state.</summary>
    [Test]
    public void InvalidInput_IsRejectedAtomically()
    {
        VehicleMovement movement = Create();
        VehicleState before = movement.State;
        Assert.Throws<ArgumentException>(() => movement.Step(Frame(2), before.Physics, Vector3.UnitY));
        Assert.Throws<ArgumentException>(() => movement.Step(Frame(1), before.Physics, new Vector3(0, 4, 0)));
        Assert.Throws<ArgumentException>(() => movement.Restore(default));
        Assert.That(movement.State, Is.EqualTo(before));
        Assert.Throws<ArgumentException>(() => new VehicleConfiguration { Acceleration = float.NaN }.Validate());
        Assert.Throws<ArgumentException>(() => new VehicleConfiguration { TicksPerSecond = 0 }.Validate());
        Assert.Throws<ArgumentException>(() => new VehiclePhysicsState(Vector3.Zero, default, Vector3.Zero, Vector3.Zero));
        Assert.That(VehicleMovement.Limit(new Vector3(100, 0, 0), 65).Length(), Is.EqualTo(65));
    }

    /// <summary>Light vehicles cannot multiply the final speed-cap remainder into an overshoot.</summary>
    [Test]
    public void LightMassRespectsDriveCapsAndHighGripDoesNotReverseSlip()
    {
        var tuning = new VehicleConfiguration { Mass = 100, Grip = 1000 };
        var body = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(0, 0, -27.999f), Vector3.Zero);
        VehicleState forward = new VehicleMovement(tuning, body).Step(Frame(1, throttle: 65535), body, Vector3.UnitY);
        Assert.That(-forward.Physics.LinearVelocity.Z, Is.LessThanOrEqualTo(tuning.ForwardSpeed));
        body = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(0, 0, 10.999f), Vector3.Zero);
        VehicleState reverse = new VehicleMovement(tuning, body).Step(Frame(1, brake: 65535), body, Vector3.UnitY);
        Assert.That(reverse.Physics.LinearVelocity.Z, Is.LessThanOrEqualTo(tuning.ReverseSpeed));
        body = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(0.01f, 0, -10), Vector3.Zero);
        VehicleState grip = new VehicleMovement(tuning, body).Step(Frame(1), body, Vector3.UnitY);
        Assert.That(grip.Physics.LinearVelocity.X, Is.InRange(0, 0.01f));
    }

    /// <summary>Handbrake keeps front authority, retains rear friction, and blocks drive from cancelling rear braking.</summary>
    [Test]
    public void HandbrakePreservesFrontGripAndRecoversRearTraction()
    {
        var body = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(5, 0, -14), Vector3.Zero);
        VehicleMovement held = Create();
        VehicleMovement powered = Create();
        held.Restore(new VehicleState(0, body, true, false, 0, 1));
        powered.Restore(held.State);
        VehicleState normal = Create().Step(Frame(1), body, Vector3.UnitY);
        VehicleState locked = held.Step(Frame(1, drift: true), body, Vector3.UnitY);
        VehicleState throttle = powered.Step(Frame(1, throttle: 65535, drift: true), body, Vector3.UnitY);
        float FrontForce(VehicleState state)
        {
            float yaw = state.Physics.AngularVelocity.Y / MathF.Exp(-held.Configuration.StabilityDamping / 60);
            float difference = yaw * 60 * held.Configuration.Wheelbase * 2 / 3;
            return (state.LateralAcceleration - difference) / 2;
        }

        Assert.That(FrontForce(locked), Is.EqualTo(FrontForce(normal)).Within(0.0001f));
        Assert.That(Math.Abs(FrontForce(locked)), Is.GreaterThan(4));
        Assert.That(Math.Abs(locked.LateralAcceleration - FrontForce(locked)), Is.GreaterThan(2));
        Assert.That(throttle.LongitudinalAcceleration, Is.EqualTo(locked.LongitudinalAcceleration));
        Assert.That(locked.LongitudinalAcceleration, Is.LessThan(-1));
        VehicleState release = held.Step(Frame(2), body, Vector3.UnitY);
        Assert.That(release.Handbrake, Is.InRange(0.8f, 0.99f));
        for (ulong tick = 3; tick <= 25; tick++)
        {
            held.Step(Frame(tick), body, Vector3.UnitY);
        }

        Assert.That(held.State.Handbrake, Is.Zero);
        Assert.That(Math.Abs(held.State.LateralAcceleration), Is.GreaterThan(Math.Abs(locked.LateralAcceleration)));
    }

    /// <summary>Release restores propulsion on the first tick while sideways momentum, yaw and grip recovery persist.</summary>
    /// <param name="speed">Forward entry speed.</param>
    /// <param name="delay">Ticks after release before throttle.</param>
    [TestCase(6f, 0)]
    [TestCase(16f, 0)]
    [TestCase(24f, 0)]
    [TestCase(16f, 6)]
    public void ReleasedHandbrakePowersThroughRemainingSlip(float speed, int delay)
    {
        var body = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(5, 0, -speed), new Vector3(0, 0.7f, 0));
        var movement = new VehicleMovement(new(), body);
        movement.Restore(new VehicleState(0, body, true, true, 0, 1));
        VehicleState held = movement.Step(Frame(1, throttle: 65535, drift: true), body, Vector3.UnitY);
        Assert.That(held.LongitudinalAcceleration, Is.LessThan(0));
        for (int index = 0; index < delay; index++)
        {
            movement.Step(Frame(movement.State.Tick + 1), body, Vector3.UnitY);
        }

        VehicleState released = movement.Step(Frame(movement.State.Tick + 1, throttle: 65535), body, Vector3.UnitY);
        Assert.That(released.LongitudinalAcceleration, Is.GreaterThan(2));
        Assert.That(-released.Physics.LinearVelocity.Z, Is.GreaterThan(speed));
        Assert.That(released.Physics.LinearVelocity.X, Is.GreaterThan(4.7f));
        Assert.That(released.Physics.AngularVelocity.Y, Is.GreaterThan(0.5f));
        Assert.That(released.Handbrake, Is.InRange(0.3f, 0.99f));
        Assert.That(released.Drifting, Is.True);
    }

    /// <summary>Propulsion remains analog and bounded by the supported axle even with extreme lateral demand.</summary>
    /// <param name="surface">Surface traction modifiers.</param>
    /// <param name="reverse">Whether the brake pedal requests reverse propulsion.</param>
    [TestCase(SurfaceType.Concrete, false)]
    [TestCase(SurfaceType.Mud, false)]
    [TestCase(SurfaceType.Concrete, true)]
    public void SlidingDriveRespectsPedalAndFrictionBudget(SurfaceType surface, bool reverse)
    {
        var tuning = new VehicleConfiguration();
        var body = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(20, 0, reverse ? 5 : -5), Vector3.Zero);
        float previous = 0;
        foreach (ushort pedal in new ushort[] { 0, 500, 4000, 16000, 65535 })
        {
            VehicleState state = new VehicleMovement(tuning, body).Step(Frame(1, throttle: reverse ? (ushort)0 : pedal, brake: reverse ? pedal : (ushort)0), body, Vector3.UnitY, surface: surface);
            float drive = Math.Abs(state.LongitudinalAcceleration);
            float capacity = tuning.TireFriction * tuning.Gravity * tuning.ResolveSurface(surface).Grip / 2;
            float yaw = state.Physics.AngularVelocity.Y / MathF.Exp(-tuning.StabilityDamping / 60);
            float rearSide = (state.LateralAcceleration + (yaw * 60 * tuning.Wheelbase * 2 / 3)) / 2;
            Assert.That(drive, Is.GreaterThanOrEqualTo(previous));
            Assert.That((drive * drive) + (rearSide * rearSide), Is.LessThanOrEqualTo((capacity * capacity) + 0.0001f));
            Assert.That(drive, Is.LessThanOrEqualTo((reverse ? tuning.ReverseAcceleration : tuning.Acceleration * tuning.ResolveSurface(surface).Acceleration) * pedal / 65535f));
            previous = drive;
        }

        Assert.That(previous, Is.GreaterThan(1.5f));
        VehicleState unsupported = new VehicleMovement(tuning, body).Step(Frame(1, throttle: 65535), body, Vector3.UnitY, wheels: new WheelSupport(Vector4.Zero));
        Assert.That(unsupported.LongitudinalAcceleration, Is.Zero);
    }

    /// <summary>A measured engine increase improves launch without changing speed caps or unrelated tuning.</summary>
    [Test]
    public void AccelerationIncreaseIsModestAndPreservesLimits()
    {
        VehicleMovement current = Create();
        var previous = new VehicleMovement(current.Configuration with { Acceleration = 8 }, current.State.Physics);
        for (int tick = 0; tick < 90; tick++)
        {
            GroundStep(current, throttle: 65535);
            GroundStep(previous, throttle: 65535);
        }

        float ratio = current.State.CommandSpeed / previous.State.CommandSpeed;
        TestContext.WriteLine($"90-tick acceleration: previous={previous.State.CommandSpeed:F3}, current={current.State.CommandSpeed:F3}, gain={(ratio - 1) * 100:F2}%");
        Assert.That(ratio, Is.GreaterThan(1).And.LessThan(current.Configuration.Acceleration / previous.Configuration.Acceleration));
        Assert.That(current.Configuration.ForwardSpeed, Is.EqualTo(previous.Configuration.ForwardSpeed));
        foreach (float invalid in new[] { -0.01f, 1.01f, float.NaN, float.PositiveInfinity })
        {
            Assert.Throws<ArgumentException>(() => new VehicleConfiguration { DriveTractionReserve = invalid }.Validate());
        }
    }

    /// <summary>Small road/contact disturbances decay under neutral steering and sustained propulsion.</summary>
    /// <param name="speed">Entry speed in metres per second.</param>
    [TestCase(5f)]
    [TestCase(20f)]
    [TestCase(35f)]
    public void NeutralThrottleSettlesSmallYawDisturbance(float speed)
    {
        var movement = new VehicleMovement(new(), new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(0.05f, 0, -speed), new Vector3(0, 0.03f, 0)));
        float peakYaw = 0;
        for (int tick = 0; tick < 600; tick++)
        {
            VehiclePhysicsState integrated = Integrate(movement.State.Physics);
            var observation = new VehiclePhysicsState(integrated.Position, integrated.Orientation, new Vector3(integrated.LinearVelocity.X, 0, integrated.LinearVelocity.Z), new Vector3(0, integrated.AngularVelocity.Y, 0));
            movement.Step(Frame(movement.State.Tick + 1, throttle: ushort.MaxValue), observation, Vector3.UnitY);
            peakYaw = Math.Max(peakYaw, Math.Abs(movement.State.Physics.AngularVelocity.Y));
        }

        float heading = Vector3.Dot(Vector3.Transform(-Vector3.UnitZ, movement.State.Physics.Orientation), -Vector3.UnitZ);
        TestContext.WriteLine($"Entry {speed}: peak yaw {peakYaw:F4}, final yaw {movement.State.Physics.AngularVelocity.Y:F4}, heading dot {heading:F4}");
        Assert.That(peakYaw, Is.LessThan(0.08f));
        Assert.That(Math.Abs(movement.State.Physics.AngularVelocity.Y), Is.LessThan(0.005f));
        Assert.That(heading, Is.GreaterThan(0.99f));
    }

    /// <summary>Lift-off loses speed smoothly without reversing or imposing the powered cap on external velocity.</summary>
    [Test]
    public void CoastDownPreservesRollingMomentumAndDecaysSmoothly()
    {
        VehicleMovement movement = Create(44.44f);
        for (int tick = 0; tick < 600; tick++)
        {
            float previous = movement.State.CommandSpeed;
            GroundStep(movement);
            Assert.That(movement.State.CommandSpeed, Is.InRange(previous * 0.99f, previous));
            Assert.That(movement.State.Physics.LinearVelocity.Z, Is.LessThan(0));
        }

        Assert.That(movement.State.CommandSpeed, Is.InRange(2.5f, 3f));
    }

    /// <summary>A raised wheel compresses the sprung body, then near-critical damping settles without pogo.</summary>
    [Test]
    public void SuspensionAbsorbsBumpAndSettles()
    {
        var tuning = new VehicleConfiguration();
        var movement = new VehicleMovement(tuning, new VehiclePhysicsState(new Vector3(0, VehicleDimensions.RideHeight, 0), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        float peak = 0;
        float minimumCompression = float.MaxValue;
        for (int tick = 0; tick < 240; tick++)
        {
            VehiclePhysicsState body = Integrate(movement.State.Physics);
            float road = tick < 30 ? 0.12f * MathF.Pow(MathF.Sin(MathF.PI * tick / 30), 2) : 0;
            float compression = Math.Max(0, tuning.SuspensionLength - (body.Position.Y - road));
            minimumCompression = Math.Min(minimumCompression, compression);
            movement.Step(Frame(movement.State.Tick + 1), body, compression > 0 ? Vector3.UnitY : Vector3.Zero, wheels: new WheelSupport(new Vector4(compression)));
            peak = Math.Max(peak, body.Position.Y - VehicleDimensions.RideHeight);
            if (tick > 120)
            {
                Assert.That(Math.Abs(body.Position.Y - VehicleDimensions.RideHeight), Is.LessThan(0.005f));
                Assert.That(Math.Abs(body.LinearVelocity.Y), Is.LessThan(0.02f));
            }
        }

        TestContext.WriteLine($"12 cm bump: chassis rise {peak:F3} m, minimum compression {minimumCompression:F3} m");
        Assert.That(peak, Is.InRange(0.02f, 0.09f));
        Assert.That(minimumCompression, Is.GreaterThan(0));
    }

    /// <summary>Independent damping controls affect only their direction and never create tensile ground support.</summary>
    [Test]
    public void CompressionAndReboundDampingHaveDistinctResponses()
    {
        float Step(float speed, VehicleConfiguration tuning)
        {
            var body = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.UnitY * speed, Vector3.Zero);
            return new VehicleMovement(tuning, body).Step(Frame(1), body, Vector3.UnitY, wheels: new WheelSupport(new Vector4(0.4f))).Physics.LinearVelocity.Y;
        }

        var baseline = new VehicleConfiguration();
        Assert.That(Step(-1, baseline with { WheelDamping = 22 }), Is.GreaterThan(Step(-1, baseline)));
        Assert.That(Step(-1, baseline with { WheelReboundDamping = 30 }), Is.EqualTo(Step(-1, baseline)));
        Assert.That(Step(0.2f, baseline with { WheelReboundDamping = 30 }), Is.LessThan(Step(0.2f, baseline)));
        Assert.That(Step(0.2f, baseline with { WheelDamping = 22 }), Is.EqualTo(Step(0.2f, baseline)));
        Assert.That(Step(10, baseline), Is.EqualTo(10 - baseline.Gravity / 60).Within(0.00001f));
    }

    /// <summary>Bump resistance adds support only near end stroke, remains bounded, and preserves airborne gravity.</summary>
    [Test]
    public void ProgressiveEndResistanceIsBoundedAndRequiresContact()
    {
        var tuning = new VehicleConfiguration();
        var body = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        float Step(float compression, VehicleConfiguration config) => new VehicleMovement(config, body).Step(Frame(1), body, Vector3.UnitY, wheels: new WheelSupport(new Vector4(compression))).Physics.LinearVelocity.Y;
        Assert.That(Step(0.3f, tuning with { WheelBumpSpring = 280 }), Is.EqualTo(Step(0.3f, tuning)));
        Assert.That(Step(0.5f, tuning with { WheelBumpStart = 0.4f }), Is.GreaterThan(Step(0.5f, tuning)));
        Assert.That(Step(0.7f, tuning with { WheelBumpSpring = 280 }), Is.GreaterThan(Step(0.7f, tuning)));
        Assert.That(Step(1, tuning with { WheelBumpSpring = 10000 }), Is.LessThanOrEqualTo(5 * tuning.Gravity / 60 + 0.00001f));
        Assert.That(Step(0, tuning), Is.EqualTo(-tuning.Gravity / 60));
    }

    private static VehicleMovement Create(float speed = 0) => new(new VehicleConfiguration(), new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(0, 0, -speed), Vector3.Zero));

    private static InputFrame Frame(ulong tick, ushort throttle = 0, ushort brake = 0, short steering = 0, bool drift = false) => new(tick, steering, throttle, brake, drift ? InputButtons.Drift : InputButtons.None, InputButtons.None, InputButtons.None);

    private static void GroundStep(VehicleMovement movement, ushort throttle = 0, ushort brake = 0, short steering = 0, bool drift = false)
    {
        Vector3 velocity = movement.State.Physics.LinearVelocity;
        velocity.Y = 0;
        movement.Step(Frame(movement.State.Tick + 1, throttle, brake, steering, drift), new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, velocity, Vector3.Zero), Vector3.UnitY);
    }

    private static VehiclePhysicsState Integrate(VehiclePhysicsState body)
    {
        Vector3 angular = body.AngularVelocity;
        Quaternion delta = angular.LengthSquared() > 0.000001f ? Quaternion.CreateFromAxisAngle(Vector3.Normalize(angular), angular.Length() / 60) : Quaternion.Identity;
        return new VehiclePhysicsState(body.Position + (body.LinearVelocity / 60), Quaternion.Normalize(delta * body.Orientation), body.LinearVelocity, body.AngularVelocity);
    }
}
