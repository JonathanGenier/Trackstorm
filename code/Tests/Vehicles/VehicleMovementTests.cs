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
        for (int index = 0; index < 600; index++)
        {
            GroundStep(movement, throttle: 65535);
        }

        Assert.That(-movement.State.Physics.LinearVelocity.Z, Is.EqualTo(28).Within(0.001));
        float before = -movement.State.Physics.LinearVelocity.Z;
        GroundStep(movement, brake: 65535);
        Assert.That(-movement.State.Physics.LinearVelocity.Z, Is.LessThan(before).And.GreaterThan(before - 1));
        for (int index = 0; index < 600; index++)
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
        for (int index = 0; index < 600; index++)
        {
            GroundStep(movement, throttle: 65535);
        }

        Assert.That(-movement.State.Physics.LinearVelocity.Z, Is.EqualTo(10).Within(0.001));
        GroundStep(movement, brake: 65535);
        Assert.That(-movement.State.Physics.LinearVelocity.Z, Is.InRange(9.8f, 10f));
        for (int index = 0; index < 600; index++)
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
        Assert.That(low.State.SteeringAngle, Is.InRange(0.01f, 0.05f));
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
        Assert.That(a.Physics.LinearVelocity.X, Is.GreaterThan(4.8f));
        Assert.That(Math.Abs(a.LateralAcceleration), Is.LessThanOrEqualTo(1.05f * 9.81f));
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
