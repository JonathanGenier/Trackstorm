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
    public void RecoveryWaitsThenContinuesExactlyAcrossRestoration()
    {
        var tuning = new VehicleConfiguration();
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI), Vector3.Zero, Vector3.Zero);
        var movement = new VehicleMovement(tuning, pose);
        for (ulong tick = 1; tick <= 120; tick++)
        {
            var input = new InputFrame(tick, 0, 0, 0, 0, 0, 0);
            var restored = new VehicleMovement(tuning, pose);
            restored.Restore(VehicleStateCodec.Decode(VehicleStateCodec.Encode(movement.State)));
            var next = movement.Step(input, pose, Vector3.UnitY, wheels: default(WheelSupport));
            Assert.That(restored.Step(input, pose, Vector3.UnitY, wheels: default(WheelSupport)), Is.EqualTo(next));
            if (tick < 75) { Assert.That(next.Physics.AngularVelocity.Length(), Is.LessThan(0.00001f)); }
        }
        Assert.That(movement.State.Physics.AngularVelocity.Length(), Is.GreaterThan(1));
        var airborne = movement.Step(new(121, 0, 0, 0, 0, 0, 0), pose, Vector3.Zero);
        Assert.That(airborne.CrashSeconds, Is.InRange(1.9f, 2f));
        Assert.That(airborne.Physics.AngularVelocity, Is.EqualTo(Vector3.Zero));
        for (ulong tick = 122; tick <= 190; tick++)
        {
            movement.Step(new(tick, 0, 0, 0, 0, 0, 0), pose, Vector3.Zero);
        }
        Assert.That(movement.State.CrashSeconds, Is.Zero, "sustained flight clears crash memory without rotating the body");
    }

    [Test]
    public void ChassisContactDissipatesReboundWithoutChangingRawImpact()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI), new(4, 15, 0), new(0, 0, 1));
        var contact = new VehicleContact(new(4, -20, 0), Vector3.UnitY, 20000, 0, true, new(0, 1, 0));
        var next = new VehicleMovement(new(), pose).Step(new(1, 0, 0, 0, 0, 0, 0), pose, Vector3.UnitY, wheels: default(WheelSupport), contacts: [contact]);
        Assert.That(next.Physics.LinearVelocity.Y, Is.LessThan(0.5f));
        Assert.That(next.Physics.LinearVelocity.X, Is.InRange(3.8f, 3.95f));
        Assert.That(next.Physics.AngularVelocity.Z, Is.InRange(0.9f, 1));
        Assert.That(contact.RelativeVelocity.Y, Is.EqualTo(-20));
        Assert.That(contact.Impulse, Is.EqualTo(20000));
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
        Assert.That(Spring(0.8f, -8).Physics.LinearVelocity.Y, Is.InRange(-6, -4));
    }

    [Test]
    public void DiagonalChassisScrapingSlowsThenRecoversWithoutErasingImpactEvidence()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.CreateFromYawPitchRoll(0, -2.1f, 0.3f), new(6, 0, -8), Vector3.Zero);
        var contact = new VehicleContact(pose.LinearVelocity, Vector3.UnitY, 15000, 0, true, new(0, 0, 2));
        var movement = new VehicleMovement(new(), pose);
        for (ulong tick = 1; tick <= 240; tick++)
        {
            var next = movement.Step(new(tick, 0, 0, 0, 0, 0, 0), pose, Vector3.UnitY, wheels: default(WheelSupport), contacts: [contact]);
            pose = new(pose.Position, pose.Orientation, new(next.Physics.LinearVelocity.X, 0, next.Physics.LinearVelocity.Z), next.Physics.AngularVelocity);
        }
        Assert.That(pose.LinearVelocity.Length(), Is.LessThan(0.1f));
        Assert.That(Vector3.Dot(pose.AngularVelocity, Vector3.Cross(Vector3.Transform(Vector3.UnitY, pose.Orientation), Vector3.UnitY)), Is.GreaterThan(0.5f));
        Assert.That(contact.RelativeVelocity, Is.EqualTo(new Vector3(6, 0, -8)));
        Assert.That(contact.Impulse, Is.EqualTo(15000));
    }

    [Test]
    public void CrashMemoryNeverOverridesGenuineAirborneInput()
    {
        var pose = new VehiclePhysicsState(new(0, 20, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitX, 2), Vector3.Zero, Vector3.Zero);
        var remembered = new VehicleMovement(new(), pose);
        remembered.Restore(new(0, pose, false, false, 0, 0, crashSeconds: 2));
        var fresh = new VehicleMovement(new(), pose);
        for (ulong tick = 1; tick <= 60; tick++)
        {
            var input = new InputFrame(tick, short.MaxValue, ushort.MaxValue, 0, InputButtons.AirRoll, 0, 0);
            var a = remembered.Step(input, pose, Vector3.Zero);
            var b = fresh.Step(input, pose, Vector3.Zero);
            Assert.That(a.Physics, Is.EqualTo(b.Physics));
            Assert.That(a.Air, Is.EqualTo(b.Air));
            pose = a.Physics;
        }
    }
}
