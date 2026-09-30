using System.Buffers.Binary;
using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

/// <summary>Progressive power remains portable, tunable and independent of braking/aerial authority.</summary>
[TestFixture]
internal sealed class ThrottleResponseTests
{
    private static readonly VehiclePhysicsState Pose = new(Vector3.Zero, Quaternion.Identity, new(0, 0, -5), Vector3.Zero);

    [Test]
    public void PowerBuildsProgressivelyAndRestoredContinuationMatchesThroughLiftAndReapply()
    {
        var movement = new VehicleMovement(new(), Pose);
        for (ulong tick = 1; tick <= 30; tick++)
        {
            float before = movement.State.Throttle;
            movement.Step(new(tick, 0, 65535, 0, 0, 0, 0), Pose, Vector3.UnitY);
            Assert.That(movement.State.Throttle, Is.GreaterThan(before).And.LessThan(before + 0.04f));
        }
        Assert.That(movement.State.Throttle, Is.InRange(0.53f, 0.55f));
        var restored = new VehicleMovement(new(), Pose);
        restored.Restore(VehicleStateCodec.Decode(VehicleStateCodec.Encode(movement.State)));
        for (ulong tick = 31; tick <= 300; tick++)
        {
            ushort pedal = tick < 60 ? (ushort)0 : tick < 90 ? (ushort)20000 : ushort.MaxValue;
            var input = new Trackstorm.Core.Input.InputFrame(tick, 12000, pedal, 0, 0, 0, 0);
            movement.Step(input, Pose, Vector3.UnitY, surface: SurfaceType.Dirt);
            restored.Step(input, Pose, Vector3.UnitY, surface: SurfaceType.Dirt);
            Assert.That(restored.State, Is.EqualTo(movement.State));
            if (tick == 59) { Assert.That(movement.State.Throttle, Is.LessThan(0.015f)); }
        }
        Assert.That(movement.State.Throttle, Is.GreaterThan(0.99f));
    }

    [Test]
    public void BrakeAndDriveDisableCancelPowerWithoutWaitingForReleaseSmoothing()
    {
        foreach (bool enabled in new[] { true, false })
        {
            var movement = new VehicleMovement(new(), Pose);
            movement.Restore(new(0, Pose, true, false, 0, 0, throttle: 1));
            movement.Step(new(1, 0, 65535, enabled ? ushort.MaxValue : (ushort)0, 0, 0, 0), Pose, Vector3.UnitY, enabled);
            Assert.That(movement.State.Throttle, Is.Zero);
            Assert.That(movement.State.LongitudinalAcceleration, enabled ? Is.LessThan(0) : Is.EqualTo(0));
        }
    }

    [TestCase(0)]
    [TestCase(65535)]
    public void OpposingForwardPedalBrakesReverseImmediately(int reversePedal)
    {
        var reversing = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, 10), Vector3.Zero);
        VehicleState Run(float response)
        {
            var movement = new VehicleMovement(new() { ThrottleRiseTime = response }, reversing);
            return movement.Step(new(1, 0, 65535, (ushort)reversePedal, 0, 0, 0), reversing, Vector3.UnitY, surface: SurfaceType.Asphalt);
        }
        Assert.That(Run(3).Physics, Is.EqualTo(Run(0.01f).Physics));
        Assert.That(Run(3).Physics.LinearVelocity.Z, Is.LessThan(9.8f));
    }

    [Test]
    public void EngineResponseDoesNotChangeDelayedAerialAuthority()
    {
        var quick = new VehicleMovement(new() { ThrottleRiseTime = 0.01f }, Pose);
        var slow = new VehicleMovement(new() { ThrottleRiseTime = 3 }, Pose);
        for (ulong tick = 1; tick <= 180; tick++)
        {
            var input = new Trackstorm.Core.Input.InputFrame(tick, 25000, tick < 100 ? ushort.MaxValue : (ushort)0, 0, 0, 0, 0);
            quick.Step(input, quick.State.Physics, Vector3.Zero);
            slow.Step(input, slow.State.Physics, Vector3.Zero);
            Assert.That(slow.State.Air, Is.EqualTo(quick.State.Air));
            Assert.That(slow.State.Physics, Is.EqualTo(quick.State.Physics));
        }
    }

    [TestCase("throttle_rise_time")]
    [TestCase("throttle_fall_time")]
    public void ResponseUsesExistingConfigurationBoundaryAndRejectsInvalidValues(string key)
    {
        var defaults = GameplayConfiguration.HostedDefaults;
        Assert.That(GameplayOptions.TryApply(defaults, new Dictionary<string, double> { ["vehicle." + key] = 0.8 }, out var changed, out _), Is.True);
        Assert.That(GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(1, new(1, changed))).State.Configuration, Is.EqualTo(changed));
        foreach (double value in new[] { 0, -1, 3.01, double.NaN })
        {
            Assert.That(GameplayOptions.TryApply(defaults, new Dictionary<string, double> { ["vehicle." + key] = value }, out _, out _), Is.False);
        }
    }

    [TestCase(float.NaN)]
    [TestCase(-0.1f)]
    [TestCase(1.1f)]
    public void SnapshotRejectsCorruptPowerContinuation(float throttle)
    {
        byte[] bytes = VehicleStateCodec.Encode(new(0, Pose, true, false, 0, 0));
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(159), throttle);
        Assert.Throws<ArgumentException>(() => VehicleStateCodec.Decode(bytes));
    }
}
