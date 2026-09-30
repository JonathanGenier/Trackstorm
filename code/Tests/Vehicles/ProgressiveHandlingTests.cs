using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

[TestFixture]
internal sealed class ProgressiveHandlingTests
{
    [Test]
    public void PowerBreakawayFadesWithRoadSpeedWithoutChangingWheelAuthority()
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
        Assert.That(low.PowerSlip, Is.GreaterThan(0.3f));
        Assert.That(medium.PowerSlip, Is.LessThan(low.PowerSlip));
        Assert.That(fast.PowerSlip, Is.LessThan(0.001f));
        Assert.That(fast.SteeringAngle, Is.EqualTo(low.SteeringAngle).Within(0.0001));
    }

    [Test]
    public void TapAndHoldHaveDifferentRearBrakingAndCoherentRelease()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -20), Vector3.Zero);
        var movement = new VehicleMovement(new(), pose);
        for (ulong tick = 1; tick <= 6; tick++) movement.Step(new(tick, 0, 0, 0, InputButtons.Drift, 0, 0), pose, Vector3.UnitY);
        var tap = movement.State;
        for (ulong tick = 7; tick <= 36; tick++) movement.Step(new(tick, 0, 0, 0, InputButtons.Drift, 0, 0), pose, Vector3.UnitY);
        var held = movement.State;
        Assert.That(tap.Handbrake, Is.InRange(0.19f, 0.21f));
        Assert.That(held.Handbrake, Is.EqualTo(1));
        Assert.That(held.LongitudinalAcceleration, Is.LessThan(tap.LongitudinalAcceleration));
        var released = movement.Step(new(37, 0, 0, 0, 0, 0, 0), pose, Vector3.UnitY);
        Assert.That(released.LongitudinalAcceleration, Is.GreaterThan(held.LongitudinalAcceleration).And.LessThan(0));
        Assert.That(released.Handbrake, Is.GreaterThan(0.9f).And.LessThan(1));
    }

    [TestCase("power_slip_full_speed", 6d)]
    [TestCase("power_slip_fade_speed", 20d)]
    [TestCase("reverse_engagement_speed", 0.5d)]
    public void NewControlsUseSharedConfigurationAndPersistence(string key, double value)
    {
        Assert.That(GameplayOptions.TryApply(new(), new Dictionary<string, double> { ["vehicle." + key] = value }, out var edited, out _), Is.True);
        Assert.That(GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(1, new(1, edited))).State.Configuration, Is.EqualTo(edited));
        Assert.That(DeveloperSettingsFile.Read(DeveloperSettingsFile.Read("", new()).Write(edited), new()).Configuration, Is.EqualTo(edited));
    }
}
