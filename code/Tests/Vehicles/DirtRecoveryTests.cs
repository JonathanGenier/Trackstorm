using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

/// <summary>Dirt-only arcade authority, boundaries and deterministic continuation.</summary>
[TestFixture]
internal sealed class DirtRecoveryTests
{
    private static readonly VehicleConfiguration Unassisted = new() { DirtSteeringReserve = 0, DirtRecovery = 0 };

    [TestCase(8f)]
    [TestCase(18f)]
    [TestCase(30f)]
    public void CountersteeringArrestsSlideYawWithoutInstantReversal(float speed)
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(-speed * 0.7f, 0, -speed), new(0, -1.5f, 0));
        var assisted = Run(new(), pose, SurfaceType.Dirt, -short.MaxValue, 45);
        var baseline = Run(Unassisted, pose, SurfaceType.Dirt, -short.MaxValue, 45);
        TestContext.WriteLine($"speed={speed}: yaw assisted={assisted.Physics.AngularVelocity.Y}, old={baseline.Physics.AngularVelocity.Y}");
        Assert.That(assisted.Physics.AngularVelocity.Y, Is.GreaterThan(baseline.Physics.AngularVelocity.Y + 0.1f));
        Assert.That(assisted.Physics.LinearVelocity.Length(), Is.GreaterThan(speed * 0.6f));
    }

    [TestCase(SurfaceType.Asphalt)]
    [TestCase(SurfaceType.Concrete)]
    [TestCase(SurfaceType.Grass)]
    [TestCase(SurfaceType.Mud)]
    [TestCase(SurfaceType.DeepMud)]
    [TestCase(SurfaceType.Water)]
    public void OtherSurfacesRetainExactTrajectory(SurfaceType surface)
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(-9, 0, -20), new(0, -1.2f, 0));
        Assert.That(Run(new(), pose, surface, -20000, 180), Is.EqualTo(Run(Unassisted, pose, surface, -20000, 180)));
    }

    [Test]
    public void AirborneAndZeroGripDoNotGainRecoveryAuthority()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(-9, 0, -20), new(0, -1.2f, 0));
        foreach (var normal in new[] { Vector3.Zero, Vector3.UnitY })
        {
            var enabled = new VehicleConfiguration { Dirt = new(0, 1, 1) };
            var disabled = enabled with { DirtSteeringReserve = 0, DirtRecovery = 0 };
            var input = new InputFrame(1, -20000, 40000, 0, 0, 0, 0);
            Assert.That(new VehicleMovement(enabled, pose).Step(input, pose, normal, surface: SurfaceType.Dirt),
                Is.EqualTo(new VehicleMovement(disabled, pose).Step(input, pose, normal, surface: SurfaceType.Dirt)));
        }
    }

    [Test]
    public void StraightDirtAccelerationAndPowerSlipAreUnchanged()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        Assert.That(Run(new(), pose, SurfaceType.Dirt, 0, 600), Is.EqualTo(Run(Unassisted, pose, SurfaceType.Dirt, 0, 600)));
    }

    [Test]
    public void UnsupportedFrontOrRearOnlyDirtDoesNotEnableTheAssist()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(-9, 0, -20), new(0, -1.2f, 0));
        foreach (var wheels in new[]
        {
            new WheelSupport(new(0, 0, 0.327f, 0.327f), SurfaceType.Dirt, SurfaceType.Dirt, SurfaceType.Dirt, SurfaceType.Dirt),
            new WheelSupport(new Vector4(0.327f), SurfaceType.Asphalt, SurfaceType.Asphalt, SurfaceType.Dirt, SurfaceType.Dirt),
        })
        {
            var input = new InputFrame(1, -20000, 40000, 0, 0, 0, 0);
            Assert.That(new VehicleMovement(new(), pose).Step(input, pose, Vector3.UnitY, surface: SurfaceType.Dirt, wheels: wheels),
                Is.EqualTo(new VehicleMovement(Unassisted, pose).Step(input, pose, Vector3.UnitY, surface: SurfaceType.Dirt, wheels: wheels)));
        }
    }

    [Test]
    public void MirroredSlidesRecoverSymmetricallyAndRemainBounded()
    {
        VehicleState Slide(float sign) => Run(new(), new(Vector3.Zero, Quaternion.Identity,
            new(14 * sign, 0, -22), new(0, 1.4f * sign, 0)), SurfaceType.Dirt, (short)(short.MaxValue * sign), 120);
        var left = Slide(-1);
        var right = Slide(1);
        Assert.That(left.Physics.LinearVelocity.X, Is.EqualTo(-right.Physics.LinearVelocity.X).Within(0.0001f));
        Assert.That(left.Physics.LinearVelocity.Z, Is.EqualTo(right.Physics.LinearVelocity.Z).Within(0.0001f));
        Assert.That(left.Physics.AngularVelocity.Y, Is.EqualTo(-right.Physics.AngularVelocity.Y).Within(0.0001f));
    }

    [TestCase("vehicle.dirt_steering_reserve", 1.1)]
    [TestCase("vehicle.dirt_recovery", 10.1)]
    public void RecoveryControlsValidateAndOldFilesAcquireDefaults(string key, double invalid)
    {
        var original = GameplayConfiguration.HostedDefaults;
        foreach (double value in new[] { invalid, -1, double.NaN })
        {
            Assert.That(GameplayOptions.TryApply(original, new Dictionary<string, double> { [key] = value }, out var rejected, out _), Is.False);
            Assert.That(rejected, Is.EqualTo(original));
        }
        Assert.That(DeveloperSettingsFile.Read(string.Empty).Configuration.Vehicle, Is.EqualTo(original.Vehicle));
        var bytes = GameplayConfigurationCodec.Encode(1, new(0, original));
        bytes[2] = 16;
        Assert.Throws<ArgumentException>(() => GameplayConfigurationCodec.Decode(bytes));
    }

    private static VehicleState Run(VehicleConfiguration config, VehiclePhysicsState pose, SurfaceType surface, short steering, int ticks)
    {
        var movement = new VehicleMovement(config, pose);
        for (int tick = 1; tick <= ticks; tick++)
        {
            var state = movement.Step(new((ulong)tick, steering, 40000, 0, 0, 0, 0), pose, Vector3.UnitY, surface: surface);
            Assert.That(Math.Abs(state.Physics.AngularVelocity.Y - pose.AngularVelocity.Y), Is.LessThan(0.5f), "No single-tick yaw snap");
            Vector3 velocity = state.Physics.LinearVelocity with { Y = 0 };
            float yaw = state.Physics.AngularVelocity.Y;
            pose = new(pose.Position + velocity / 60, Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw / 60) * pose.Orientation), velocity, new(0, yaw, 0));
            if (tick == ticks / 2)
            {
                var restored = new VehicleMovement(config, pose);
                restored.Restore(VehicleStateCodec.Decode(VehicleStateCodec.Encode(state)));
                var input = new InputFrame((ulong)tick + 1, steering, 40000, 0, 0, 0, 0);
                Assert.That(restored.Step(input, pose, Vector3.UnitY, surface: surface),
                    Is.EqualTo(movement.Step(input, pose, Vector3.UnitY, surface: surface)));
                movement.Restore(state);
            }
        }
        return movement.State;
    }

}
