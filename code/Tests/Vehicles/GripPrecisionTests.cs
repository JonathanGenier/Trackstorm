using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

/// <summary>Normal tire authority improves without changing suspension, airborne authority or input portability.</summary>
[TestFixture]
internal sealed class GripPrecisionTests
{
    [TestCase(SurfaceType.Asphalt)]
    [TestCase(SurfaceType.Dirt)]
    [TestCase(SurfaceType.Grass)]
    public void BaselinePurchaseSubstantiallyReducesLateralDrift(SurfaceType surface)
    {
        var tuning = new VehicleConfiguration();
        var old = tuning with { AsphaltGrip = 1, Dirt = new(1.25f, 1.15f, 0.95f), Grass = new(1.25f, 1.4f, 0.9f) };
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(6, 0, -25), Vector3.Zero);
        var current = new VehicleMovement(tuning, pose).Step(new(1, 0, 0, 0, 0, 0, 0), pose, Vector3.UnitY, surface: surface);
        var baseline = new VehicleMovement(old, pose).Step(new(1, 0, 0, 0, 0, 0, 0), pose, Vector3.UnitY, surface: surface);
        Assert.That(6 - current.Physics.LinearVelocity.X, Is.GreaterThan((6 - baseline.Physics.LinearVelocity.X) * 1.4f));
        Assert.That(current.Physics.LinearVelocity.Y, Is.EqualTo(baseline.Physics.LinearVelocity.Y));
    }

    [Test]
    public void DigitalRatesAndAsphaltGripUseTheSharedConfigurationTransaction()
    {
        var edits = new Dictionary<string, double> { ["input.steering_rise"] = 0.8, ["input.steering_return"] = 1.2, ["input.steering_reversal"] = 1.6, ["vehicle.asphalt_grip"] = 2.4, ["input.throttle_rise"] = 2, ["input.throttle_release"] = 3, ["input.brake_rise"] = 8, ["input.brake_release"] = 9 };
        Assert.That(GameplayOptions.TryApply(new(), edits, out var tuned, out _), Is.True);
        Assert.That(GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(1, new(1, tuned))).State.Configuration, Is.EqualTo(tuned));
        foreach (string key in edits.Keys)
        foreach (double value in new[] { -1d, double.NaN, double.PositiveInfinity, 101d })
        {
            Assert.That(GameplayOptions.TryApply(tuned, new Dictionary<string, double> { [key] = value }, out var rejected, out _), Is.False);
            Assert.That(rejected, Is.EqualTo(tuned));
        }
    }
}
