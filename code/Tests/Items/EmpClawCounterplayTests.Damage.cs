using System.Numerics;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;

namespace Trackstorm.Core.Tests.Items;

internal sealed partial class EmpClawCounterplayTests
{
    private EmpClawAttachment[] BlastFixture(float damage = 5, float radius = 1)
    {
        _host = new(299, new() { MaximumDamage = damage, SalvoDamage = damage, ExplosionRadius = radius, SalvoBlastRadius = radius },
            damageConfiguration: new() { MaxHP = 1000 }, matchConfiguration: new() { MinimumPlayers = 1, CountdownTicks = 1 });
        _host.Join(2); Step(2);
        return SeedClaws();
    }

    private MissileState Explosive(ulong id, HeldItem item, Vector3 end)
    {
        Vector3 start = end + Vector3.UnitY;
        var arc = new SalvoFlight(end + Vector3.UnitY * 2, end, 1, 2, 1, 1);
        // Both observations put the existing projectile on one known final segment.
        return item == HeldItem.Salvo
            ? new(id, 1, arc.At(1), arc.VelocityAt(1), SalvoFlight.LifetimeTicks - 1) { Arc = arc }
            : new(id, 1, start, -Vector3.UnitY * 60, 300);
    }

    private void RestoreCombat(EmpClawAttachment[] claws, params MissileState[] missiles)
    {
        var items = _host.Items;
        items.Restore(new(items.Revision + 1, _host.Snapshot(), items.Slots, missiles, [],
            shields: items.Shields, discardRevision: items.DiscardRevision, empAttachments: claws), items.Revision + 1,
            Math.Max(items.TokenHighWater, missiles.Select(m => m.Id).DefaultIfEmpty(0UL).Max()));
    }

    [TestCase(HeldItem.Missile)]
    [TestCase(HeldItem.Salvo)]
    public void SharedDamagingBlastReachesMembraneEdgeWithoutDamagingCarOrOtherClaws(HeldItem item)
    {
        var claws = BlastFixture();
        Vector3 nose = EmpClawGeometry.Nose(claws[1], Pose(2));
        // Centre lies outside the damage radius, but the near membrane is inside it.
        Vector3 center = nose + Vector3.UnitZ * 1.1f;
        RestoreCombat(claws, Explosive(200, item, center));
        _host.Step(default, v => new(Pose(v.VehicleId), Vector3.UnitY), collide: (_, _) => 1);
        Assert.That(_host.Items.EmpAttachments, Is.EquivalentTo(new[] { claws[0], claws[2] }));
        Assert.That(_host.World.GetVehicle(2).Movement.EnginePowerScale, Is.EqualTo(.2f));
        Assert.That(_host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(1000));
        Assert.That(_host.Items.Events.Count(e => e.Impact), Is.EqualTo(1), "Only the original explosion is published.");
        Assert.That(_host.Items.Missiles, Is.Empty);
    }

    [TestCase(HeldItem.Missile, false)]
    [TestCase(HeldItem.Salvo, false)]
    [TestCase(HeldItem.Missile, true)]
    [TestCase(HeldItem.Salvo, true)]
    public void BlastOutsideMembraneOrWithZeroDamagePreservesAllClaws(HeldItem item, bool zeroDamage)
    {
        var claws = BlastFixture(zeroDamage ? 0 : 5);
        Vector3 center = EmpClawGeometry.Nose(claws[1], Pose(2)) + Vector3.UnitZ * (zeroDamage ? .6f : 1.21f);
        RestoreCombat(claws, Explosive(200, item, center));
        _host.Step(default, v => new(Pose(v.VehicleId), Vector3.UnitY), collide: (_, _) => 1);
        Assert.That(_host.Items.EmpAttachments, Is.EqualTo(claws));
    }

    [TestCase(HeldItem.Missile)]
    [TestCase(HeldItem.Salvo)]
    public void DamagingProjectileSweepHitsNoseBeforeNativeBodyAndNeverMetalAlone(HeldItem item)
    {
        var claws = BlastFixture().Select(c => c with { LocalPosition = c.LocalPosition with { X = c.LocalPosition.X * 3 } }).ToArray();
        Vector3 nose = EmpClawGeometry.Nose(claws[1], Pose(2));
        RestoreCombat(claws, Explosive(200, item, nose));
        _host.Step(default, v => new(Pose(v.VehicleId), Vector3.UnitY));
        Assert.That(_host.Items.EmpAttachments, Is.EquivalentTo(new[] { claws[0], claws[2] }));
        Assert.That(_host.Items.Missiles, Is.Empty);
        Vector3 impact = _host.Items.Events.Single(e => e.Impact).Position;
        Assert.That(Vector3.Distance(impact, nose), Is.EqualTo(EmpClawGeometry.InflatedRadius).Within(.0001));
        // A metal-only sweep offset beyond the membrane must not turn the whole claw into a target.
        RestoreCombat(claws, new MissileState(201, 1, Pose(2).Position + claws[1].LocalPosition - Vector3.UnitX,
            Vector3.UnitX * 120, 300));
        Step();
        Assert.That(_host.Items.EmpAttachments, Is.EqualTo(claws));
        Assert.That(_host.Items.Missiles, Has.Count.EqualTo(1));
    }

