using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed partial class EmpClawCounterplayTests
{
    private HostVehicleSession _host = null!;
    private Vector3 _aim;
    private bool _held;
    private Func<ulong, Vector3, Vector3, WeaponRayHit?> _ray = null!;
    private Func<ulong, Vector3, Vector3, float, bool>? _contact;
    private static VehiclePhysicsState Pose(ulong vehicle) => new(new(0, 10, vehicle == 1 ? 0 : -20), Quaternion.Identity, Vector3.Zero, Vector3.Zero);

    [SetUp]
    public void Setup()
    {
        _host = new(299, new() { MachineGunSpread = 0, MachineGunFireRate = 60 },
            matchConfiguration: new() { MinimumPlayers = 1, CountdownTicks = 1 });
        _host.Join(2); _held = false; _contact = null; _ray = (_, _, _) => null; _aim = -Vector3.UnitZ;
        Step(2);
    }

    private void Step(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            foreach (var slot in _host.Items.Slots.Where(s => WeaponAim.Supports(s.Active.Item)))
            {
                _host.Items.RequestAim(_host.World, slot.Vehicle, slot.Life, slot.Active.Token, slot.SelectionRevision,
                    _host.World.State.Tick + 1, slot.Vehicle == 1 ? _aim : -Vector3.UnitZ);
            }
            _host.Step(_held ? new(0, 0, 0, 0, InputButtons.UseItem, 0, 0) : default,
                v => new(Pose(v.VehicleId), Vector3.UnitY), raycastWeapon: _ray, contactEmpNose: _contact);
        }
    }

    private void Gun()
    {
        Assert.That(_host.Items.Grant(_host.World, 1, HeldItem.MachineGun), Is.True);
        Step(180);
        var slot = _host.Items.Slots.Single(s => s.Vehicle == 1);
        Assert.That(_host.Items.RequestUse(_host.World, 1, slot.Life, slot.Token), Is.True);
        _held = true;
    }

    private void AimAt(Vector3 center)
    {
        _held = false;
        _aim = Vector3.Normalize(center - (Pose(1).Position + WeaponAim.Pivot));
        Step(40); // Actual accepted articulation must settle; no injected accepted hit.
        var gun = _host.Items.Slots.Single(s => s.Vehicle == 1);
        Assert.That(_host.Items.RequestUse(_host.World, 1, gun.Life, gun.Active.Token), Is.True);
        _held = true;
    }

    private EmpClawAttachment[] SeedClaws()
    {
        ulong tick = _host.World.State.Tick;
        var claws = Enumerable.Range(0, 3).Select(i => new EmpClawAttachment((ulong)(100 + i), 1, 2, 1,
            new((i - 1) * .8f, 1, 2), Vector3.UnitZ, tick, tick + 1800)).ToArray();
        _host.Items.Restore(new(Math.Max(1, _host.Items.Revision), _host.Snapshot(), _host.Items.Slots, [], [], empAttachments: claws),
            Math.Max(1, _host.Items.Revision), 103);
        return claws;
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void MachineGunDestroysOnlyActuallyHitStoredSocketsWithoutDischargeOrDamage(int count)
    {
        Assert.That(_host.Items.Grant(_host.World, 2, HeldItem.EmpClaw), Is.True);
        Gun();
        for (int i = 0; i < count; i++)
        {
            AimAt(Pose(2).Position + EmpClawGeometry.StoredNose(i));
            Step();
            var load = _host.Items.Slots.Single(s => s.Vehicle == 2).EmpClaw!;
            Assert.That(load.Remaining, Is.EqualTo(2 - i));
            Assert.That(load.RoundMask & (1 << i), Is.Zero);
            Assert.That(load.ShotsFired, Is.Zero);
            Assert.That(_host.Items.Events.Count(e => e.EmpPop), Is.EqualTo(1));
            Assert.That(_host.Items.Missiles, Is.Empty);
            Assert.That(_host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(100));
        }
        Assert.That(_host.Items.Slots.Single(s => s.Vehicle == 2).Item, Is.EqualTo(count == 3 ? HeldItem.None : HeldItem.EmpClaw));
        var publication = new ItemPublication(_host.Items.Revision, _host.Snapshot(), _host.Items.Slots, [], _host.Items.Events);
        Assert.That(ItemCodec.DecodeState(ItemCodec.EncodeState(publication)).Slots, Is.EqualTo(publication.Slots));
        Assert.That(ItemCodec.DecodeState(ItemCodec.EncodeState(publication)).Events, Is.EqualTo(publication.Events));
    }

    [Test]
    public void BulletPopsSpecificBalloonAndRemainingClawsKeepTheirOriginalDeadlines()
    {
        Gun(); var claws = SeedClaws();
        AimAt(EmpClawGeometry.Nose(claws[1], Pose(2)));
        Step();
        Assert.That(_host.Items.EmpAttachments.Select(c => c.Id), Is.EquivalentTo(new[] { claws[0].Id, claws[2].Id }));
        Assert.That(_host.Items.EmpAttachments, Is.EquivalentTo(new[] { claws[0], claws[2] }));
        Assert.That(_host.World.GetVehicle(2).Movement.EnginePowerScale, Is.EqualTo(.2f));
        Assert.That(_host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(100));
    }

    [Test]
    public void SimultaneousWorldPopsRemoveOnlyTheirIdentitiesAndClearFinalPowerAtAtomicBoundary()
    {
        var claws = SeedClaws();
        _contact = (_, _, center, radius) => center.X < .1f;
        Step();
        Assert.That(_host.Items.EmpAttachments.Single(), Is.EqualTo(claws[2]));
        Assert.That(_host.World.GetVehicle(2).Movement.EnginePowerScale, Is.EqualTo(.2f));
        _contact = (_, _, _, _) => true;
        Step();
        Assert.That(_host.Items.EmpAttachments, Is.Empty);
        Assert.That(_host.World.GetVehicle(2).Movement.EnginePowerScale, Is.EqualTo(1));
        Assert.That(_host.World.GetVehicle(2).Damage.LastDamage, Is.Null);
    }

    [Test]
    public void CoverAheadOfMembraneStopsBulletWithoutPoppingIt()
    {
        Gun(); var claws = SeedClaws();
        AimAt(EmpClawGeometry.Nose(claws[1], Pose(2)));
        _ray = (_, _, _) => new(.001f, 0);
        Step();
        Assert.That(_host.Items.EmpAttachments, Is.EqualTo(claws));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ExistingMissileBlastDestroysOnlyRoundsInsideItsDamagingRadius(bool inside)
    {
        _host = new(299, damageConfiguration: new() { MaxHP = 1000 },
            matchConfiguration: new() { MinimumPlayers = 1, CountdownTicks = 1 });
        _host.Join(2); Step(2);
        Assert.That(_host.Items.Grant(_host.World, 2, HeldItem.EmpClaw), Is.True);
        Step(100);
        Vector3 center = Pose(2).Position + EmpClawGeometry.StoredNose(1) + (inside ? Vector3.UnitY : Vector3.UnitY * 20);
        var missile = new MissileState(100, 1, center + Vector3.UnitZ * 100.5f, -Vector3.UnitZ * 60, 300);
        _host.Items.Restore(new(_host.Items.Revision, _host.Snapshot(), _host.Items.Slots, [missile], []), _host.Items.Revision, 100);
        Step(100); // Recovery redeploys the selected hardware while the missile approaches.
        _host.Step(default, v => new(Pose(v.VehicleId), Vector3.UnitY), collide: (_, _) => .5f);
        var target = _host.Items.Slots.Single(s => s.Vehicle == 2);
        Assert.That(target.EmpClaw!.Remaining, Is.EqualTo(inside ? 0 : 3));
        Assert.That(target.EmpClaw.ShotsFired, Is.Zero);
        Assert.That(_host.Items.Events.Count(e => e.EmpPop), Is.EqualTo(inside ? 3 : 0));
        Assert.That(_host.Items.Missiles, Is.Empty, "Destroyed ammunition produces no secondary projectile.");
        float expectedDamage = _host.Items.Explosion(center, Pose(2).Position).Damage;
        Assert.That(_host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(1000 - expectedDamage).Within(.001), "Only the original missile deals damage.");
    }

    [Test]
    public void SimultaneousShieldContactsPopBothClawsWithoutEmpDamageOrShieldConsumption()
    {
        Assert.That(_host.Items.Grant(_host.World, 2, HeldItem.Shield), Is.True);
        Step(2);
        var shield = _host.Items.Shields.Single();
        ulong tick = _host.World.State.Tick;
        var missiles = new[] { 100UL, 101UL }.Select(id => new MissileState(id, 1,
            Pose(2).Position + ShieldGeometry.Center + Vector3.UnitZ * .6f, -Vector3.UnitZ * 60, 300)
            { EmpClaw = new(1, 2, 1, tick + 300) }).ToArray();
        _host.Items.Restore(new(_host.Items.Revision, _host.Snapshot(), _host.Items.Slots, missiles, [], shields: [shield]),
            _host.Items.Revision, 101);
        Step();
        Assert.That(_host.Items.Missiles, Is.Empty);
        Assert.That(_host.Items.EmpAttachments, Is.Empty);
        Assert.That(_host.Items.Events.Count(e => e.EmpPop), Is.EqualTo(2));
        Assert.That(_host.World.GetVehicle(2).Movement.EnginePowerScale, Is.EqualTo(1));
        Assert.That(_host.World.GetVehicle(2).Damage.LastDamage, Is.Null);
        Assert.That(_host.Items.Shields.Single(), Is.EqualTo(shield));
    }

    [Test]
    public void NewAttachmentWhoseNoseAlreadyTouchesWorldPopsBeforeApplyingEmp()
    {
        ulong tick = _host.World.State.Tick;
        var missile = new MissileState(100, 1, Pose(2).Position + Vector3.UnitZ * 2, -Vector3.UnitZ * 60, 300)
        { EmpClaw = new(1, 2, 1, tick + 300) };
        _host.Items.Restore(new(1, _host.Snapshot(), [], [missile], []), 1, 100);
        _ray = (_, _, _) => new(.5f, 2) { Normal = Vector3.UnitZ };
        _contact = (_, _, _, _) => true;
        Step();
        Assert.That(_host.Items.EmpAttachments, Is.Empty);
        Assert.That(_host.Items.Events.Single().EmpPop, Is.True);
        Assert.That(_host.World.GetVehicle(2).Movement.EnginePowerScale, Is.EqualTo(1));
        Assert.That(_host.World.GetVehicle(2).Damage.LastDamage, Is.Null);
    }

    [Test]
    public void TwoBulletsInOneBoundaryPopOnlyTwoOfThreeOverlappingNoses()
    {
        Assert.That(_host.TryConfigure(0, new Dictionary<string, double> { ["items.machine_gun_fire_rate"] = 120 }, out _), Is.True);
        Gun();
        var claws = SeedClaws().Select(c => c with { LocalPosition = new(0, 1, 2) }).ToArray();
        _host.Items.Restore(new(_host.Items.Revision, _host.Snapshot(), _host.Items.Slots, [], [], empAttachments: claws), _host.Items.Revision, 103);
        AimAt(EmpClawGeometry.Nose(claws[0], Pose(2)));
        Step();
        Assert.That(_host.Items.EmpAttachments.Single().Id, Is.EqualTo(claws[2].Id));
        Assert.That(_host.Items.Events.Count(e => e.Item == HeldItem.MachineGun), Is.EqualTo(2));
        Assert.That(_host.World.GetVehicle(2).Movement.EnginePowerScale, Is.EqualTo(.2f));
        Assert.That(_host.World.GetVehicle(2).Damage.LastDamage, Is.Null);
    }

    [Test]
    public void ShieldDestroyedEarlierInTheSameBatchCannotProtectLaterClawContact()
    {
        Assert.That(_host.Items.Grant(_host.World, 2, HeldItem.Shield), Is.True);
        Step(2);
        ulong tick = _host.World.State.Tick;
        Vector3 start = Pose(2).Position + ShieldGeometry.Center + Vector3.UnitZ * .6f;
        var missiles = new[]
        {
            new MissileState(100, 1, start, -Vector3.UnitZ * 60, 300),
            new MissileState(101, 1, start, -Vector3.UnitZ * 60, 300) { EmpClaw = new(1, 2, 1, tick + 300) },
        };
        _host.Items.Restore(new(_host.Items.Revision, _host.Snapshot(), _host.Items.Slots, missiles, [], shields: _host.Items.Shields.Select(s => s with { HP = 1, DamageSequence = 1 })), _host.Items.Revision, 101);
        _host.Step(default, v => new(Pose(v.VehicleId), Vector3.UnitY), collide: (_, _) => .5f,
            raycastEmpClaw: (_, _, _, _) => new(.9f, 2) { Normal = Vector3.UnitZ });
        Assert.That(_host.Items.Shields, Is.Empty);
        Assert.That(_host.Items.EmpAttachments.Single().Id, Is.EqualTo(101));
        Assert.That(_host.World.GetVehicle(2).Damage.LastDamage, Is.Null, "The first shield absorbed the original blast.");
    }

    [Test]
    public void RestoredDestroyedSocketsNeverReappearAndCannotBeFiredByDelayedCapability()
    {
        Assert.That(_host.Items.Grant(_host.World, 2, HeldItem.EmpClaw), Is.True);
        Gun();
        AimAt(Pose(2).Position + EmpClawGeometry.StoredNose(1)); Step(); _held = false;
        var before = _host.Items.Slots.Single(s => s.Vehicle == 2);
        var checkpoint = new ResumeCheckpoint(new ItemPublication(_host.Items.Revision, _host.Snapshot(), _host.Items.Slots, [], []),
            _host.World.State.Match!, null, _host.Configuration);
        _host = HostVehicleSession.Restore(ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(checkpoint)), _host.CaptureAuthority(), 1);
        Assert.That(_host.Items.Slots.Single(s => s.Vehicle == 2), Is.EqualTo(before));
        Step(100);
        Assert.That(_host.Items.RequestUse(_host.World, 2, before.Life, before.Token), Is.True);
        Step();
        Assert.That(_host.Items.Slots.Single(s => s.Vehicle == 2).EmpClaw!.RoundMask, Is.EqualTo(4));
        Assert.That(_host.Items.RequestUse(_host.World, 2, before.Life, before.Token), Is.False);
    }

    [TestCase(0f, 1f, 0f)]
    [TestCase(0f, -1f, 0f)]
    [TestCase(1f, 0f, 0f)]
    [TestCase(0f, 0f, -1f)]
    public void NoseOnlyRegionFollowsAnyContactNormalAndVehicleOrientation(float x, float y, float z)
    {
        var claw = new EmpClawAttachment(1, 1, 2, 1, new(1, -1, 2), new(x, y, z), 1, 1801);
        var pose = new VehiclePhysicsState(new(10, 20, 30), Quaternion.CreateFromYawPitchRoll(1, 2, 3), Vector3.Zero, Vector3.Zero);
        Vector3 nose = EmpClawGeometry.Nose(claw, pose);
        Assert.That(EmpClawGeometry.Intersect(nose - Vector3.UnitX, nose + Vector3.UnitX, nose, EmpClawGeometry.InflatedRadius), Is.Not.Null);
        Vector3 metal = pose.Position + Vector3.Transform(claw.LocalPosition, pose.Orientation);
        Assert.That(EmpClawGeometry.Intersect(metal, metal, nose, EmpClawGeometry.InflatedRadius), Is.Null, "Metal contact is outside the nose membrane.");
    }
}
