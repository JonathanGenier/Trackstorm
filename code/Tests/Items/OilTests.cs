using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

/// <summary>Persistent hazards use the existing item transaction and complete recovery boundary.</summary>
[TestFixture]
internal sealed class OilTests
{
    [Test]
    public void FullHazardAndContactBoundsRoundTripAndRejectMalformedContinuation()
    {
        var host = new HostVehicleSession(99);
        for (ulong id = 2; id <= 8; id++) { host.JoinPlayer(id * 10, id); }
        var patches = Enumerable.Range(1, ItemAuthority.MaximumPatches).Select(id => new OilPatch((ulong)id, 1, Vector3.Zero, Vector3.UnitY, 3)).ToArray();
        var contacts = patches.SelectMany(patch => host.World.State.Vehicles.Select(v => new OilContact(patch.Id, v.VehicleId, v.LifeId))).ToArray();
        var state = new ItemPublication(1, host.Snapshot(), [], [], [], patches: patches, oilContacts: contacts);
        byte[] bytes = ItemCodec.EncodeState(state);
        var decoded = ItemCodec.DecodeState(bytes);
        Assert.That(decoded.Patches, Is.EqualTo(patches));
        Assert.That(decoded.OilContacts, Is.EqualTo(contacts));
        Assert.That(bytes.Length, Is.LessThan(32768));
        Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), [], [], [], patches: patches, oilContacts: contacts.Append(contacts[0])));
        Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), [], [], [], patches: patches, oilContacts: [new(1, 1, 99)]));
        Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), [], [], [], patches: [patches[0] with { Normal = new(float.NaN, 0, 0) }]));
        Assert.Throws<ArgumentException>(() => new ItemAuthority().Restore(state, 1, 31));
        bytes[2] = 3;
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeState(bytes));
    }

    [Test]
    public void FailedPlacementAndCapRetainSlotAndDuplicateRequestsCannotDeploy()
    {
        var host = new HostVehicleSession(99, new ItemConfiguration { MaximumOilPatches = 1 });
        Grant(host);
        var slot = host.Items.Slots.Single();
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.False);
        host.Step(default, Outside);
        Assert.That(host.Items.Slots.Single(), Is.EqualTo(slot));
        Assert.That(host.Items.Patches, Is.Empty);
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.True);
        host.Step(default, Outside, placeOil: Place);
        Assert.That(host.Items.Patches.Count, Is.EqualTo(1));
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.False);
        Grant(host);
        host.Step(default, Outside, placeOil: Place);
        Assert.That(host.Items.Patches.Count, Is.EqualTo(1));
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.Oil));
    }

    [Test]
    public void OwnerAndPeerTriggerOncePerEntryAndRecoverWithoutDuplicateImpulse()
    {
        var host = new HostVehicleSession(99);
        host.JoinPlayer(10, 2);
        Grant(host);
        host.Step(default, Outside, placeOil: Place);
        var patch = host.Items.Patches.Single();
        VehicleObservation Inside(VehicleSnapshot state) => At(state, patch.Position + patch.Normal * 0.9f, patch.Normal);
        host.Step(default, Inside);
        Assert.That(host.Items.OilContacts.Count, Is.EqualTo(2));
        Assert.That(host.World.State.Vehicles.All(v => v.Movement.OilTicks == 120), Is.True);
        Assert.That(host.World.Events.Entries.Count(e => e.Kind == "Oil triggered"), Is.EqualTo(2));
        var publication = new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], [], patches: host.Items.Patches, oilContacts: host.Items.OilContacts);
        var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(publication, host.World.State.Match!, null, host.Configuration)));
        var restored = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 2);
        Assert.That(restored.Items.Patches, Is.EqualTo(host.Items.Patches));
        Assert.That(restored.Items.OilContacts, Is.EqualTo(host.Items.OilContacts));
        for (int i = 0; i < 125; i++) { restored.Step(default, Inside); }
        Assert.That(restored.World.State.Vehicles.All(v => v.Movement.OilTicks == 0), Is.True);
        Assert.That(restored.World.Events.Entries.Count(e => e.Kind == "Oil triggered"), Is.Zero);
        restored.Step(default, Outside);
        restored.Step(default, Inside);
        Assert.That(restored.World.Events.Entries.Count(e => e.Kind == "Oil triggered"), Is.EqualTo(2));
        Assert.That(restored.Items.Patches.Count, Is.EqualTo(1));
        Assert.That(host.PrepareJoin(3, 2)!.Items.Patches, Is.EqualTo(host.Items.Patches));
    }

    [Test]
    public void BankedContactRejectsFlightAndOtherRoadLevels()
    {
        var normal = Vector3.Normalize(new Vector3(0.4f, 1, 0));
        var patch = new OilPatch(1, 1, new Vector3(1, 2, 3), normal, 3);
        patch.Validate();
        VehicleObservation Observe(Vector3 offset, Vector3 support) => new(new VehiclePhysicsState(patch.Position + offset, Quaternion.Identity, Vector3.Zero, Vector3.Zero), support);
        Assert.That(patch.Contains(Observe(normal * 0.9f, normal)), Is.True);
        Assert.That(patch.Contains(Observe(normal * 0.9f, Vector3.Zero)), Is.False);
        Assert.That(patch.Contains(Observe(normal * 4, normal)), Is.False);
        Assert.That(patch.Contains(Observe(-normal, normal)), Is.False);
        Assert.That(patch.Contains(Observe(normal * 0.9f + Vector3.UnitZ * 4, normal)), Is.False);
    }

    [Test]
    public void RejectedWorldBatchDoesNotConsumeOrCreateGhostPatch()
    {
        var host = new HostVehicleSession(99);
        Grant(host);
        var state = host.World.GetVehicle(1);
        var input = new InputFrame(1, 0, 0, 0, 0, 0, 0);
        Assert.Throws<ArgumentException>(() => host.Items.Step(host.World, input,
            [new VehicleStepRequest(1, default, Outside(state))], (_, _) => null, Place));
        Assert.That(host.Items.Patches, Is.Empty);
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.Oil));
        Assert.That(host.World.State.Tick, Is.Zero);
        host.Step(default, Outside, placeOil: Place);
        Assert.That(host.Items.Patches.Count, Is.EqualTo(1));
    }

    [Test]
    public void PatchSurvivesDeployerRemovalAndNewLifeCanTrigger()
    {
        var host = new HostVehicleSession(99);
        host.JoinPlayer(10, 2);
        Assert.That(host.Items.Grant(host.World, 2, HeldItem.Oil), Is.True);
        var slot = host.Items.Slots.Single();
        Assert.That(host.UseItem(10, 99, slot.Life, slot.Token), Is.True);
        host.Step(default, Outside, placeOil: Place);
        var patch = host.Items.Patches.Single();
        host.Suspend(10);
        host.ExpirePlayer(2);
        Assert.That(host.Items.Patches.Single(), Is.EqualTo(patch));
        VehicleObservation Inside(VehicleSnapshot state) => At(state, patch.Position + Vector3.UnitY * 0.9f, Vector3.UnitY);
        host.Step(default, Inside);
        var input = new InputFrame(host.World.State.Tick + 1, 0, 0, 0, 0, 0, 0);
        host.Items.Step(host.World, input, [new VehicleStepRequest(1, input, Inside(host.World.GetVehicle(1)), reset: Inside(host.World.GetVehicle(1)).Physics)], (_, _) => null);
        Assert.That(host.Items.Patches.Single(), Is.EqualTo(patch));
        Assert.That(host.Items.OilContacts, Is.Empty);
        host.Step(default, Inside);
        Assert.That(host.Items.OilContacts.Single().Life, Is.EqualTo(2));
        Assert.That(host.World.GetVehicle(1).Movement.OilTicks, Is.EqualTo(120));
    }

    [Test]
    public void OilReducesTireBudgetButPreservesSteeringAndReplaysTimer()
    {
        var pose = new VehiclePhysicsState(new Vector3(0, 0.9f, 0), Quaternion.Identity, new Vector3(5, 0, -15), Vector3.Zero);
        var dry = new VehicleMovement(new(), pose);
        var oil = new VehicleMovement(new(), pose);
        var input = new InputFrame(1, 16000, 65535, 0, 0, 0, 0);
        var baseline = dry.Step(input, pose, Vector3.UnitY);
        var slippery = oil.Step(input, pose, Vector3.UnitY, oilSpin: 2.6f);
        Assert.That(slippery.SteeringAngle, Is.EqualTo(baseline.SteeringAngle).And.Not.Zero);
        Assert.That(Math.Abs(slippery.LateralAcceleration), Is.LessThan(Math.Abs(baseline.LateralAcceleration)));
        Assert.That(Math.Abs(slippery.Physics.AngularVelocity.Y), Is.GreaterThan(2));
        var decoded = VehicleStateCodec.Decode(VehicleStateCodec.Encode(slippery));
        dry.Restore(decoded);
        var next = new InputFrame(2, 16000, 65535, 0, 0, 0, 0);
        Assert.That(dry.Step(next, slippery.Physics, Vector3.UnitY), Is.EqualTo(oil.Step(next, slippery.Physics, Vector3.UnitY)));
    }

    private static void Grant(HostVehicleSession host)
    {
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.Oil), Is.True);
        var slot = host.Items.Slots.Single(s => s.Vehicle == 1);
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.True);
    }

    private static OilPatch Place(ItemSlot slot, VehiclePhysicsState pose) => new(slot.Token, slot.Vehicle, pose.Position + Vector3.UnitZ * 4 - Vector3.UnitY * 0.9f, Vector3.UnitY, 3);
    private static VehicleObservation Outside(VehicleSnapshot state) => At(state, new Vector3(0, 0.9f, 0), Vector3.UnitY);
    private static VehicleObservation At(VehicleSnapshot state, Vector3 position, Vector3 support) => new(new VehiclePhysicsState(position, Quaternion.Identity, state.Movement.Physics.LinearVelocity, state.Movement.Physics.AngularVelocity), support);
}
