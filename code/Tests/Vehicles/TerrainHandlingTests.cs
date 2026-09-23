using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

/// <summary>Observable terrain ordering, slope drive and portable tuning regressions.</summary>
[TestFixture]
internal sealed class TerrainHandlingTests
{
    private static readonly SurfaceType[] Ladder = [SurfaceType.Asphalt, SurfaceType.Concrete, SurfaceType.Dirt, SurfaceType.Grass, SurfaceType.Mud, SurfaceType.DeepMud];

    [Test]
    public void DefaultSurfacesOrderAccelerationGripAndSustainedProgress()
    {
        float previousSpeed = float.MaxValue;
        float previousLateralCorrection = float.MaxValue;
        foreach (SurfaceType surface in Ladder)
        {
            var initial = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(6, 0, -10), Vector3.Zero);
            var lateral = new VehicleMovement(new(), initial);
            var state = lateral.Step(new InputFrame(1, 0, 0, 0, 0, 0, 0), initial, Vector3.UnitY, surface: surface);
            float correction = 6 - state.Physics.LinearVelocity.X;
            Assert.That(correction, Is.LessThan(previousLateralCorrection), surface.ToString());
            previousLateralCorrection = correction;
            float speed = Drive(surface, 0, 480);
            Assert.That(speed, Is.GreaterThan(1).And.LessThan(previousSpeed), surface.ToString());
            previousSpeed = speed;
        }

        Assert.That(Drive(SurfaceType.DeepMud, 0, 1200), Is.LessThan(Drive(SurfaceType.Mud, 0, 1200) * 0.55f));
    }

    [TestCase(SurfaceType.Asphalt, 20)]
    [TestCase(SurfaceType.Concrete, 20)]
    [TestCase(SurfaceType.Dirt, 20)]
    [TestCase(SurfaceType.Grass, 15)]
    [TestCase(SurfaceType.Mud, 12)]
    [TestCase(SurfaceType.DeepMud, 10)]
    public void ThrottleStartsUphillWithoutBrakingDeadlock(SurfaceType surface, float degrees)
    {
        Assert.That(Drive(surface, degrees, 300), Is.GreaterThan(0.5f));
    }

    [Test]
    public void EverySurfaceOptionUsesExistingValidatedPortableConfiguration()
    {
        foreach (var option in GameplayOptions.All.Where(o => o.Group is "Concrete" or "Dirt" or "Grass" or "Mud" or "Deep Mud"))
        {
            var original = GameplayConfiguration.HostedDefaults;
            Assert.That(GameplayOptions.TryApply(original, new Dictionary<string, double> { [option.Key] = option.Read(original) * 0.8 }, out var changed, out var error), Is.True, error);
            var decoded = GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(1, new(7, changed)));
            Assert.That(decoded.State.Configuration, Is.EqualTo(changed));
            Assert.That(DeveloperSettingsFile.Read(DeveloperSettingsFile.Read(string.Empty).Write(changed) ).Configuration, Is.EqualTo(changed));
            foreach (double invalid in new[] { -1d, double.NaN, 101d })
            {
                Assert.That(GameplayOptions.TryApply(original, new Dictionary<string, double> { [option.Key] = invalid }, out var rejected, out _), Is.False);
                Assert.That(rejected, Is.EqualTo(original));
            }
        }
    }

    [Test]
    public void MaterialMappingReusesIdentityAndKeepsUnassignedMaterialsNeutral()
    {
        Assert.That(SurfaceHandling.Resolve(SurfaceIdentity.Asphalt), Is.EqualTo(SurfaceType.Asphalt));
        Assert.That(SurfaceHandling.Resolve(SurfaceIdentity.DeepMud), Is.EqualTo(SurfaceType.DeepMud));
        Assert.That(SurfaceHandling.Resolve(SurfaceIdentity.Water), Is.EqualTo(SurfaceType.Water));
        Assert.That(SurfaceHandling.Resolve(SurfaceIdentity.Rock), Is.EqualTo(SurfaceType.Asphalt));
        Assert.That(SurfaceHandling.Resolve(null, SurfaceType.Mud), Is.EqualTo(SurfaceType.Mud));
        Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceHandling.Resolve((SurfaceIdentity)255));
    }

    [Test]
    public void SurfaceCatalogSchemaRejectsPreviousWireVersion()
    {
        byte[] bytes = GameplayConfigurationCodec.Encode(1, new(0, GameplayConfiguration.HostedDefaults));
        bytes[2] = 7;
        Assert.Throws<ArgumentException>(() => GameplayConfigurationCodec.Decode(bytes));
    }

    private static float Drive(SurfaceType surface, float degrees, int ticks)
    {
        var orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, degrees * MathF.PI / 180);
        Vector3 normal = Vector3.Transform(Vector3.UnitY, orientation);
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, orientation);
        var physics = new VehiclePhysicsState(Vector3.Zero, orientation, Vector3.Zero, Vector3.Zero);
        var movement = new VehicleMovement(new(), physics);
        for (int tick = 1; tick <= ticks; tick++)
        {
            var state = movement.Step(new InputFrame((ulong)tick, 0, ushort.MaxValue, 0, 0, 0, 0), physics, normal, surface: surface);
            Vector3 velocity = state.Physics.LinearVelocity;
            velocity -= normal * Vector3.Dot(velocity, normal);
            physics = new VehiclePhysicsState(physics.Position + velocity / 60, orientation, velocity, Vector3.Zero);
        }

        return Vector3.Dot(physics.LinearVelocity, forward);
    }
}
