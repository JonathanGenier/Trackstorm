using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class OilTests
{
    [Test]
    public void OwnerAndRepeatedEnemyContactPreserveBudgetUntilSecondDistinctEnemy()
    {
        var host = Create();
        Deploy(host);
        Step(host, 1);
        Assert.That(host.Items.OilContacts, Is.Empty);
        Assert.That(host.World.GetVehicle(1).Movement.OilTicks, Is.EqualTo(105));
        Step(host, 2);
        for (int i = 0; i < 130; i++) { Step(host, 2); }
        Assert.That(host.Items.OilContacts.Single().Vehicle, Is.EqualTo(2));
        Assert.That(host.World.GetVehicle(2).Movement.OilTicks, Is.EqualTo(105));
        Step(host);
        Step(host, 2);
        Assert.That(host.Items.Patches.Count, Is.EqualTo(1));
        Assert.That(host.World.Events.Entries.Count(e => e.Kind == "Oil triggered"), Is.EqualTo(1));
        Step(host, 3);
        Assert.That(host.Items.Patches, Is.Empty);
        Assert.That(host.Items.OilContacts, Is.Empty);
        Assert.That(host.World.GetVehicle(3).Movement.OilTicks, Is.EqualTo(105));
    }

    [Test]
    public void RecoveryRetainsLifetimeOwnerAndDistinctHistoryAcrossExitAndNewLife()
    {
        var host = Create();
        Deploy(host);
        Step(host, 2);
        Step(host);
        var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(
            new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], [], patches: host.Items.Patches, oilContacts: host.Items.OilContacts),
            host.World.State.Match!, null, host.Configuration)));
        var restored = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 2);
        Assert.That(restored.Items.Patches, Is.EqualTo(host.Items.Patches));
        Assert.That(restored.Items.OilContacts, Is.EqualTo(host.Items.OilContacts));
        var input = new InputFrame(restored.World.State.Tick + 1, 0, 0, 0, 0, 0, 0);
        restored.Items.Step(restored.World, input, restored.World.State.Vehicles.Select(v =>
            new VehicleStepRequest(v.VehicleId, input, Observe(v, false), reset: v.VehicleId == 2 ? Observe(v, false).Physics : null)).ToArray(), (_, _) => null);
        Step(restored, 2);
        Assert.That(restored.Items.OilContacts.Single().Life, Is.EqualTo(1));
        Assert.That(restored.World.GetVehicle(2).LifeId, Is.EqualTo(2));
        Assert.That(restored.Items.Patches.Count, Is.EqualTo(1));
        Step(restored, 3);
        Assert.That(restored.Items.Patches, Is.Empty);
    }

    [Test]
    public void MultipleDeploymentsDoNotBrickHeldOilAndLifetimeCleansUp()
    {
        var host = Create();
        for (int i = 0; i < 64; i++) { Deploy(host); }
        Assert.That(host.Items.Patches.Count, Is.EqualTo(64));
        Assert.That(ItemCodec.DecodeState(ItemCodec.EncodeState(new(1, host.Snapshot(), host.Items.Slots, [], [], patches: host.Items.Patches))).Patches.Count, Is.EqualTo(64));
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.None));
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["items.oil_lifetime_seconds"] = 1 }, out _), Is.True);
        Deploy(host);
        ulong id = host.Items.Patches.Last().Id;
        for (int i = 0; i < 59; i++) { Step(host); }
        Assert.That(host.Items.Patches.Any(p => p.Id == id), Is.True);
        Step(host);
        Assert.That(host.Items.Patches.Any(p => p.Id == id), Is.False);
        Assert.That(host.Items.Patches.Count, Is.EqualTo(64));
    }

    [Test]
    public void OrderedItemUpdatesReuseOnlyExactUnchangedOilAndCheckpointsRemainComplete()
    {
        var host = Create();
        var patches = Enumerable.Range(1, 1500).Select(id => new OilPatch((ulong)id, 1, new(100 + id * 7, 0, 0), Vector3.UnitY, 3)).ToArray();
        var first = new ItemPublication(1, host.Snapshot(), [], [], [], patches: patches);
        var next = new ItemPublication(2, host.Snapshot(), [], [], [], patches: patches);
        byte[] reference = ItemCodec.EncodeState(next, first);
        Assert.That(reference.Length, Is.LessThan(16384));
        Assert.That(ItemCodec.DecodeState(reference, first).Patches, Is.EqualTo(patches));
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeState(reference));
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeState(reference, next));
        Assert.That(ItemCodec.DecodeState(ItemCodec.EncodeState(next)).Patches, Is.EqualTo(patches), "A standalone boundary never requires prior state.");
        var changed = new ItemPublication(3, host.Snapshot(), [], [], [], patches: patches, oilContacts: [new(1, 2, 1)]);
        var decoded = ItemCodec.DecodeState(ItemCodec.EncodeState(changed, next));
        Assert.That(decoded.OilContacts, Is.EqualTo(changed.OilContacts), "First enemy contact replaces the full Oil baseline.");
        var removed = new ItemPublication(4, host.Snapshot(), [], [], [], patches: patches.Skip(1));
        Assert.That(ItemCodec.DecodeState(ItemCodec.EncodeState(removed, changed)).Patches.Count, Is.EqualTo(1499));
    }

    [Test]
    public void LargeOilBoundaryRoundTripsResumeWithoutTruncationOrLifetimeRefresh()
    {
        var host = Create();
        var patches = Enumerable.Range(1, 3000).Select(id => new OilPatch((ulong)id, 1, new(100 + id * 7, 0, 0), Vector3.UnitY, 3)).ToArray();
        var contacts = patches.Select(patch => new OilContact(patch.Id, 2, 1)).ToArray();
        var publication = new ItemPublication(1, host.Snapshot(), [], [], [], patches: patches, oilContacts: contacts);
        host.Items.Restore(publication, 1, 3000);
        byte[] bytes = ResumeCheckpointCodec.Encode(new(publication, host.World.State.Match!, null, host.Configuration));
        Assert.That(bytes.Length, Is.GreaterThan(65536));
        var restored = HostVehicleSession.Restore(ResumeCheckpointCodec.Decode(bytes), host.CaptureAuthority(), 2);
        Step(restored);
        Assert.That(restored.Items.Patches, Is.EqualTo(patches));
        Assert.That(restored.Items.OilContacts, Is.EqualTo(contacts));
        Assert.That(restored.Items.Revision, Is.EqualTo(1), "Elapsed time alone does not republish the complete Oil set.");
    }

    [Test]
    public void FailedPlacementAndRejectedBatchKeepInventoryAndAuthoritativeState()
    {
        var host = Create();
        Grant(host);
        Step(host);
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.Oil));
        var slot = host.Items.Slots.Single();
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.True);
        ulong tick = host.World.State.Tick;
        var input = new InputFrame(tick + 1, 0, 0, 0, 0, 0, 0);
        Assert.Throws<ArgumentException>(() => host.Items.Step(host.World, input,
            host.World.State.Vehicles.Select(v => new VehicleStepRequest(v.VehicleId, default, Observe(v, false))).ToArray(), (_, _) => null, Place));
        Assert.That(host.Items.Patches, Is.Empty);
        Assert.That(host.Items.Slots.Single(), Is.EqualTo(slot));
        Assert.That(host.World.State.Tick, Is.EqualTo(tick));
        host.Step(default, v => Observe(v, false), placeOil: Place);
        Assert.That(host.Items.Patches.Count, Is.EqualTo(1));
    }

    [Test]
    public void CodecRejectsDuplicateEnemyOwnerContactAndSpentPatch()
    {
        var host = Create();
        Deploy(host);
        var patch = host.Items.Patches.Single();
        ItemPublication State(params OilContact[] contacts) => new(1, host.Snapshot(), [], [], [], patches: [patch], oilContacts: contacts);
        Assert.Throws<ArgumentException>(() => State(new OilContact(patch.Id, 1, 1)));
        Assert.Throws<ArgumentException>(() => State(new OilContact(patch.Id, 2, 1), new(patch.Id, 2, 2)));
        Assert.Throws<ArgumentException>(() => State(new OilContact(patch.Id, 2, 1), new(patch.Id, 3, 1)));
        var decoded = ItemCodec.DecodeState(ItemCodec.EncodeState(State(new OilContact(patch.Id, 2, 1))));
        Assert.That(decoded.Patches.Single(), Is.EqualTo(patch));
        Assert.That(decoded.OilContacts.Single().Vehicle, Is.EqualTo(2));
    }

    [Test]
    public void OilHasNoEntryYawAndRecoversProgressivelyWhilePreservingSteeringAndDrive()
    {
        var pose = new VehiclePhysicsState(new(0, 0.9f, 0), Quaternion.Identity, new(5, 0, -15), Vector3.Zero);
        var dry = new VehicleMovement(new(), pose);
        var oil = new VehicleMovement(new(), pose);
        var input = new InputFrame(1, 16000, 65535, 0, 0, 0, 0);
        var baseline = dry.Step(input, pose, Vector3.UnitY);
        var slippery = oil.Step(input, pose, Vector3.UnitY, oilContact: true);
        Assert.That(slippery.SteeringAngle, Is.EqualTo(baseline.SteeringAngle).And.Not.Zero);
        Assert.That(slippery.LateralAcceleration, Is.EqualTo(baseline.LateralAcceleration * 0.5f).Within(0.001));
        Assert.That(slippery.LongitudinalAcceleration, Is.EqualTo(baseline.LongitudinalAcceleration));
        Assert.That(Math.Abs(slippery.Physics.AngularVelocity.Y), Is.LessThan(0.3));
        var replay = new VehicleMovement(new(), pose);
        replay.Restore(VehicleStateCodec.Decode(VehicleStateCodec.Encode(slippery)));
        for (ulong tick = 2; tick <= 106; tick++)
        {
            input = new InputFrame(tick, 16000, 65535, 0, 0, 0, 0);
            var before = oil.State;
            var next = oil.Step(input, pose, Vector3.UnitY);
            Assert.That(replay.Step(input, pose, Vector3.UnitY), Is.EqualTo(next));
            Assert.That(next.OilTicks, Is.EqualTo(Math.Max(0, before.OilTicks - 1)));
        }
        Assert.That(oil.State.OilTicks, Is.Zero);
        var straight = new VehicleMovement(new(), new(Vector3.Zero, Quaternion.Identity, new(0, 0, -15), Vector3.Zero));
        Assert.That(straight.Step(new(1, 0, 0, 0, 0, 0, 0), straight.State.Physics, Vector3.UnitY, oilContact: true).Physics.AngularVelocity.Y, Is.Zero);
    }

    [TestCase("vehicle.oil_grip_reduction", -0.1)]
    [TestCase("vehicle.oil_grip_reduction", 0.9)]
    [TestCase("vehicle.oil_recovery_seconds", 0)]
    [TestCase("vehicle.oil_recovery_seconds", 11)]
    [TestCase("items.oil_enemy_contacts", 1.5)]
    [TestCase("items.oil_enemy_contacts", 0)]
    [TestCase("items.oil_lifetime_seconds", 0)]
    [TestCase("items.oil_lifetime_seconds", double.NaN)]
    public void UnsafeTuningIsRejected(string key, double value) =>
        Assert.That(GameplayOptions.TryApply(new(), new Dictionary<string, double> { [key] = value }, out _, out _), Is.False);

    [Test]
    public void BankedContactRejectsFlightAndOtherRoadLevels()
    {
        var normal = Vector3.Normalize(new Vector3(0.4f, 1, 0));
        var patch = new OilPatch(1, 1, new(1, 2, 3), normal, 3);
        VehicleObservation At(Vector3 offset, Vector3 support) => new(new(patch.Position + offset, Quaternion.Identity, Vector3.Zero, Vector3.Zero), support);
        Assert.That(patch.Contains(At(normal * 0.9f, normal)), Is.True);
        Assert.That(patch.Contains(At(normal * 0.9f, Vector3.Zero)), Is.False);
        Assert.That(patch.Contains(At(normal * 4, normal)), Is.False);
        Assert.That(patch.Contains(At(-normal, normal)), Is.False);
    }

    private static HostVehicleSession Create()
    {
        var host = new HostVehicleSession(99);
        host.JoinPlayer(10, 2);
        host.JoinPlayer(20, 3);
        return host;
    }
    private static void Grant(HostVehicleSession host)
    {
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.Oil), Is.True);
        var slot = host.Items.Slots.Single();
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.True);
    }
    private static void Deploy(HostVehicleSession host) { Grant(host); host.Step(default, v => Observe(v, false), placeOil: Place); }
    private static OilPatch Place(ItemSlot slot, VehiclePhysicsState pose) => new(slot.Token, slot.Vehicle, Vector3.Zero, Vector3.UnitY, 3);
    private static void Step(HostVehicleSession host, params ulong[] inside) => host.Step(default, v => Observe(v, inside.Contains(v.VehicleId)));
    private static VehicleObservation Observe(VehicleSnapshot v, bool inside) => new(new(new(inside ? 0 : 6, 0.9f, 0), Quaternion.Identity, Vector3.Zero, Vector3.Zero), Vector3.UnitY);
}
