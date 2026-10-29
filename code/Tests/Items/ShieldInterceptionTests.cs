using System.Numerics;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class ShieldInterceptionTests
{
    private HostVehicleSession _host = null!;
    private Quaternion _rotation;
    private VehiclePhysicsState Pose(ulong id) => new(id == 1 ? new(-40, 0, 0) : Vector3.Zero, _rotation, default, default);
    private Vector3 Point(Vector3 local) => Vector3.Transform(local, _rotation);
    private void Step() => _host.Step(default, v => new(Pose(v.VehicleId), Vector3.UnitY), collide: (_, _) => 1);

    [SetUp]
    public void Setup()
    {
        _rotation = Quaternion.Identity;
        _host = new(317, new() { SalvoBlastRadius = 5, MaximumDamage = 250, ExplosionRadius = 10 },
            damageConfiguration: new() { MaxHP = 1000 }, matchConfiguration: new() { MinimumPlayers = 1, CountdownTicks = 1 });
        _host.Join(2); Step(); Step();
        Assert.That(_host.Items.Grant(_host.World, 2, HeldItem.Shield), Is.True);
    }

    private void Blast(HeldItem item, Vector3 local, int count = 1)
    {
        var items = _host.Items; ulong token = items.TokenHighWater;
        Vector3 center = Point(local);
        var missiles = Enumerable.Range(0, count).Select(_ =>
        {
            var arc = new SalvoFlight(center + Point(Vector3.UnitY * 2), center, 1, 2, 1, 1);
            return item == HeldItem.Salvo
                ? new MissileState(++token, 1, arc.At(1), arc.VelocityAt(1), SalvoFlight.LifetimeTicks - 1) { Arc = arc }
                : new MissileState(++token, 1, center + Point(Vector3.UnitY), -Point(Vector3.UnitY) * 60, 300);
        }).ToArray();
        items.Restore(new(items.Revision + 1, _host.Snapshot(), items.Slots, missiles, [], shields: items.Shields), items.Revision + 1, token);
        Step();
    }

    [TestCase(HeldItem.Salvo, 0)]
    [TestCase(HeldItem.Salvo, 1)]
    [TestCase(HeldItem.Missile, 0)]
    [TestCase(HeldItem.Missile, 1)]
    public void RearBlastProtectsRotatedHullAndDamagesArmorOnce(HeldItem item, int tilted)
    {
        if (tilted != 0) { _rotation = Quaternion.CreateFromYawPitchRoll(1.2f, .3f, -.2f); }
        Blast(item, new(0, .25f, 4));
        Assert.That(_host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(1000));
        Assert.That(_host.Items.Shields.Single().HP, Is.LessThan(1000));
        Assert.That(_host.Items.Shields.Single().DamageSequence, Is.EqualTo(1));
    }

    [TestCase(HeldItem.Salvo, -4f, .25f)]
    [TestCase(HeldItem.Missile, -4f, .25f)]
    [TestCase(HeldItem.Salvo, 4f, 3f)]
    [TestCase(HeldItem.Missile, 4f, 3f)]
    public void FrontOrUpperExposureUsesExistingFalloffWithoutFixedSplit(HeldItem item, float z, float y)
    {
        Vector3 center = new(0, y, z);
        var expected = _host.Items.Explosion(center, Vector3.Zero, item, VehicleHull.ClosestPoint(Pose(2), center)).Damage;
        Blast(item, center);
        Assert.That(_host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(1000 - expected).Within(.001));
        if (z > 0) { Assert.That(_host.Items.Shields.Single().HP, Is.LessThan(1000)); }
    }

    [TestCase(HeldItem.Salvo)]
    [TestCase(HeldItem.Missile)]
    public void LethalWallBlastProtectsAllCarsBeforeRemovalAndNextBlastReachesThem(HeldItem item)
    {
        _host.Join(3); Step();
        var shield = _host.Items.Shields.Single();
        _host.Items.TransitionShield(_host.World, shield.Id, ShieldStage.RearShield, ShieldStage.WorldWall,
            new(new(0, .25f, 3.3f), Quaternion.Identity, default, default));
        _host.Items.DamageShield(_host.World, shield.Id, 1, 999, new("test", 1, "setup"));
        Blast(item, new(0, .25f, 4));
        Assert.That(_host.Items.Shields, Is.Empty);
        foreach (ulong id in new ulong[] { 2, 3 }) { Assert.That(_host.World.GetVehicle(id).Damage.CurrentHP, Is.EqualTo(1000)); }
        Blast(item, new(0, .25f, 4));
        foreach (ulong id in new ulong[] { 2, 3 }) { Assert.That(_host.World.GetVehicle(id).Damage.CurrentHP, Is.LessThan(1000)); }
    }

    [TestCase(0f, true)]
    [TestCase(10f, false)]
    public void DeployedProtectionFollowsWorldPositionInsteadOfFormerOwnership(float x, bool covered)
    {
        var shield = _host.Items.Shields.Single();
        _host.Items.TransitionShield(_host.World, shield.Id, ShieldStage.RearShield, ShieldStage.WorldWall,
            new(new(x, .25f, 3.3f), Quaternion.Identity, default, default));
        Blast(HeldItem.Salvo, new(0, .25f, 4));
        Assert.That(_host.World.GetVehicle(2).Damage.CurrentHP == 1000, Is.EqualTo(covered));
    }

    [Test]
    public void BlastOnWallFaceCanPropagateAwayFromThatFace()
    {
        var shield = _host.Items.Shields.Single();
        _host.Items.TransitionShield(_host.World, shield.Id, ShieldStage.RearShield, ShieldStage.WorldWall,
            new(new(0, .25f, 3.3f), Quaternion.Identity, default, default));
        Blast(HeldItem.Missile, new(0, .25f, 3.16f));
        Assert.That(_host.World.GetVehicle(2).Damage.CurrentHP, Is.LessThan(1000));
        Assert.That(_host.Items.Shields.Single().DamageSequence, Is.EqualTo(1));
    }

    [Test]
    public void RepeatedSameTickImpactsRemoveCoverOnlyAfterTheLethalExplosion()
    {
        var shield = _host.Items.Shields.Single();
        _host.Items.DamageShield(_host.World, shield.Id, 1, 999, new("test", 1, "setup"));
        Blast(HeldItem.Salvo, new(0, .25f, 4), 2);
        var expected = _host.Items.Explosion(new(0, .25f, 4), Vector3.Zero, HeldItem.Salvo,
            VehicleHull.ClosestPoint(Pose(2), new(0, .25f, 4))).Damage;
        Assert.That(_host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(1000 - expected).Within(.001));
        Assert.That(_host.Items.Events.Count(e => e.Impact), Is.EqualTo(2));
        Assert.That(_host.World.Events.Entries.Count(e => e.Kind == "Destroyed" && e.Cause == "Shield"), Is.EqualTo(1));
        Step();
        Assert.That(_host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(1000 - expected).Within(.001));
    }

    [TestCase(4f, true)]
    [TestCase(-3f, false)]
    public void MineContactUsesCurrentContactPositionAndDoesNotAddRadialDamage(float z, bool blocked)
    {
        _host.Join(3); Step();
        var items = _host.Items;
        var mine = new ProxyMineState(items.TokenHighWater + 1, 1, new(0, 0, z), default, Vector3.UnitY, 30);
        items.Restore(new(items.Revision + 1, _host.Snapshot(), items.Slots, [], [], mines: [mine], shields: items.Shields), items.Revision + 1, mine.Id);
        _host.Step(default, v => new(Pose(v.VehicleId), Vector3.UnitY), moveMine: (_, candidate) => new(candidate, 2));
        Assert.That(_host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(blocked ? 1000 : 750));
        Assert.That(items.Shields.Single().HP, Is.EqualTo(blocked ? 750 : 1000));
        Assert.That(_host.World.GetVehicle(3).Damage.CurrentHP, Is.EqualTo(1000));
        Assert.That(items.Mines, Is.Empty);
    }
}
