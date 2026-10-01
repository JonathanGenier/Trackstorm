using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

[TestFixture]
internal sealed class ProgressiveHandlingTests
{
    [Test]
    public void OrdinaryPoweredSteeringNeverBuildsAutomaticSlip()
    {
        VehicleState Drive(float speed)
        {
            var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -speed), Vector3.Zero);
            var movement = new VehicleMovement(new(), pose);
            for (ulong tick = 1; tick <= 240; tick++)
                movement.Step(new(tick, 32767, 65535, 0, 0, 0, 0), pose, Vector3.UnitY, surface: SurfaceType.Dirt);
            return movement.State;
        }
        var low = Drive(6);
        var medium = Drive(12);
        var fast = Drive(20);
        Assert.That(low.PowerSlip, Is.Zero);
        Assert.That(medium.PowerSlip, Is.Zero);
        Assert.That(fast.PowerSlip, Is.LessThan(0.001f));
        Assert.That(fast.SteeringAngle, Is.LessThan(low.SteeringAngle));
    }

    [Test]
    public void TapAndHoldHaveDifferentRearBrakingAndCoherentRelease()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -20), Vector3.Zero);
        var movement = new VehicleMovement(new(), pose);
        for (ulong tick = 1; tick <= 6; tick++) movement.Step(new(tick, 0, 0, 0, InputButtons.Drift, 0, 0), pose, Vector3.UnitY);
        var tap = movement.State;
        for (ulong tick = 7; tick <= 66; tick++) movement.Step(new(tick, 0, 0, 0, InputButtons.Drift, 0, 0), pose, Vector3.UnitY);
        var held = movement.State;
        Assert.That(tap.Handbrake, Is.InRange(0.09f, 0.11f));
        Assert.That(held.Handbrake, Is.EqualTo(1));
        Assert.That(held.LongitudinalAcceleration, Is.LessThan(tap.LongitudinalAcceleration));
        var released = movement.Step(new(67, 0, 0, 0, 0, 0, 0), pose, Vector3.UnitY);
        Assert.That(released.LongitudinalAcceleration, Is.GreaterThan(held.LongitudinalAcceleration).And.LessThan(0));
        Assert.That(released.Handbrake, Is.GreaterThan(0.9f).And.LessThan(1));
    }

    [TestCase(-20f)]
    [TestCase(0f)]
    [TestCase(20f)]
    public void LockedRearUsesActualFrontContactVelocityThroughBroadside(float forwardSpeed)
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(-12, 0, -forwardSpeed), new(0, -3, 0));
        VehicleState Drive(float reserve)
        {
            var movement = new VehicleMovement(new() { DirtSteeringReserve = reserve }, pose);
            movement.Restore(new(0, pose, true, true, 0.8f, 1, SurfaceType.Dirt));
            return movement.Step(new(1, 30000, 65535, 0, InputButtons.Drift, 0, 0), pose, Vector3.UnitY, surface: SurfaceType.Dirt);
        }
        // Powered line-following assistance must not inject extra torque into a locked-rear slide.
        Assert.That(Drive(1), Is.EqualTo(Drive(0)));
    }

    [TestCase("steering_full_speed", 6d)]
    [TestCase("steering_fade_speed", 30d)]
    [TestCase("high_speed_steering_scale", 0.3d)]
    [TestCase("steering_counter_response", 3d)]
    [TestCase("reverse_engagement_speed", 0.5d)]
    public void NewControlsUseSharedConfigurationAndPersistence(string key, double value)
    {
        Assert.That(GameplayOptions.TryApply(new(), new Dictionary<string, double> { ["vehicle." + key] = value }, out var edited, out _), Is.True);
        Assert.That(GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(1, new(1, edited))).State.Configuration, Is.EqualTo(edited));
        Assert.That(DeveloperSettingsFile.Read(DeveloperSettingsFile.Read("", new()).Write(edited), new()).Configuration, Is.EqualTo(edited));
    }
    [Test]
    public void HeavierDefaultPreservesApprovedDriveBrakeAndSteeringForces()
    {
        var current = new VehicleConfiguration();
        var previous = current with { Mass = 1400, Acceleration = 24, Braking = 45, ReverseAcceleration = 8, HandbrakeBraking = 30, Grip = 26 };
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -20), Vector3.Zero);
        var heavier = new VehicleMovement(current, pose);
        var approved = new VehicleMovement(previous, pose);
        for (ulong tick = 1; tick <= 300; tick++)
        {
            var input = new InputFrame(tick, tick < 150 ? (short)8000 : (short)-8000,
                tick < 100 ? ushort.MaxValue : (ushort)0, tick >= 200 ? ushort.MaxValue : (ushort)0,
                tick is >= 100 and < 150 ? InputButtons.Drift : 0, 0, 0);
            var a = heavier.Step(input, pose, Vector3.UnitY, surface: SurfaceType.Dirt);
            var b = approved.Step(input, pose, Vector3.UnitY, surface: SurfaceType.Dirt);
            Assert.That(Vector3.Distance(a.Physics.LinearVelocity, b.Physics.LinearVelocity), Is.LessThan(0.00001f));
            Assert.That(Vector3.Distance(a.Physics.AngularVelocity, b.Physics.AngularVelocity), Is.LessThan(0.00001f));
        }
        Assert.That(current.Mass, Is.EqualTo(3000));
    }
}
