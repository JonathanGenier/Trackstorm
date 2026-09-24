using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

/// <summary>Rocket force behavior independent of engine demand and native wheel support.</summary>
[TestFixture]
internal sealed class NitroThrustTests
{
    [TestCase(0)]
    [TestCase(12)]
    public void RocketPropelsWithoutThrottleAndCombinesWithDrive(float speed)
    {
        var coast = Run(speed, 0, 0, true, default);
        var rocket = Run(speed, 0, 0, true, new(60, 18000, 1.4f, 1));
        var combined = Run(speed, ushort.MaxValue, 0, true, new(60, 18000, 1.4f, 1));
        Assert.That(-rocket.Physics.LinearVelocity.Z, Is.GreaterThan(speed));
        Assert.That(-rocket.Physics.LinearVelocity.Z + coast.Physics.LinearVelocity.Z, Is.EqualTo(18000f / 900 / 60).Within(0.00001));
        Assert.That(-combined.Physics.LinearVelocity.Z, Is.GreaterThan(-rocket.Physics.LinearVelocity.Z));
    }

    [TestCase(-5)]
    [TestCase(0)]
    [TestCase(10)]
    public void ReverseAlwaysOpposesForwardRocket(float speed)
    {
        var reverse = Run(speed, 0, ushort.MaxValue, true, default);
        var opposed = Run(speed, 0, ushort.MaxValue, true, new(60, 18000, 1.4f, 1));
        var rocket = Run(speed, 0, 0, true, new(60, 18000, 1.4f, 1));
        Assert.That(opposed.Physics.LinearVelocity.Z, Is.LessThan(reverse.Physics.LinearVelocity.Z));
        Assert.That(opposed.Physics.LinearVelocity.Z, Is.GreaterThan(rocket.Physics.LinearVelocity.Z));
    }

    [TestCase(0)]
    [TestCase(0.25f)]
    [TestCase(1)]
    public void AirborneForceFollowsChassisAndRuntimeFraction(float fraction)
    {
        var config = new VehicleConfiguration();
        var orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.4f);
        var pose = new VehiclePhysicsState(Vector3.Zero, orientation, Vector3.Zero, Vector3.Zero);
        var input = new InputFrame(1, 0, 0, 0, InputButtons.UseItem, 0, 0);
        var plain = new VehicleMovement(config, pose).Step(input, pose, Vector3.Zero);
        var rocket = new VehicleMovement(config, pose).Step(input, pose, Vector3.Zero, nitro: new(60, 18000, 1.4f, fraction));
        Vector3 expected = Vector3.Transform(-Vector3.UnitZ, orientation) * (18000f / config.Mass / 60 * fraction);
        Assert.That(Vector3.Distance(rocket.Physics.LinearVelocity - plain.Physics.LinearVelocity, expected), Is.LessThan(0.00001));
        Assert.That(VehicleStateCodec.Decode(VehicleStateCodec.Encode(rocket)), Is.EqualTo(rocket));
    }

    [Test]
    public void CapBoundsAddedThrustButNeverClampsExistingMomentumAndReleaseRemovesForce()
    {
        var config = new VehicleConfiguration();
        float cap = config.ForwardSpeed * 1.4f;
        var atCap = Run(cap, 0, 0, false, new(60, 18000, 1.4f, 1));
        var aboveCap = Run(cap + 1, 0, 0, false, new(60, 18000, 1.4f, 1));
        Assert.That(-atCap.Physics.LinearVelocity.Z, Is.EqualTo(cap));
        Assert.That(-aboveCap.Physics.LinearVelocity.Z, Is.EqualTo(cap + 1));
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -10), Vector3.Zero);
        var movement = new VehicleMovement(config, pose);
        movement.Restore(new VehicleState(0, pose, false, false, 0, 0, nitro: new(60, 18000, 1.4f, 1)));
        var release = movement.Step(new InputFrame(1, 0, 0, 0, 0, 0, InputButtons.UseItem), pose, Vector3.Zero);
        Assert.That(release.Physics.LinearVelocity.Z, Is.EqualTo(-10));
        Assert.That(release.Nitro.Active, Is.False);
    }

    [Test]
    public void ThrustRespectsMassAndRejectsInvalidTuning()
    {
        var light = Run(0, 0, 0, false, new(60, 18000, 1.4f, 1));
        var heavy = Run(0, 0, 0, false, new(60, 18000, 1.4f, 1), 1800);
        Assert.That(heavy.Physics.LinearVelocity.Z, Is.EqualTo(light.Physics.LinearVelocity.Z / 2));
        foreach (float invalid in new[] { -1, float.NaN, float.PositiveInfinity, 100001 })
        {
            Assert.Throws<ArgumentException>(() => new ItemConfiguration { NitroForwardThrust = invalid }.Validate());
        }
        Assert.Throws<ArgumentException>(() => new ItemConfiguration { NitroAirborneThrustScale = 1.01f }.Validate());
        Assert.DoesNotThrow(() => new ItemConfiguration { NitroAirborneThrustScale = 0 }.Validate());
    }

    private static VehicleState Run(float speed, ushort throttle, ushort reverse, bool grounded, NitroState nitro, float mass = 900)
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -speed), Vector3.Zero);
        return new VehicleMovement(new VehicleConfiguration { Mass = mass }, pose).Step(
            new InputFrame(1, 0, throttle, reverse, InputButtons.UseItem, 0, 0), pose, grounded ? Vector3.UnitY : Vector3.Zero, nitro: nitro);
    }
}
