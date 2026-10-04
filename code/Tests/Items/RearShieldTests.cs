using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class RearShieldTests
{
    private Trackstorm.Core.Simulation.Simulation _world = null!;
    private ItemAuthority _items = null!;
    private Vector3 _shooter;
    private Quaternion _heading;
    private static readonly DamageContext Hit = new("machine-gun", 2, "test");

    [SetUp]
    public void SetUp()
    {
        _world = new(new(60));
        _shooter = new(0, 0.2f, 10); _heading = Quaternion.Identity;
        _world.AddVehicle(1, new(), new() { MaxHP = 1000 }, Pose(Vector3.Zero));
        _world.AddVehicle(2, new(), new() { MaxHP = 1000 }, Pose(_shooter));
        _items = new(new() { MachineGunFireRate = 60, MachineGunSpread = 0 });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SelectionCyclesPreserveExactSlotDamagedIdentityWithoutPlacementAdapter(bool second)
    {
        if (second) { _items.Grant(_world, 1, HeldItem.Wrench); }
        _items.Grant(_world, 1, HeldItem.Tombstone);
        if (second) { _items.Switch(_world, 1, 1, 1); }
        var original = _items.Tombstones.Single();
        _items.DamageTombstone(_world, original.Id, 7, 123, Hit);
        var slot = _items.Slots.Single();
        Assert.That(_items.RequestUse(_world, 1, 1, original.Token), Is.True);
        Step();
        Assert.That(_items.Slots.Single(), Is.EqualTo(slot));
        Assert.That(_items.Tombstones.Single(), Is.EqualTo(original with { Stage = TombstoneStage.RearShield, HP = 877, DamageSequence = 7 }));
        _items.RequestUse(_world, 1, 1, original.Token); Step();
        Assert.That(_items.Events, Is.Empty);
        Assert.That(_items.DamageTombstone(_world, original.Id, 6, 500, Hit), Is.Null);
        for (ulong revision = slot.SelectionRevision + 1; revision <= slot.SelectionRevision + 20; revision++)
        {
            Assert.That(_items.Switch(_world, 1, 1, revision), Is.True);
            Assert.That(_items.Switch(_world, 1, 1, revision), Is.False);
            Step();
            bool exposed = (revision - slot.SelectionRevision) % 2 == 0;
            Assert.That(_items.Tombstones.Single(), Is.EqualTo(original with
                { Stage = exposed ? TombstoneStage.RearShield : TombstoneStage.Held, HP = 877, DamageSequence = 7 }));
            Assert.That(_items.Slots.Single().Full, Is.EqualTo(second));
        }
    }

    [TestCase(0, 10, true)]
    [TestCase(10, 0, false)]
    [TestCase(0, -10, false)]
    [TestCase(4, 10, true)]
    [TestCase(7, 10, true)]
    [TestCase(12, 10, false)]
    public void RaysUsePhysicalPanelsAndLeaveUncoveredFrontAndForwardSidesVulnerable(float x, float z, bool blocked)
    {
        Deploy();
        _shooter = new(x, 0.2f, z);
        var direction = Vector3.Normalize(-_shooter);
        _heading = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(-direction.X, -direction.Z));
        Fire();
        Assert.That(_world.GetVehicle(1).Damage.CurrentHP < 1000, Is.EqualTo(!blocked));
        Assert.That(_items.Tombstones.Single().HP < 1000, Is.EqualTo(blocked));
    }

    [Test]
    public void CloserWorldCoverWinsAndRejectedBatchCannotPartlyDamageShield()
    {
        Deploy(); Arm();
        Step(fire: true, ray: (_, _, _) => new(0.001f, 0));
        Assert.That(_items.Tombstones.Single().HP, Is.EqualTo(1000));
        var before = _items.Tombstones.Single();
        Assert.Throws<ArgumentException>(() => Step(fire: true, invalidTick: true));
        Assert.That(_items.Tombstones.Single(), Is.EqualTo(before));
        Step(fire: true);
        Assert.That(_items.Tombstones.Single().HP, Is.LessThan(1000));
    }

    [Test]
    public void LethalRoundIsBlockedOnceThenFurtherRoundsReachVehicle()
    {
        Deploy();
        var stone = _items.Tombstones.Single();
        _items.DamageTombstone(_world, stone.Id, 12, 999, Hit);
        Fire();
        Assert.That(_items.Tombstones, Is.Empty);
        Assert.That(_world.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(1000));
        Step(fire: true);
        Assert.That(_world.GetVehicle(1).Damage.CurrentHP, Is.LessThan(1000));
        Assert.That(_world.Events.Entries.Count(e => e.Kind == "Destroyed" && e.Cause == "Tombstone"), Is.EqualTo(1));
    }

    [Test]
    public void DuplicateContactPointsUseOneDamageAndRecoveryPreservesCooldown()
    {
        Deploy();
        var contact = new VehicleContact(new(0, 0, -20), Vector3.UnitZ, 0, 2, localPosition: TombstoneGeometry.Center);
        var sameImpactChassis = new VehicleContact(new(0, 0, -20), Vector3.UnitZ, 0, 2, localPosition: new(0, 0, 2.48f));
        Step(contacts: [contact, contact, contact, sameImpactChassis]);
        var stone = _items.Tombstones.Single();
        Assert.That(stone.HP, Is.EqualTo(952));
        Assert.That(_world.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(1000));
        var world = new Trackstorm.Core.Networking.Replication.WorldSnapshot(99, _world.State.Tick,
            _world.State.Vehicles.Select(v => new Trackstorm.Core.Networking.Replication.ReplicatedVehicle(v, 0)));
        var publication = ItemCodec.DecodeState(ItemCodec.EncodeState(new(1, world, _items.Slots, [], [], tombstones: _items.Tombstones)));
        var restored = new ItemAuthority(_items.Configuration);
        restored.Restore(publication, _items.Revision, _items.TokenHighWater);
        _items = restored;
        Step(contacts: [contact]);
        Assert.That(_items.Tombstones.Single(), Is.EqualTo(stone));
        Assert.That(_world.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(1000));
        var side = new VehicleContact(new(-20, 0, 0), Vector3.UnitX, 0, 2, localPosition: new(1.3f, 0, 0));
        Step(contacts: [side]);
        Assert.That(_world.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(952));
    }

    [Test]
    public void RotatedMovingPoseAndEdgeGeometryRemainSpatial()
    {
        var pose = new VehiclePhysicsState(new(20, 4, 80), Quaternion.CreateFromYawPitchRoll(1.2f, 0.3f, -0.2f), new(4, 0, 3), Vector3.Zero);
        Vector3 World(Vector3 p) => pose.Position + Vector3.Transform(p, pose.Orientation);
        Assert.That(TombstoneGeometry.Intersect(pose, World(new(0, 0.2f, 8)), World(Vector3.Zero)), Is.Not.Null);
        Assert.That(TombstoneGeometry.Intersect(pose, World(new(4, 0.2f, 8)), World(new(4, 0.2f, -8))), Is.Null);
        Assert.That(TombstoneGeometry.Intersect(pose, World(new(0, 3, 8)), World(new(0, 3, -8))), Is.Null);
    }

    [TestCase(-5, 2.4f, true)]
    [TestCase(5, 2.4f, true)]
    [TestCase(-5, 0, false)]
    [TestCase(5, 0, false)]
    public void WraparoundWingsProtectRearSidesButLeaveForwardSidesOpen(float x, float z, bool covered)
    {
        var pose = new VehiclePhysicsState(new(20, 4, 80), Quaternion.CreateFromYawPitchRoll(.7f, .2f, -.3f), Vector3.Zero, Vector3.Zero);
        Vector3 World(Vector3 p) => pose.Position + Vector3.Transform(p, pose.Orientation);
        Assert.That(TombstoneGeometry.Intersect(pose, World(new(x, 1.1f, z)), World(new(0, 1.1f, z))).HasValue, Is.EqualTo(covered));
        Assert.That(TombstoneGeometry.Contains(new(Math.Sign(x) * 1.85f, 1.1f, z)), Is.EqualTo(covered));
        Assert.That(TombstoneGeometry.Contains(new(0, .25f, 2.4f)), Is.False, "The open interior is not solid armor.");
    }

    [Test]
    public void FullHeightRearArmorStopsRoofLevelHitsAndAllowsHitsAboveIt()
    {
        var pose = Pose(Vector3.Zero);
        Assert.That(TombstoneGeometry.Intersect(pose, new(0, 1.3f, 6), new(0, 1.3f, 0)), Is.Not.Null);
        Assert.That(TombstoneGeometry.Intersect(pose, new(0, 1.7f, 6), new(0, 1.7f, 0)), Is.Null);
    }

    [Test]
    public void MissileInterceptExplodesOnceWithoutAlsoDamagingProtectedVehicle()
    {
        Deploy();
        _items.Grant(_world, 2, HeldItem.Missile);
        _items.RequestUse(_world, 2, 1, _items.Slots.Single(s => s.Vehicle == 2).Token);
        int impacts = 0;
        for (int i = 0; i < 12; i++) { Step(); impacts += _items.Events.Count(e => e.Impact); }
        Assert.That(impacts, Is.EqualTo(1));
        Assert.That(_items.Missiles, Is.Empty);
        Assert.That(_items.Tombstones.Single().HP, Is.LessThan(1000));
        Assert.That(_items.Tombstones.Single().DamageSequence, Is.EqualTo(1));
        Assert.That(_world.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(1000));
        Assert.That(_world.GetVehicle(2).Damage.CurrentHP, Is.LessThan(1000), "Other blast targets keep existing radial damage");
    }

    [Test]
    public void IncomingVehicleContactDamagesStationaryDefendersShieldOnce()
    {
        Deploy();
        ulong tick = _world.State.Tick + 1;
        var input = new InputFrame(tick, 0, 0, 0, 0, 0, 0);
        var point = TombstoneGeometry.Center - _shooter;
        var contact = new VehicleContact(new(0, 0, -20), Vector3.UnitZ, 0, 1, localPosition: point);
        _items.Step(_world, input, [new(1, input, new(Pose(Vector3.Zero), Vector3.UnitY)),
            new(2, input, new(Pose(_shooter), Vector3.UnitY, [contact, contact]))], (_, _) => null);
        Assert.That(_items.Tombstones.Single().HP, Is.EqualTo(952));
        Assert.That(_world.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(1000));
        Assert.That(_world.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(952), "Striking vehicle retains its collision damage");
    }

    private void Deploy()
    {
        _items.Grant(_world, 1, HeldItem.Tombstone);
        Step();
    }

    [Test]
    public void TwoPhysicalSlotsExposeOnlySelectedPoolAndDestructionClearsThatSlot()
    {
        Deploy();
        _items.Grant(_world, 1, HeldItem.Tombstone);
        _items.Switch(_world, 1, 1, 1);
        Step();
        Assert.That(_items.Tombstones.Select(s => s.Stage), Is.EqualTo(new[] { TombstoneStage.Held, TombstoneStage.RearShield }));
        Fire();
        Assert.That(_items.Tombstones.Select(s => s.HP), Is.EqualTo(new[] { 1000f, 997.75f }));
        Assert.That(_items.Slots.Single(s => s.Vehicle == 1).SecondItem, Is.EqualTo(HeldItem.Tombstone));
        var second = _items.Tombstones[1];
        _items.Switch(_world, 1, 1, 2); Step();
        Assert.That(_items.Tombstones[1], Is.EqualTo(second with { Stage = TombstoneStage.Held }));
        _items.Switch(_world, 1, 1, 3); Step();
        Assert.That(_items.Tombstones[1], Is.EqualTo(second));
        _items.DamageTombstone(_world, second.Id, 10, 997, Hit);
        _items.RequestUse(_world, 2, 1, _items.Slots.Single(s => s.Vehicle == 2).Token);
        Step(fire: true);
        Assert.That(_items.Slots.Single(s => s.Vehicle == 1).SecondItem, Is.EqualTo(HeldItem.None));
        Assert.That(_items.Slots.Single(s => s.Vehicle == 1).Item, Is.EqualTo(HeldItem.Tombstone));
        Assert.That(_items.Tombstones.Single().Stage, Is.EqualTo(TombstoneStage.Held));
        Assert.That(_world.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(1000));
    }

    [Test]
    public void StoredShieldDoesNotProtectAndReselectionRestoresDamagedCover()
    {
        Deploy(); Fire();
        var damaged = _items.Tombstones.Single();
        _items.Switch(_world, 1, 1, 1);
        Step(fire: true);
        Assert.That(_world.GetVehicle(1).Damage.CurrentHP, Is.LessThan(1000));
        Assert.That(_items.Tombstones.Single(), Is.EqualTo(damaged with { Stage = TombstoneStage.Held }));
        float hp = _world.GetVehicle(1).Damage.CurrentHP;
        _items.Switch(_world, 1, 1, 2);
        Step(fire: true);
        Assert.That(_world.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(hp));
        Assert.That(_items.Tombstones.Single().HP, Is.LessThan(damaged.HP));
        Assert.That(_items.Tombstones.Single().Id, Is.EqualTo(damaged.Id));
    }
    private void Arm()
    {
        _items.Grant(_world, 2, HeldItem.MachineGun);
        _items.RequestUse(_world, 2, 1, _items.Slots.Single(s => s.Vehicle == 2).Token);
    }
    private void Fire() { Arm(); Step(fire: true); }
    private void Step(bool fire = false, VehicleContact[]? contacts = null, Func<ulong, Vector3, Vector3, WeaponRayHit?>? ray = null, bool invalidTick = false)
    {
        ulong tick = _world.State.Tick + 1;
        var input = new InputFrame(tick, 0, 0, 0, 0, 0, 0);
        var shot = new InputFrame(invalidTick ? tick + 1 : tick, 0, 0, 0, fire ? InputButtons.UseItem : 0, 0, 0);
        _items.Step(_world, input, [new(1, input, new(Pose(Vector3.Zero), Vector3.UnitY, contacts)),
            new(2, shot, new(Pose(_shooter, _heading), Vector3.UnitY))], (_, _) => null,
            raycastWeapon: ray ?? ((_, start, end) => new(Vector3.Distance(start, Vector3.Zero) / Vector3.Distance(start, end), 1)));
    }
    private static VehiclePhysicsState Pose(Vector3 p, Quaternion? q = null) => new(p, q ?? Quaternion.Identity, Vector3.Zero, Vector3.Zero);
}
