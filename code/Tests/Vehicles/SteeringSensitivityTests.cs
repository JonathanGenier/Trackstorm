using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

[TestFixture]
internal sealed class SteeringSensitivityTests
{
    [Test]
    public void SpeedEnvelopeIsContinuousMonotonicAndPreservesParkingRange()
    {
        var tuning = new VehicleConfiguration();
        Assert.That(tuning.SteeringLimit(0), Is.EqualTo(tuning.SteeringAngle));
        Assert.That(tuning.SteeringLimit(8), Is.EqualTo(tuning.SteeringAngle));
        float previous = tuning.SteeringAngle;
        for (int i = 0; i <= 650; i++)
        {
            float limit = tuning.SteeringLimit(i / 10f);
            Assert.That(limit, Is.InRange(tuning.SteeringAngle * tuning.HighSpeedSteeringScale, previous + 0.000001f));
            Assert.That(previous - limit, Is.LessThan(0.004f));
            previous = limit;
        }
        Assert.That(previous, Is.EqualTo(0.225f).Within(0.000001f));
    }

    [TestCase(SurfaceType.Asphalt)]
    [TestCase(SurfaceType.Dirt)]
    [TestCase(SurfaceType.Grass)]
    public void SustainedPoweredTurnCannotCreateAutomaticSlip(SurfaceType surface)
    {
        foreach (float speed in new[] { 0f, 6f, 16f, 40f })
        foreach (short steering in new short[] { -32767, -2000, 0, 2000, 32767 })
        {
            var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -speed), Vector3.Zero);
            var movement = new VehicleMovement(new(), pose);
            for (ulong tick = 1; tick <= 300; tick++)
                movement.Step(new(tick, steering, 65535, 0, 0, 0, 0), pose, Vector3.UnitY, surface: surface);
            Assert.That(movement.State.PowerSlip, Is.Zero);
            Assert.That(movement.State.Handbrake, Is.Zero);
        }
    }

    [Test]
    public void SpeedChangeIsRateBoundedAndReplayUsesTheSameObservedRoadSpeed()
    {
        var tuning = new VehicleConfiguration();
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -4), Vector3.Zero);
        var movement = new VehicleMovement(tuning, pose);
        movement.Restore(new(0, pose, true, false, tuning.SteeringAngle, 0));
        var replay = new VehicleMovement(tuning, pose);
        replay.Restore(VehicleStateCodec.Decode(VehicleStateCodec.Encode(movement.State)));
        // A sideways slide counts as road speed; it must not regain parking range.
        var fast = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(40, 0, 0), Vector3.Zero);
        var input = new InputFrame(1, 32767, 0, 0, 0, 0, 0);
        movement.Step(input, fast, Vector3.UnitY);
        replay.Step(input, fast, Vector3.UnitY);
        Assert.That(replay.State, Is.EqualTo(movement.State));
        Assert.That(movement.State.SteeringAngle, Is.InRange(tuning.SteeringAngle - tuning.SteeringResponse / 60, tuning.SteeringAngle - 0.001f));
    }

    [TestCase("vehicle.high_speed_steering_scale", double.NaN)]
    [TestCase("vehicle.high_speed_steering_scale", 0.01)]
    [TestCase("vehicle.steering_full_speed", 40)]
    [TestCase("vehicle.steering_fade_speed", 8)]
    public void InvalidSpeedTuningRejectsWholeTransaction(string key, double value)
    {
        Assert.That(GameplayOptions.TryApply(new(), new Dictionary<string, double> { [key] = value }, out _, out _), Is.False);
    }

    [Test]
    public void OppositeIntentReturnsWheelFasterThanOrdinarySteerIn()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -6), Vector3.Zero);
        var movement = new VehicleMovement(new(), pose);
        movement.Restore(new(0, pose, true, false, 0.8f, 0));
        for (ulong tick = 1; tick <= 30; tick++)
            movement.Step(new(tick, -32767, 0, 0, 0, 0, 0), pose, Vector3.UnitY);
        Assert.That(movement.State.SteeringAngle, Is.LessThan(0), "An opposing target crosses center within half a second without snapping.");
    }
}
