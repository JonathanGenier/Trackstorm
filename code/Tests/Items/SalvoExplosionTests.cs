using System.Numerics;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class SalvoExplosionTests
{
    private HostVehicleSession _host = null!;
    private readonly Dictionary<ulong, VehiclePhysicsState> _poses = [];

    [SetUp]
    public void SetUp()
    {
        _host = new(213, damageConfiguration: new() { MaxHP = 1000 }, matchConfiguration: new() { MinimumPlayers = 1, CountdownTicks = 1 });
        _host.Join(2);
        _poses.Clear();
        _poses[1] = new(new(-40, 0, 0), Quaternion.Identity, default, default);
        _poses[2] = new(Vector3.Zero, Quaternion.Identity, default, default);
        Step(); Step();
    }

    private void Step(Func<MissileState, Vector3, float?>? collision = null)
        => _host.Step(default, v => new(_poses[v.VehicleId], Vector3.UnitY), collide: collision);

    private void Seed(params Vector3[] centers)
    {
        var missiles = centers.Select((center, i) =>
        {
            var arc = new SalvoFlight(center + Vector3.UnitY * 2, center, 1, 2, 1, 1);
            return new MissileState((ulong)i + 200, 1, arc.At(1), arc.VelocityAt(1), SalvoFlight.LifetimeTicks - 1) { Arc = arc };
        }).ToArray();
        var items = _host.Items;
        items.Restore(new(items.Revision + 1, _host.Snapshot(), items.Slots, missiles, [], shields: items.Shields),
            items.Revision + 1, Math.Max(items.TokenHighWater, missiles.Max(m => m.Id)));
    }

    [Test]
    public void ThreeFootDefaultIsTunableThroughExistingConfigurationAndCodec()
    {
        Assert.That(_host.Items.Configuration.SalvoBlastRadius, Is.EqualTo(.9144f));
        foreach (double radius in new[] { .1, .9144, 2, 30 })
        {
            Assert.That(_host.TryConfigure(0, new Dictionary<string, double> { ["items.salvo_blast_radius"] = radius }, out _), Is.True);
            Assert.That(_host.Items.Configuration.SalvoBlastRadius, Is.EqualTo(radius).Within(.00001));
            var checkpoint = new ResumeCheckpoint(new(Math.Max(1, _host.Items.Revision), _host.Snapshot(), _host.Items.Slots, [], []), _host.World.State.Match!, null, _host.Configuration);
            Assert.That(ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(checkpoint)).Configuration, Is.EqualTo(checkpoint.Configuration));
        }
        Assert.That(_host.TryConfigure(0, new Dictionary<string, double> { ["items.salvo_blast_radius"] = .09 }, out _), Is.False);
    }

    [TestCase(0f)]
    [TestCase(1.3f)]
    public void FrontSurfaceIsDamagedOnceEvenWithRearShieldAndRotatedVehicle(float yaw)
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw), default, default);
        _poses[2] = pose;
        _host.Items.Grant(_host.World, 2, HeldItem.Shield);
        Vector3 point = VehicleHull.ClosestPoint(pose, Vector3.Transform(new Vector3(0, .2f, -10), pose.Orientation));
        Seed(point); Step((_, _) => 1);
        Assert.That(_host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(750).Within(.001));
        Assert.That(_host.Items.Shields.Single().HP, Is.EqualTo(1000));
        Assert.That(_host.Items.Events.Count(e => e.Impact), Is.EqualTo(1));
        for (int i = 0; i < 30; i++) { Step(); }
        Assert.That(_host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(750).Within(.001));
    }

    [Test]
    public void RearImpactSpendsShieldOnceAndLethalHitStillProtectsOverlappingVehicle()
    {
        _host.Items.Grant(_host.World, 2, HeldItem.Shield);
        var shield = _host.Items.Shields.Single();
        _host.Items.DamageShield(_host.World, shield.Id, 1, 900, new("test", 1, "setup"));
        // Rear panel lies only .77 m from the chassis: both intersect the default blast.
        Seed(new Vector3(0, .25f, 3.25f)); Step((_, _) => 1);
        Assert.That(_host.Items.Shields, Is.Empty);
        Assert.That(_host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(1000));
        Seed(new Vector3(0, .25f, 3.25f)); Step((_, _) => 1);
        Assert.That(_host.World.GetVehicle(2).Damage.CurrentHP, Is.LessThan(1000));
    }

    [Test]
    public void TwoOverlappingBlastsDamageTwoExposedCarsOnceEachWithoutShieldCrossProtection()
    {
        _host.Join(3); _poses[3] = new(Vector3.UnitY, Quaternion.Identity, default, default); Step();
        _host.Items.Grant(_host.World, 2, HeldItem.Shield);
        Vector3 rear = new(0, .25f, 3.25f);
        Seed(rear, rear); Step((_, _) => 1);
        Assert.That(_host.Items.Shields.Single().HP, Is.EqualTo(500).Within(.001));
        Assert.That(_host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(1000));
        float expected = 1000 - _host.Items.Events.Where(e => e.Impact).Sum(e =>
            _host.Items.Explosion(e.Position, VehicleHull.ClosestPoint(_poses[3], e.Position), HeldItem.Salvo).Damage);
        Assert.That(expected, Is.LessThan(1000));
        Assert.That(_host.World.GetVehicle(3).Damage.CurrentHP, Is.EqualTo(expected).Within(.001));
        Assert.That(_host.Items.Events.Count(e => e.Impact), Is.EqualTo(2));
    }

    [Test]
    public void WallEdgeWithinBlastTakesOneHitRegardlessOfOverlappingCarCount()
    {
        _host.Items.Grant(_host.World, 2, HeldItem.Shield);
        var shield = _host.Items.Shields.Single();
        _host.Items.TransitionShield(_host.World, shield.Id, ShieldStage.RearShield, ShieldStage.WorldWall,
            new VehiclePhysicsState(new(0, 0, 8), Quaternion.Identity, default, default));
        // Strike the actual angled right-wing tip, rather than the former flat footprint.
        Seed(new Vector3(2.75f, 0, 7.1f)); Step((_, _) => 1);
        Assert.That(_host.Items.Shields.Single().HP, Is.EqualTo(750).Within(.001));
        Assert.That(_host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(1000));
    }

    [Test]
    public void FailedBatchThenRetryCannotPartlyApplyOrDoubleExplosionDamage()
    {
        Vector3 front = VehicleHull.ClosestPoint(_poses[2], new(0, .2f, -10));
        Seed(front, front);
        Assert.Throws<ArgumentException>(() => Step((m, _) => m.Id == 200 ? 1 : float.NaN));
        Assert.That(_host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(1000));
        Assert.That(_host.Items.Missiles, Has.Count.EqualTo(2));
        Step((_, _) => 1);
        Assert.That(_host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(500).Within(.001));
        Assert.That(_host.Items.Events.Count(e => e.Impact), Is.EqualTo(2));
    }

    [Test]
    public void TallRockTopUsesAuthoredBoundsInsteadOfDistantGroundOrigin()
    {
        var box = new EnvironmentRockBounds(new(0, 2, 0), new(2, 4, 2), Quaternion.Identity);
        var layout = new EnvironmentLayout([Vector3.Zero], [], [2.5f], bounds: [box]);
        var environment = new EnvironmentAuthority(layout);
        Seed(new Vector3(0, 4, 0)); Step((_, _) => 1);
        environment.Advance(_host.World.State.Tick, [], _host.Items.Events, _host.Items);
        Assert.That(environment.Snapshot(213, _host.World.State.Tick).Rocks[0].Stage, Is.EqualTo(2));
    }

    [Test]
    public void LargeBrokenRockEdgeUsesDebrisBoundsRatherThanVolumeSphere()
    {
        var debris = new EnvironmentRockBounds(new(0, .25f, 0), new(2, .5f, 1), Quaternion.Identity);
        var layout = new EnvironmentLayout([Vector3.Zero], [], [8f], debrisBounds: debris);
        var environment = new EnvironmentAuthority(layout);
        environment.Restore(new(213, 2, [new(2, 0, 0, default, default, 600), default, default, default], []));
        float size = layout.Size(0, 2);
        Vector3 edge = new(size, size * .25f, 0);
        environment.Advance(3, [], [new(1, 1, HeldItem.Salvo, edge, true)], _host.Items);
        Assert.That(environment.Snapshot(213, 3).Rocks[0].Stage, Is.EqualTo(3));
    }

    [Test]
    public void RockSurfaceFracturesOnceAndCheckpointPreservesDestructionWithoutReplayingBlast()
    {
        var layout = new EnvironmentLayout([Vector3.Zero], [], [4f]);
        var environment = new EnvironmentAuthority(layout);
        Vector3 point = new(2, 0, 0);
        Seed(point); Step((_, _) => 1);
        ulong tick = _host.World.State.Tick;
        environment.Advance(tick, [], _host.Items.Events, _host.Items);
        var snapshot = environment.Snapshot(213, tick);
        Assert.That(snapshot.Rocks[0].Stage, Is.EqualTo(2));
        Assert.That(snapshot.Rocks.Count(r => r.Stage > 0), Is.EqualTo(2));
        environment.Advance(tick, [], _host.Items.Events, _host.Items);
        Assert.That(environment.Snapshot(213, tick).Rocks, Is.EqualTo(snapshot.Rocks));
        var restored = new EnvironmentAuthority(layout); restored.Restore(snapshot);
        restored.Advance(tick + 1, [], [], _host.Items);
        Assert.That(restored.Snapshot(213, tick + 1).Rocks.Select(r => r.Stage), Is.EqualTo(snapshot.Rocks.Select(r => r.Stage)));
    }
}