    [Test]
    public void OverlappingMissileAndSalvoPopsAreIdempotentAndDoNotAddBlastDamage()
    {
        var claws = BlastFixture(radius: 5);
        Vector3 center = EmpClawGeometry.Nose(claws[1], Pose(2));
        RestoreCombat(claws, Explosive(200, HeldItem.Missile, center), Explosive(201, HeldItem.Salvo, center));
        _host.Step(default, v => new(Pose(v.VehicleId), Vector3.UnitY), collide: (_, _) => 1);
        var impacts = _host.Items.Events.Where(e => e.Impact).ToArray();
        Assert.That(impacts, Has.Length.EqualTo(2));
        float expected = impacts.Sum(e => _host.Items.Explosion(e.Position, Pose(2).Position, e.Item).Damage);
        Assert.That(_host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(1000 - expected).Within(.001));
        Assert.That(_host.Items.EmpAttachments, Is.Empty);
        Assert.That(_host.World.GetVehicle(2).Movement.EnginePowerScale, Is.EqualTo(1));
        var saved = new ItemPublication(_host.Items.Revision, _host.Snapshot(), _host.Items.Slots, [], [], empAttachments: _host.Items.EmpAttachments);
        var restored = new ItemAuthority(_host.Items.Configuration);
        restored.Restore(ItemCodec.DecodeState(ItemCodec.EncodeState(saved)), saved.Revision, _host.Items.TokenHighWater);
        Assert.That(restored.EmpAttachments, Is.Empty, "Recovery cannot resurrect popped identities.");
    }

    [Test]
    public void ShieldCoverBetweenBlastAndMembranePreservesNose()
    {
        var claws = BlastFixture(radius: 5);
        Assert.That(_host.Items.Grant(_host.World, 2, HeldItem.Shield), Is.True);
        Step(2);
        Vector3 center = Pose(2).Position + new Vector3(0, 1, 4);
        RestoreCombat(claws, Explosive(200, HeldItem.Missile, center));
        _host.Step(default, v => new(Pose(v.VehicleId), Vector3.UnitY), collide: (_, _) => 1);
        Assert.That(_host.Items.EmpAttachments, Is.EqualTo(claws));
        Assert.That(_host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(1000));
    }

    [Test]
    public void NonDamagingGunfireDoesNotDestroyNose()
    {
        Assert.That(_host.TryConfigure(0, new Dictionary<string, double> { ["items.machine_gun_damage"] = 0 }, out _), Is.True);
        Gun(); var claws = SeedClaws();
        AimAt(EmpClawGeometry.Nose(claws[1], Pose(2))); Step();
        Assert.That(_host.Items.EmpAttachments, Is.EqualTo(claws));
    }

    [Test]
    public void SalvoBlastDoesNotBroadenStoredAmmoRulesOrShootDownFlyingEmp()
    {
        BlastFixture(radius: 5);
        Assert.That(_host.Items.Grant(_host.World, 2, HeldItem.EmpClaw), Is.True);
        Step(100);
        Vector3 center = Pose(2).Position + EmpClawGeometry.StoredNose(1);
        var emp = new MissileState(201, 1, center, Vector3.UnitY * 10, 300)
            { EmpClaw = new(1, 2, 1, _host.World.State.Tick + 300) };
        var arc = new SalvoFlight(center + Vector3.UnitY * 200, center, 1, 101, 0, 1);
        var salvo = new MissileState(200, 1, arc.Origin, arc.VelocityAt(0), SalvoFlight.LifetimeTicks) { Arc = arc };
        RestoreCombat([], salvo, emp);
        Step(100); // Recover normal deployment before the final blast reaches the magazine.
        Assert.That(_host.Items.EmpClawReady(_host.Items.Slots.Single(s => s.Vehicle == 2)), Is.True);
        _host.Step(default, v => new(Pose(v.VehicleId), Vector3.UnitY), collide: (m, _) => m.EmpClaw is null ? 1 : null);
        Assert.That(_host.Items.Slots.Single(s => s.Vehicle == 2).EmpClaw!.Remaining, Is.EqualTo(3));
        Assert.That(_host.Items.Missiles.Single().Id, Is.EqualTo(201));
    }

    [Test]
    public void MineChassisDamageDoesNotInventRadialNoseDamage()
    {
        var claws = BlastFixture();
        var items = _host.Items;
        var mine = new ProxyMineState(200, 1, Pose(2).Position, Vector3.Zero, Vector3.UnitY, 0);
        items.Restore(new(items.Revision + 1, _host.Snapshot(), items.Slots, [], [], mines: [mine], empAttachments: claws), items.Revision + 1, 200);
        _host.Step(default, v => new(Pose(v.VehicleId), Vector3.UnitY), moveMine: (_, candidate) => new(candidate, 2));
        Assert.That(_host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(750));
        Assert.That(items.EmpAttachments, Is.EqualTo(claws), "Mine damage is contact-only on the chassis; its cosmetic dust is not a damaging blast.");
    }
}
