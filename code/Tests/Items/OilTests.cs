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
    [TestCase(1ul)]
    [TestCase(2ul)]
    public void RepeatedContactsNeverConsumeHazardAndContinuousOverlapDoesNotRefresh(ulong vehicle)
    {
        var host = Create(); Deploy(host); var patch = host.Items.Patches.Single();
        Step(host, vehicle);
        for (int i = 0; i < 430; i++) { Step(host, vehicle); }
        Assert.That(host.World.GetVehicle(vehicle).Movement.OilTicks, Is.Zero);
        for (int i = 0; i < 8; i++)
        {
            Step(host); Assert.That(host.Items.OilContacts, Is.Empty); Step(host, vehicle);
            Assert.That(host.Items.Patches.Single(), Is.EqualTo(patch));
            Assert.That(host.World.GetVehicle(vehicle).Movement.OilTicks, Is.EqualTo(120));
        }
        Assert.That(host.World.Events.Entries.Count(e => e.Kind == "Oil triggered"), Is.EqualTo(9));
        while (host.World.State.Tick < patch.ExpiresAtTick - 1) { Step(host, vehicle); }
        Assert.That(host.Items.Patches.Single(), Is.EqualTo(patch)); Step(host, vehicle);
        Assert.That(host.Items.Patches, Is.Empty); Assert.That(host.Items.OilContacts, Is.Empty);
    }

    [Test]
    public void AllEightVehiclesCanContactSameHazardInOneStep()
    {
        var host = Create(); for (ulong id = 4; id <= 8; id++) { host.JoinPlayer(id * 10, id); }
        Deploy(host); Step(host, 1, 2, 3, 4, 5, 6, 7, 8);
        Assert.That(host.Items.Patches.Count, Is.EqualTo(1));
        Assert.That(host.Items.OilContacts.Count, Is.EqualTo(8));
        var publication = new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], [], patches: host.Items.Patches, oilContacts: host.Items.OilContacts);
        Assert.That(ItemCodec.DecodeState(ItemCodec.EncodeState(publication)).OilContacts, Is.EqualTo(publication.OilContacts));
        Assert.That(host.World.State.Vehicles.All(v => v.Movement.OilTicks == 120), Is.True);
    }

    [TestCase(1ul, false)]
    [TestCase(2ul, false)]
    [TestCase(2ul, true)]
    public void RecoveryRetainsDeadlineAndInsideLatchUntilExitOrNewLife(ulong vehicle, bool newLife)
    {
        var host = Create(); Deploy(host); Step(host, vehicle);
        var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(
            new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], [], patches: host.Items.Patches, oilContacts: host.Items.OilContacts),
            host.World.State.Match!, null, host.Configuration)));
        var restored = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 2);
        Assert.That(restored.Items.Patches, Is.EqualTo(host.Items.Patches));
        Assert.That(restored.Items.OilContacts, Is.EqualTo(host.Items.OilContacts));
        Step(restored, vehicle);
        Assert.That(restored.World.GetVehicle(vehicle).Movement.OilTicks, Is.EqualTo(119));
        if (newLife)
        {
            var input = new InputFrame(restored.World.State.Tick + 1, 0, 0, 0, 0, 0, 0);
            restored.Items.Step(restored.World, input, restored.World.State.Vehicles.Select(v =>
                new VehicleStepRequest(v.VehicleId, input, Observe(v, false), reset: v.VehicleId == vehicle ? Observe(v, false).Physics : null)).ToArray(), (_, _) => null);
        }
        else { Step(restored); }
        Step(restored, vehicle);
        Assert.That(restored.Items.Patches, Is.EqualTo(host.Items.Patches));
        Assert.That(restored.World.GetVehicle(vehicle).Movement.OilTicks, Is.EqualTo(120));
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
        OilContact[] contacts = [];
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
    public void CodecAcceptsMultipleVehiclesAndRejectsDuplicateOrExpiredState()
    {
        var host = Create();
        Deploy(host);
        var patch = host.Items.Patches.Single();
        ItemPublication State(params OilContact[] contacts) => new(1, host.Snapshot(), [], [], [], patches: [patch], oilContacts: contacts);
        Assert.DoesNotThrow(() => State(new OilContact(patch.Id, 1, 1)));
        Assert.Throws<ArgumentException>(() => State(new OilContact(patch.Id, 2, 1), new(patch.Id, 2, 2)));
        Assert.DoesNotThrow(() => State(new OilContact(patch.Id, 2, 1), new(patch.Id, 3, 1)));
        Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), [], [], [], patches: [patch with { ExpiresAtTick = host.World.State.Tick }]));
        var decoded = ItemCodec.DecodeState(ItemCodec.EncodeState(State(new OilContact(patch.Id, 2, 1))));
        Assert.That(decoded.Patches.Single(), Is.EqualTo(patch));
        Assert.That(decoded.OilContacts.Single().Vehicle, Is.EqualTo(2));
    }

    [TestCase("vehicle.oil_grip_reduction", -0.1)]
    [TestCase("vehicle.oil_grip_reduction", 0.96)]
    [TestCase("vehicle.oil_rear_drive_reduction", 1)]
    [TestCase("vehicle.oil_rear_lateral_reduction", double.NaN)]
    [TestCase("vehicle.oil_impairment_seconds", 0)]
    [TestCase("vehicle.oil_impairment_seconds", 21)]
    [TestCase("vehicle.oil_recovery_curve", 0.4)]
    [TestCase("vehicle.oil_recovery_curve", 4)]
    [TestCase("vehicle.oil_recovery_seconds", 0)]
    [TestCase("vehicle.oil_recovery_seconds", 11)]
    [TestCase("items.oil_enemy_contacts", 2)]
    [TestCase("items.oil_lifetime_seconds", 0)]
    [TestCase("items.oil_lifetime_seconds", double.NaN)]
    public void UnsafeTuningIsRejected(string key, double value) =>
        Assert.That(GameplayOptions.TryApply(new(), new Dictionary<string, double> { [key] = value }, out _, out _), Is.False);

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
    private static VehicleObservation Observe(VehicleSnapshot v, bool inside) => new(new(new(inside ? 0 : 6, 0.9f, 0), Quaternion.Identity, Vector3.Zero, Vector3.Zero), Vector3.UnitY, wheels: new WheelSupport(new Vector4(new VehicleConfiguration().SuspensionLength - 0.9f)));
}
