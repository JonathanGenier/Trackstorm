using System.Numerics;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Development;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Development;

[TestFixture]
internal sealed class IntegratedDrivingTuningTests
{
    [TestCase("vehicle.wall_drag", 2)]
    [TestCase("vehicle.crash_dissipation", 0)]
    [TestCase("vehicle.crash_rotation", 0)]
    [TestCase("vehicle.crash_angular_limit", 0.1)]
    public void CollisionEditorsChangeDissipativeResponse(string key, double value)
    {
        var defaults = GameplayConfiguration.HostedDefaults;
        Assert.That(GameplayOptions.TryApply(defaults, new Dictionary<string, double> { [key] = value }, out var tuned, out _), Is.True);
        var incoming = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(-15, 0, -30), Vector3.Zero);
        var contact = new VehicleContact(incoming.LinearVelocity, Vector3.UnitZ, 0, 0, localPosition: new(2, 0, -2), staticObstacle: true);
        var baseline = EnvironmentCollision.Resolve(incoming, Vector3.UnitY, [contact], defaults.Vehicle);
        var actual = EnvironmentCollision.Resolve(incoming, Vector3.UnitY, [contact], tuned.Vehicle);
        Assert.That(actual, Is.Not.EqualTo(baseline));
        Assert.That(actual.LinearVelocity.Length(), Is.LessThanOrEqualTo(incoming.LinearVelocity.Length()));
        Assert.That(actual.LinearVelocity.Y, Is.Zero);
        Assert.That(actual.AngularVelocity.Length(), Is.LessThanOrEqualTo(tuned.Vehicle.CrashAngularLimit + 0.00001f));
    }

    [TestCase("environment.health_scale", 2)]
    [TestCase("environment.impact_threshold", 8)]
    [TestCase("environment.impact_scale", 2)]
    public void DestructionEditorsAffectAcceptedDamageAndPreservePartialDamage(string key, double value)
    {
        var defaults = GameplayConfiguration.HostedDefaults;
        Assert.That(GameplayOptions.TryApply(defaults, new Dictionary<string, double> { [key] = value }, out var tuned, out _), Is.True);
        var layout = new EnvironmentLayout([Vector3.Zero], []);
        var authority = new EnvironmentAuthority(layout);
        var baseline = new EnvironmentAuthority(layout);
        var request = Impact(9);
        baseline.Advance(1, [request], [], new());
        authority.Advance(1, [request], [], new(), tuned.Destruction);
        var snapshot = authority.Snapshot(1, 1);
        Assert.That(snapshot.Rocks[0].Damage, Is.LessThan(baseline.Snapshot(1, 1).Rocks[0].Damage));
        var restored = new EnvironmentAuthority(layout);
        restored.Restore(EnvironmentCodec.Decode(EnvironmentCodec.Encode(snapshot)));
        authority.Advance(13, [request], [], new(), defaults.Destruction);
        restored.Advance(13, [request], [], new(), defaults.Destruction);
        Assert.That(EnvironmentCodec.Encode(restored.Snapshot(1, 13)), Is.EqualTo(EnvironmentCodec.Encode(authority.Snapshot(1, 13))));
    }

    [TestCase("environment.piece_speed", 1)]
    [TestCase("environment.push_scale", 0.01)]
    [TestCase("environment.velocity_retention", 0.1)]
    public void PieceEditorsChangeMotionWithinRecoveryBounds(string key, double value)
    {
        Assert.That(GameplayOptions.TryApply(GameplayConfiguration.HostedDefaults, new Dictionary<string, double> { [key] = value }, out var tuned, out _), Is.True);
        var layout = new EnvironmentLayout([Vector3.Zero], []);
        var baseline = new EnvironmentAuthority(layout);
        var authority = new EnvironmentAuthority(layout);
        baseline.Advance(1, [Impact(14)], [], new());
        authority.Advance(1, [Impact(14)], [], new(), tuned.Destruction);
        baseline.Advance(2, [], [], new());
        authority.Advance(2, [], [], new(), tuned.Destruction);
        var snapshot = authority.Snapshot(1, 2);
        Assert.That(snapshot.Rocks[0].Velocity.Length(), Is.LessThan(baseline.Snapshot(1, 2).Rocks[0].Velocity.Length()));
        Assert.DoesNotThrow(() => EnvironmentCodec.Decode(EnvironmentCodec.Encode(snapshot)));
    }

    [Test]
    public void HostAppliesDestructionTuningAndCheckpointRetainsIt()
    {
        var old = PrototypeArena.Configuration;
        var point = old.Players[0].Position;
        var layout = new EnvironmentLayout([point], []);
        var arena = new ArenaConfiguration(old.Minimum, old.Maximum, old.Players, old.Items, old.Surfaces, layout);
        var host = new Core.Networking.Replication.HostVehicleSession(7, arena: arena);
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["environment.health_scale"] = 10 }, out _), Is.True);
        host.Step(default, s => new(s.Movement.Physics, Vector3.UnitY, [new(new(0, 0, -20), Vector3.UnitZ, 0, 0, environmentRock: 1)]));
        Assert.That(host.Environment!.Snapshot(7, 1).Rocks[0].Stage, Is.EqualTo(1));
        Assert.That(host.Environment.Snapshot(7, 1).Rocks[0].Damage, Is.EqualTo(36));
        var checkpoint = host.PrepareJoin(2, 1)!;
        var decoded = Core.Networking.Replication.ResumeCheckpointCodec.Decode(Core.Networking.Replication.ResumeCheckpointCodec.Encode(checkpoint));
        Assert.That(decoded.Configuration.Configuration.Destruction.HealthScale, Is.EqualTo(10));
        Assert.That(decoded.Environment!.Rocks[0].Damage, Is.EqualTo(36));
    }

    [TestCase("vehicle.wall_drag", -1)]
    [TestCase("vehicle.crash_dissipation", 1.01)]
    [TestCase("vehicle.crash_rotation", double.NaN)]
    [TestCase("vehicle.crash_angular_limit", 3.01)]
    [TestCase("environment.health_scale", 0)]
    [TestCase("environment.impact_threshold", -1)]
    [TestCase("environment.impact_scale", 101)]
    [TestCase("environment.piece_speed", 6.01)]
    [TestCase("environment.push_scale", 1.01)]
    [TestCase("environment.velocity_retention", 1)]
    public void UnsafeEditsRejectTheWholeTransaction(string key, double value)
    {
        var defaults = GameplayConfiguration.HostedDefaults;
        Assert.That(GameplayOptions.TryApply(defaults, new Dictionary<string, double> { [key] = value, ["vehicle.mass"] = 1200 }, out var result, out _), Is.False);
        Assert.That(result, Is.EqualTo(defaults));
    }

    private static VehicleStepRequest Impact(float speed) => new(1, default,
        new VehicleObservation(new(new(0, 1, 0), Quaternion.Identity, new(0, 0, -speed), Vector3.Zero), Vector3.UnitY,
            [new(new(0, 0, -speed), Vector3.UnitZ, 0, 0, environmentRock: 1)]));
}
