using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

/// <summary>Boundary, state-machine, and deterministic replay checks for authoritative movement helpers.</summary>
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
        Assert.That(-movement.State.Physics.LinearVelocity.Z, Is.EqualTo(before - (25f / 60)).Within(0.001));
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
        var configuration = new VehicleConfiguration { TicksPerSecond = 120, ForwardSpeed = 10, ReverseSpeed = 4, Acceleration = 6, Braking = 12, BoostSpeed = 5, MaximumPhysicsSpeed = 20 };
        var movement = new VehicleMovement(configuration, new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        GroundStep(movement, throttle: 65535);
        Assert.That(-movement.State.Physics.LinearVelocity.Z, Is.EqualTo(0.05f).Within(0.000001));
        for (int index = 0; index < 600; index++)
        {
            GroundStep(movement, throttle: 65535);
        }

        Assert.That(-movement.State.Physics.LinearVelocity.Z, Is.EqualTo(10).Within(0.001));
        GroundStep(movement, brake: 65535);
        Assert.That(-movement.State.Physics.LinearVelocity.Z, Is.EqualTo(9.9f).Within(0.001));
        for (int index = 0; index < 600; index++)
        {
            GroundStep(movement, brake: 65535);
        }

        Assert.That(movement.State.Physics.LinearVelocity.Z, Is.EqualTo(4).Within(0.001));
    }

    /// <summary>Authority-disabled vehicles retain gravity but cannot drive, steer or charge a boost.</summary>
    [Test]
    public void DisabledDrive_IgnoresDrivingControls()
    {
        VehicleMovement movement = Create();
        VehicleState state = movement.Step(Frame(1, throttle: 65535, steering: 32767, drift: true), movement.State.Physics, Vector3.UnitY, false);
        Assert.That(state.Physics.LinearVelocity.Y, Is.EqualTo(-0.4f).Within(0.000001));
        Assert.That(state.Physics.LinearVelocity.X, Is.Zero);
        Assert.That(state.Physics.LinearVelocity.Z, Is.Zero);
        Assert.That(state.Physics.AngularVelocity, Is.EqualTo(Vector3.Zero));
        Assert.That(state.Drifting, Is.False);
        Assert.That(state.BoostTicks, Is.Zero);
    }

    /// <summary>Steering is neutral at rest, normalized at bounds, slower at high speed, and reversed in reverse.</summary>
    [Test]
    public void Steering_IsSpeedSensitiveAndNormalized()
    {
        Assert.That(VehicleMovement.Steering(1, 0, 2), Is.Zero);
        Assert.That(VehicleMovement.Steering(2, 10, 2), Is.EqualTo(VehicleMovement.Steering(1, 10, 2)));
        Assert.That(VehicleMovement.Steering(-2, 10, 2), Is.EqualTo(-VehicleMovement.Steering(1, 10, 2)));
        Assert.That(VehicleMovement.Steering(1, -10, 2), Is.EqualTo(-VehicleMovement.Steering(1, 10, 2)));
        Assert.That(Math.Abs(VehicleMovement.Steering(1, 50, 2)), Is.LessThan(Math.Abs(VehicleMovement.Steering(1, 10, 2))));
        Assert.Throws<ArgumentException>(() => VehicleMovement.Steering(float.NaN, 10, 2));
    }

    /// <summary>Only a sufficiently long grounded turn yields one boost on release.</summary>
    [Test]
    public void Drift_ChargesHoldsReleasesAndExpires()
    {
        VehicleMovement movement = Create(15);
        GroundStep(movement, steering: 20000, drift: true);
        Assert.That(movement.State.Drifting, Is.True);
        Assert.That(movement.State.DriftTicks, Is.EqualTo(1));
        GroundStep(movement, steering: 20000);
        Assert.That(movement.State.BoostTicks, Is.Zero);
        for (int index = 0; index < 45; index++)
        {
            GroundStep(movement, throttle: 65535, steering: 20000, drift: true);
        }

        GroundStep(movement, steering: 20000);
        Assert.That(movement.State.Drifting, Is.False);
        Assert.That(movement.State.DriftTicks, Is.Zero);
        Assert.That(movement.State.BoostTicks, Is.EqualTo(54));
        for (int index = 0; index < 60; index++)
        {
            GroundStep(movement);
        }

        Assert.That(movement.State.BoostTicks, Is.Zero);
    }

    /// <summary>Standing still, straight driving, airborne drift and early release cannot create boost.</summary>
    /// <param name="speed">Longitudinal speed.</param>
    /// <param name="steering">Logical steering strength.</param>
    /// <param name="grounded">Whether support is present.</param>
    [TestCase(0, 20000, true)]
    [TestCase(15, 0, true)]
    [TestCase(15, 20000, false)]
    public void InvalidDrift_CannotCharge(float speed, short steering, bool grounded)
    {
        VehicleMovement movement = Create(speed);
        for (int index = 0; index < 80; index++)
        {
            var body = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(0, 0, -speed), Vector3.Zero);
            movement.Step(Frame(movement.State.Tick + 1, steering: steering, drift: true), body, grounded ? Vector3.UnitY : Vector3.Zero);
        }

        movement.Step(Frame(movement.State.Tick + 1), movement.State.Physics, grounded ? Vector3.UnitY : Vector3.Zero);
        Assert.That(movement.State.DriftTicks, Is.Zero);
        Assert.That(movement.State.BoostTicks, Is.Zero);
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
        Assert.That(movement.State.Physics.LinearVelocity.Y, Is.EqualTo(-0.8f).Within(0.00001));
        GroundStep(movement);
        Assert.That(movement.State.Grounded, Is.True);
        Assert.That(movement.State.Physics.LinearVelocity.Y, Is.EqualTo(-0.4f).Within(0.00001));
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
