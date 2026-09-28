using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

[TestFixture]
internal sealed class TrophyTruckTests
{
    [Test]
    public void UnsupportedAxleCannotApplyDriveOrSteeringForce()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -10), Vector3.Zero);
        var tuning = new VehicleConfiguration { FrontDriveShare = 1 };
        var state = new VehicleMovement(tuning, pose).Step(new(1, short.MaxValue, ushort.MaxValue, 0, 0, 0, 0),
            pose, Vector3.UnitY, surface: SurfaceType.Asphalt, wheels: new(new(0, 0, 0.45f, 0.45f)));
        Assert.That(state.LongitudinalAcceleration, Is.Zero);
        Assert.That(state.Physics.AngularVelocity.Y, Is.Zero);
    }

    [Test]
    public void ZeroCrashAngularLimitIsAValidRuntimeSetting()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -20), Vector3.Zero);
        var contact = new VehicleContact(pose.LinearVelocity, Vector3.UnitZ, 0, 0, localPosition: new(1, 0, -2), staticObstacle: true);
        var state = EnvironmentCollision.Resolve(pose, Vector3.UnitY, [contact], new() { CrashAngularLimit = 0 });
        Assert.That(state.LinearVelocity, Is.EqualTo(Vector3.Zero));
        Assert.That(state.AngularVelocity, Is.EqualTo(Vector3.Zero));
    }
    [TestCase(0f, 20f, 1400f, 1400f)]
    [TestCase(-20f, 20f, 1400f, 1400f)]
    [TestCase(0f, 20f, 700f, 1400f)]
    public void CenteredImpactsConserveMomentumAndRemoveClosingSpeed(float targetSpeed, float speed, float targetMass, float mass)
    {
        var a = new VehiclePhysicsState(new(-2, 0, 0), Quaternion.Identity, new(speed, 0, 0), Vector3.Zero);
        var b = new VehiclePhysicsState(new(2, 0, 0), Quaternion.Identity, new(targetSpeed, 0, 0), Vector3.Zero);
        var result = VehicleCollision.ResolvePair(a, new() { Mass = mass }, b, new() { Mass = targetMass }, -Vector3.UnitX, Vector3.Zero);
        float expected = (mass * speed + targetMass * targetSpeed) / (mass + targetMass);
        Assert.That(result.First.LinearVelocity.X, Is.EqualTo(expected).Within(0.00001));
        Assert.That(result.Second.LinearVelocity.X, Is.EqualTo(expected).Within(0.00001));
        Assert.That(result.First.AngularVelocity, Is.EqualTo(Vector3.Zero));
        var repeated = VehicleCollision.ResolvePair(result.First, new() { Mass = mass }, result.Second, new() { Mass = targetMass }, -Vector3.UnitX, Vector3.Zero);
        Assert.That(repeated, Is.EqualTo(result));
    }

    [Test]
    public void GlancingImpactPreservesTangentialMotionAndBoundsRotation()
    {
        var a = new VehiclePhysicsState(new(-2, 0, 0), Quaternion.Identity, new(30, 0, 8), Vector3.Zero);
        var b = new VehiclePhysicsState(new(2, 0, 0), Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        var tuning = new VehicleConfiguration();
        var result = VehicleCollision.ResolvePair(a, tuning, b, tuning, -Vector3.UnitX, new(0, 0, 2));
        Assert.That(result.First.LinearVelocity.Z, Is.EqualTo(8));
        Assert.That(result.First.LinearVelocity + result.Second.LinearVelocity, Is.EqualTo(a.LinearVelocity));
        Assert.That(result.First.AngularVelocity.Length(), Is.InRange(0.1f, tuning.CrashAngularLimit + 0.00001f));
        Assert.That(result.Second.AngularVelocity.Y, Is.EqualTo(-result.First.AngularVelocity.Y));
    }

    [Test]
    public void FrontDriveImprovesClimbingAndUsesTheEstablishedConfigurationBoundary()
    {
        var tuning = new VehicleConfiguration();
        var orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 30 * MathF.PI / 180);
        var normal = Vector3.Transform(Vector3.UnitY, orientation);
        var pose = new VehiclePhysicsState(Vector3.Zero, orientation, Vector3.Zero, Vector3.Zero);
        var input = new InputFrame(1, 0, ushort.MaxValue, 0, 0, 0, 0);
        var wheels = new WheelSupport(new Vector4(tuning.Gravity * normal.Y / tuning.WheelSpring));
        VehicleState Drive(VehicleConfiguration c) => new VehicleMovement(c, pose).Step(input, pose, normal, surface: SurfaceType.Dirt, wheels: wheels);
        var forward = Vector3.Transform(-Vector3.UnitZ, orientation);
        Assert.That(Vector3.Dot(Drive(tuning).Physics.LinearVelocity, forward), Is.GreaterThan(0.1f));
        Assert.That(Drive(tuning).LongitudinalAcceleration, Is.GreaterThan(Drive(tuning with { FrontDriveShare = 0 }).LongitudinalAcceleration));
        var configuration = GameplayConfiguration.HostedDefaults;
        Assert.That(GameplayOptions.TryApply(configuration, new Dictionary<string, double> { ["vehicle.front_drive_share"] = 0.2 }, out var changed, out _), Is.True);
        Assert.That(GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(1, new(1, changed))).State.Configuration, Is.EqualTo(changed));
        Assert.That(GameplayOptions.TryApply(configuration, new Dictionary<string, double> { ["vehicle.front_drive_share"] = 1.1 }, out _, out _), Is.False);
    }

    [Test]
    public void BumperOrRoofContactDoesNotReceiveAnUprightSpring()
    {
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 2.5f);
        var pose = new VehiclePhysicsState(Vector3.Zero, rotation, Vector3.Zero, new(1, 0, 0.5f));
        var state = new VehicleMovement(new(), pose).Step(new(1, 0, 0, 0, 0, 0, 0), pose, Vector3.UnitY, wheels: default(WheelSupport));
        Assert.That(state.Physics.AngularVelocity, Is.EqualTo(pose.AngularVelocity));
    }

    [Test]
    public void LoadedSuspensionReleasesEnergyButResistsDeepCompressionProgressively()
    {
        var tuning = new VehicleConfiguration();
        VehicleState Spring(float depth, float vertical)
        {
            var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, vertical, 0), Vector3.Zero);
            return new VehicleMovement(tuning, pose).Step(new(1, 0, 0, 0, 0, 0, 0), pose, Vector3.UnitY, wheels: new(new Vector4(depth)));
        }
        float a = Spring(0.4f, 0).Physics.LinearVelocity.Y;
        float b = Spring(0.6f, 0).Physics.LinearVelocity.Y;
        float c = Spring(0.8f, 0).Physics.LinearVelocity.Y;
        Assert.That(c - b, Is.GreaterThan((b - a) * 2));
        Assert.That(Spring(0.8f, 0.5f).Physics.LinearVelocity.Y, Is.GreaterThan(0.5f));
        Assert.That(Spring(0.8f, -8).Physics.LinearVelocity.Y, Is.InRange(-8, -7));
    }
}
