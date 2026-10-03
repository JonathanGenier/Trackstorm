using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class WorldWallTests
{
    private static readonly VehiclePhysicsState Placement = new(new(0, 2, 8), Quaternion.Identity, new(2, 0, 0), new(0, 0.1f, 0));
    private static readonly DamageContext Hit = new("machine-gun", 2, "bullet");

    [TestCase(false)]
    [TestCase(true)]
    public void SingleUseClearsExactSlotAndKeepsDamagedPoolAndReplayMemory(bool second)
    {
        var host = Start();
        host.Items.Grant(host.World, 1, second ? HeldItem.Wrench : HeldItem.Tombstone);
        host.Items.Grant(host.World, 1, second ? HeldItem.Tombstone : HeldItem.Wrench);
        if (second) { host.Items.Switch(host.World, 1, 1, 1); }
        var original = host.Items.Tombstones.Single();
        host.Items.DamageTombstone(host.World, original.Id, 7, 321, Hit);
        var slot = host.Items.Slots.Single();
        Assert.That(host.Items.RequestUse(host.World, 1, 1, original.Token), Is.True);
        Assert.That(host.Items.RequestUse(host.World, 1, 1, original.Token), Is.False);
        host.Step(default, Observe, placeTombstone: (selected, _, _) =>
        {
            Assert.That(selected.Vehicle, Is.EqualTo(original.Owner));
            Assert.That(selected.Token, Is.EqualTo(original.Token));
            Assert.That(selected.Item, Is.EqualTo(HeldItem.Tombstone));
            return Placement;
        });
        var wall = host.Items.Tombstones.Single();
        Assert.That(wall, Is.EqualTo(original with { Stage = TombstoneStage.WorldWall, Life = 0, Token = 0, HP = 679,
            DamageSequence = 7, Position = Placement.Position, Orientation = Placement.Orientation,
            LinearVelocity = Placement.LinearVelocity, AngularVelocity = Placement.AngularVelocity, ExpiresAtTick = host.World.State.Tick + 7200 }));
        Assert.That(host.Items.Slots.Single(), Is.EqualTo(second ? slot with { SecondItem = HeldItem.None } : slot with { Item = HeldItem.None }));
        Assert.That(host.Items.RequestUse(host.World, 1, 1, original.Token), Is.False);
        host.Step(default, Observe, placeTombstone: (_, _, _) => Placement);
        Assert.That(host.Items.Tombstones.Single(), Is.EqualTo(wall));
        Assert.That(host.Items.Events, Is.Empty);
    }

    [Test]
    public void StowedOrSwitchedBeforeCommitCannotDeployAndFailedPlacementRetainsShield()
    {
        var host = Start(); host.Items.Grant(host.World, 1, HeldItem.Tombstone);
        var shield = host.Items.Tombstones.Single();
        host.Items.RequestUse(host.World, 1, 1, shield.Token);
        host.Items.Switch(host.World, 1, 1, 1);
        host.Step(default, Observe, placeTombstone: (_, _, _) => Placement);
        Assert.That(host.Items.Tombstones.Single().Stage, Is.EqualTo(TombstoneStage.Held));
        Assert.That(host.Items.RequestUse(host.World, 1, 1, shield.Token), Is.False);
        host.Items.Switch(host.World, 1, 1, 2);
        host.Items.RequestUse(host.World, 1, 1, shield.Token);
        host.Step(default, Observe, placeTombstone: (_, _, _) => null);
        Assert.That(host.Items.Tombstones.Single(), Is.EqualTo(shield));
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.Tombstone));
    }

    [Test]
    public void RejectedVehicleBatchCannotCommitDeploymentOrMotion()
    {
        var host = Start(); host.Items.Grant(host.World, 1, HeldItem.Tombstone);
        var shield = host.Items.Tombstones.Single(); host.Items.RequestUse(host.World, 1, 1, shield.Token);
        var badTick = new InputFrame(host.World.State.Tick, 0, 0, 0, 0, 0, 0);
        Assert.Throws<ArgumentException>(() => host.Items.Step(host.World, badTick,
            [new(1, badTick, Observe(host.World.GetVehicle(1)), [])], (_, _) => null, placeTombstone: (_, _, _) => Placement));
        Assert.That(host.Items.Tombstones.Single(), Is.EqualTo(shield));
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.Tombstone));
        host.Step(default, Observe, placeTombstone: (_, _, _) => Placement);
        Assert.That(host.Items.Tombstones.Single().Stage, Is.EqualTo(TombstoneStage.WorldWall));
    }

    [Test]
    public void MotionAndCompleteRecoveryRetainIndependentPoseVelocitiesAndWatermark()
    {
        var host = Start(); var wall = Deploy(host);
        host.Items.DamageTombstone(host.World, wall.Id, 11, 200, Hit);
        var moved = new VehiclePhysicsState(new(10, 3, 20), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.4f), new(4, 0, 1), new(0, 2, 0));
        host.Step(default, Observe, observeTombstone: _ => new(moved));
        var state = host.Items.Tombstones.Single();
        Assert.That(state.Position, Is.EqualTo(moved.Position));
        Assert.That(state.HP, Is.EqualTo(800));
        var publication = ItemCodec.DecodeState(ItemCodec.EncodeState(new(1, host.Snapshot(), host.Items.Slots, [], [], tombstones: host.Items.Tombstones)));
        var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(publication, host.World.State.Match!, null, host.Configuration)));
        var restored = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 1);
        Assert.That(restored.Items.Tombstones.Single(), Is.EqualTo(state));
        Assert.That(restored.Items.DamageTombstone(restored.World, wall.Id, 11, 100, Hit), Is.Null);
        Assert.That(host.PrepareJoin(3, 2)!.Items.Tombstones.Single(), Is.EqualTo(state));
        host.Items.RemovePlayer(1);
        host.Step(default, Observe);
        Assert.That(host.Items.Tombstones.Single(), Is.EqualTo(state));
    }

    [Test]
    public void BulletsBlockOnIndependentWallAndLethalHitRemovesItExactlyOnce()
    {
        var host = Start(); var wall = Deploy(host);
        host.Items.DamageTombstone(host.World, wall.Id, 1, 999, Hit);
        host.Items.Grant(host.World, 1, HeldItem.MachineGun);
        host.TryConfigure(0, new Dictionary<string, double> { ["items.machine_gun_spread"] = 0, ["items.machine_gun_fire_rate"] = 60 }, out _);
        var hp = host.World.GetVehicle(1).Damage;
        host.Items.RequestUse(host.World, 1, 1, host.Items.Slots.Single().Token);
        VehicleObservation Shooter(VehicleSnapshot s) => new(new(new(0, 2, 12), Quaternion.Identity, Vector3.Zero, Vector3.Zero), Vector3.UnitY);
        host.Step(new(1, 0, 0, 0, InputButtons.UseItem, 0, 0), Shooter, raycastWeapon: (_, _, _) => null);
        Assert.That(host.Items.Tombstones, Is.Empty);
        Assert.That(host.Items.Events.Single().Impact, Is.True);
        Assert.That(host.World.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(hp.CurrentHP));
        Assert.That(host.Items.DamageTombstone(host.World, wall.Id, 99, 1000, Hit), Is.Null);
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.MachineGun));
    }

    [Test]
    public void MultipleWallsTakeIndependentCollisionDamageWithoutOwningInventory()
    {
        var host = Start(); var first = Deploy(host); var second = Deploy(host);
        VehicleObservation Contact(VehicleSnapshot s) => new(s.Movement.Physics, Vector3.UnitY,
            [new(new(0, 0, -20), Vector3.UnitZ, 0, 0, tombstone: first.Id), new(new(0, 0, -20), Vector3.UnitZ, 0, 0, tombstone: first.Id)]);
        host.Step(default, Contact);
        var damaged = host.Items.Tombstones.Single(s => s.Id == first.Id);
        Assert.That(damaged.HP, Is.LessThan(1000));
        Assert.That(damaged.DamageSequence, Is.EqualTo(1));
        Assert.That(host.Items.Tombstones.Single(s => s.Id == second.Id).HP, Is.EqualTo(1000));
        host.Step(default, Contact);
        Assert.That(host.Items.Tombstones.Single(s => s.Id == first.Id).HP, Is.EqualTo(damaged.HP));
    }

    [TestCase(HeldItem.Missile)]
    [TestCase(HeldItem.Salvo)]
    public void ExplosionsDamageAndPushIndependentWallsOncePerImpact(HeldItem weapon)
    {
        var host = Start(); var wall = Deploy(host);
        host.Items.Grant(host.World, 1, weapon);
        host.TryConfigure(0, new Dictionary<string, double> { ["items.salvo_arc_height"] = 1, ["items.salvo_launch_height"] = 1 }, out _);
        host.Items.RequestUse(host.World, 1, 1, host.Items.Slots.Single().Active.Token);
        VehicleObservation Shooter(VehicleSnapshot s) => new(new(new(0, 2, 12), Quaternion.Identity, Vector3.Zero, Vector3.Zero), Vector3.UnitY);
        host.Step(default, Shooter, collide: (_, _) => 1, ground: point => new(point.X, 2, point.Z));
        var hit = host.Items.Tombstones.Single();
        Assert.That(hit.HP, Is.LessThan(1000));
        Assert.That(hit.DamageSequence, Is.EqualTo(1));
        Assert.That(hit.LinearVelocity, Is.Not.EqualTo(wall.LinearVelocity));
        host.Step(default, Shooter);
        Assert.That(host.Items.Tombstones.Single(), Is.EqualTo(hit), "No repeated blast on the next tick");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void WallContactSharesMomentumWithoutStoppingCarOrDuplicatingManifoldImpulse(bool duplicate)
    {
        var host = Start(); var wall = Deploy(host);
        var contact = new VehicleContact(new(0, 0, -18), Vector3.UnitZ, 0, 0,
            localPosition: wall.Position - host.World.GetVehicle(1).ObservedPhysics.Position, tombstone: wall.Id);
        VehicleObservation Contact(VehicleSnapshot s) => new(new(s.ObservedPhysics.Position, Quaternion.Identity, Vector3.Zero, Vector3.Zero),
            Vector3.UnitY, duplicate ? [contact, contact] : [contact]);
        host.Step(default, Contact);
        var pushed = host.Items.Tombstones.Single();
        float expected = -18 * host.World.MovementTuning(1).Mass / (host.World.MovementTuning(1).Mass + wall.WallMass);
        Assert.That(host.World.GetVehicle(1).ObservedPhysics.LinearVelocity.Z, Is.EqualTo(expected).Within(0.0001f));
        Assert.That(pushed.LinearVelocity.Z, Is.EqualTo(expected).Within(0.0001f));
        Assert.That(pushed.HP, Is.LessThan(wall.HP));
        Assert.That(host.World.GetVehicle(1).Damage.CurrentHP, Is.LessThan(1000), "Normal collision damage remains");
        VehicleObservation MovingContact(VehicleSnapshot s) => new(new(s.ObservedPhysics.Position, Quaternion.Identity, Vector3.Zero, Vector3.Zero),
            Vector3.UnitY, [new(Vector3.Zero, Vector3.UnitZ, 0, 0, localPosition: wall.Position - s.ObservedPhysics.Position, tombstone: wall.Id)]);
        host.Step(default, MovingContact);
        Assert.That(host.World.GetVehicle(1).ObservedPhysics.LinearVelocity.Z, Is.EqualTo(expected).Within(0.0001f), "Co-moving contact does not stop the car again");
        Assert.That(host.Items.Tombstones.Single().LinearVelocity, Is.EqualTo(pushed.LinearVelocity));
    }

    [Test]
    public void WallPushCannotOverrideAnUnrelatedBlockingContact()
    {
        var host = Start(); var wall = Deploy(host);
        VehicleObservation Contact(VehicleSnapshot s) => new(new(s.ObservedPhysics.Position, Quaternion.Identity, Vector3.Zero, Vector3.Zero), Vector3.UnitY,
            [new(new(0, 0, -18), Vector3.UnitZ, 0, 0, tombstone: wall.Id), new(new(0, 0, -18), Vector3.UnitZ, 0, 0)]);
        host.Step(default, Contact);
        Assert.That(host.World.GetVehicle(1).ObservedPhysics.LinearVelocity, Is.EqualTo(Vector3.Zero));
    }

    [Test]
    public void DeploymentCapturesValidatedPhysicalTuningAndRetuningCannotResizeExistingWalls()
    {
        var host = Start();
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["items.tombstone_width"] = 8, ["items.tombstone_mass"] = 400 }, out _), Is.True);
        var wall = Deploy(host);
        Assert.That(wall.WallSize.X, Is.EqualTo(8)); Assert.That(wall.WallMass, Is.EqualTo(400));
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["items.tombstone_width"] = 6 }, out _), Is.True);
        Assert.That(host.Items.Tombstones.Single(), Is.EqualTo(wall));
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["items.tombstone_depth"] = 0 }, out _), Is.False);
        Assert.Throws<ArgumentException>(() => (wall with { LinearVelocity = new(float.NaN, 0, 0) }).Validate());
        Assert.Throws<ArgumentException>(() => (wall with { WallSize = Vector3.Zero }).Validate());
    }

    [Test]
    public void SimultaneousOverlappingDeploymentRetainsSecondShieldInsteadOfCreatingIntersectingBodies()
    {
        var host = Start(); host.Join(42);
        host.Items.Grant(host.World, 1, HeldItem.Tombstone); host.Items.Grant(host.World, 2, HeldItem.Tombstone);
        foreach (var slot in host.Items.Slots) { host.Items.RequestUse(host.World, slot.Vehicle, slot.Life, slot.Active.Token); }
        host.Step(default, Observe, placeTombstone: (_, _, _) => Placement);
        Assert.That(host.Items.Tombstones.Count(s => !s.Attached), Is.EqualTo(1));
        Assert.That(host.Items.Tombstones.Single(s => s.Owner == 2).Stage, Is.EqualTo(TombstoneStage.RearShield));
        Assert.That(host.Items.Slots.Single(s => s.Vehicle == 2).Item, Is.EqualTo(HeldItem.Tombstone));
    }

    [TestCase(-2f, false)]
    [TestCase(2f, false)]
    [TestCase(2f, true)]
    public void OffCentreVehicleContactSpinsWallWithoutPrematureTipping(float offset, bool duplicate)
    {
        var host = Start(); var wall = Deploy(host);
        Strike(host, wall, offset, 12, duplicate);
        var pushed = host.Items.Tombstones.Single();
        Assert.That(pushed.AngularVelocity.Y * offset, Is.GreaterThan(0.5f));
        Assert.That(pushed.AngularVelocity.X, Is.Zero);
        Assert.That(pushed.Tipping, Is.False);
        Assert.That(pushed.HP, Is.LessThan(wall.HP));
        if (duplicate)
        {
            var single = Start(); var singleWall = Deploy(single); Strike(single, singleWall, offset, 12, false);
            Assert.That(pushed.AngularVelocity, Is.EqualTo(single.Items.Tombstones.Single().AngularVelocity));
            Assert.That(pushed.LinearVelocity, Is.EqualTo(single.Items.Tombstones.Single().LinearVelocity));
        }
    }

    [Test]
    public void HardImpactReleasesTippingAndOnlySideGroundContactBreaksTheWall()
    {
        var host = Start(); var wall = Deploy(host); Strike(host, wall, 0, 35, false);
        var tipped = host.Items.Tombstones.Single();
        Assert.That(tipped.Tipping, Is.True);
        Assert.That(Math.Abs(tipped.AngularVelocity.X), Is.GreaterThan(1));
        var sideways = new VehiclePhysicsState(wall.Position, Quaternion.CreateFromAxisAngle(Vector3.UnitX, 1.5f), Vector3.Zero, Vector3.Zero);
        host.Step(default, Observe, observeTombstone: _ => new(sideways));
        Assert.That(host.Items.Tombstones, Has.Count.EqualTo(1), "Airborne tilt alone cannot break it");
        var slope = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.3f);
        host.Step(default, Observe, observeTombstone: _ => new(new(wall.Position, slope, Vector3.Zero, Vector3.Zero), Vector3.Transform(Vector3.UnitY, slope)));
        Assert.That(host.Items.Tombstones, Has.Count.EqualTo(1), "Normal slope support is not a side impact");
        host.Step(default, Observe, observeTombstone: _ => new(sideways, Vector3.UnitY));
        Assert.That(host.Items.Tombstones, Is.Empty);
        Assert.That(host.Items.DamageTombstone(host.World, wall.Id, 100, 1000, Hit), Is.Null, "Terminal break cannot repeat");
    }

    [Test]
    public void RecoveryPreservesTippingAndOriginalExpiryDespiteRetuningOrNewInventory()
    {
        var host = Start();
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["items.tombstone_lifetime"] = 1 }, out _), Is.True);
        var wall = Deploy(host); Strike(host, wall, 0, 35, false);
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.Wrench), Is.True);
        host.TryConfigure(0, new Dictionary<string, double> { ["items.tombstone_lifetime"] = 120 }, out _);
        var publication = ItemCodec.DecodeState(ItemCodec.EncodeState(new(1, host.Snapshot(), host.Items.Slots, [], [], tombstones: host.Items.Tombstones)));
        var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(publication, host.World.State.Match!, null, host.Configuration)));
        var restored = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 1);
        Assert.That(restored.Items.Tombstones.Single().ExpiresAtTick, Is.EqualTo(wall.ExpiresAtTick));
        Assert.That(restored.Items.Tombstones.Single().Tipping, Is.True);
        foreach (var session in new[] { host, restored })
        {
            while (session.World.State.Tick + 1 < wall.ExpiresAtTick) { session.Step(default, Observe); }
            Assert.That(session.Items.Tombstones, Has.Count.EqualTo(1));
            session.Step(default, Observe);
            Assert.That(session.Items.Tombstones, Is.Empty);
            Assert.That(session.Items.Slots.Single().Active.Item, Is.EqualTo(HeldItem.Wrench));
            session.Step(default, Observe);
            Assert.That(session.Items.Tombstones, Is.Empty);
        }
    }

    private static void Strike(HostVehicleSession host, TombstoneState wall, float offset, float speed, bool duplicate)
    {
        var pose = new VehiclePhysicsState(wall.Position + new Vector3(0, -0.75f, 8), Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        var contact = new VehicleContact(new(0, 0, -speed), Vector3.UnitZ, 0, 0, localPosition: new(offset, 0, -7.7f), tombstone: wall.Id);
        host.Step(default, _ => new(pose, Vector3.UnitY, duplicate ? [contact, contact] : [contact]),
            observeTombstone: _ => new(new(wall.Position, wall.Orientation, Vector3.Zero, Vector3.Zero)));
    }

    private static TombstoneState Deploy(HostVehicleSession host)
    {
        host.Items.Grant(host.World, 1, HeldItem.Tombstone);
        host.Items.RequestUse(host.World, 1, 1, host.Items.Slots.Single().Active.Token);
        var placement = new VehiclePhysicsState(Placement.Position + new Vector3(host.Items.Tombstones.Count(s => !s.Attached) * 10, 0, 0), Placement.Orientation, Placement.LinearVelocity, Placement.AngularVelocity);
        host.Step(default, Observe, placeTombstone: (_, _, _) => placement);
        return host.Items.Tombstones.Last();
    }
    private static HostVehicleSession Start()
    {
        var host = new HostVehicleSession(99, matchConfiguration: new() { CountdownTicks = 1, MinimumPlayers = 1 });
        host.Step(default, Observe); host.Step(default, Observe); return host;
    }
    private static VehicleObservation Observe(VehicleSnapshot state) => new(state.Movement.Physics, Vector3.UnitY);
}
