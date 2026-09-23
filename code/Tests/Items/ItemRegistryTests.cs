using System.Numerics;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

/// <summary>Four-item identity, distribution and recovery through the existing authoritative owners.</summary>
[TestFixture]
internal sealed class ItemRegistryTests
{
    [TestCase(HeldItem.Oil)]
    [TestCase(HeldItem.Nitro)]
    public void UnimplementedUseRetainsExactSlotAcrossResumeAndReset(HeldItem item)
    {
        var host = new HostVehicleSession(99);
        host.JoinPlayer(10, 2);
        Assert.That(host.Items.Grant(host.World, 2, item), Is.True);
        var slot = host.Items.Slots.Single();
        for (int i = 0; i < 4; i++)
        {
            Assert.That(host.UseItem(10, 99, slot.Life, slot.Token), Is.False);
            host.Step(default, Observe);
        }

        Assert.That(host.Items.Slots.Single(), Is.EqualTo(slot));
        Assert.That(host.Items.Events, Is.Empty);
        Assert.That(host.Items.Missiles, Is.Empty);
        host.Suspend(10);
        var resume = new ResumeCheckpoint(new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], []), host.World.State.Match!, null, host.Configuration);
        var decoded = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(resume));
        Assert.That(decoded.Items.Slots.Single(), Is.EqualTo(slot));
        Assert.That(host.ResumePlayer(20, 2), Is.True);
        Assert.That(host.Items.Grant(host.World, 2, HeldItem.Wrench), Is.False);
        Assert.That(host.UseItem(10, 99, slot.Life, slot.Token), Is.False);
        var input = new Trackstorm.Core.Input.InputFrame(host.World.State.Tick + 1, 0, 0, 0, 0, 0, 0);
        host.Items.Step(host.World, input, host.World.State.Vehicles.Select(state =>
            new VehicleStepRequest(state.VehicleId, input, Observe(state), reset: state.Movement.Physics)).ToArray(), (_, _) => null);
        Assert.That(host.Items.Slots, Is.Empty);
    }

    [TestCase(HeldItem.Wrench)]
    [TestCase(HeldItem.Missile)]
    [TestCase(HeldItem.Oil)]
    [TestCase(HeldItem.Nitro)]
    public void SingleWeightedItemClaimsAndRoundTripsWithoutSecondAuthority(HeldItem item)
    {
        var tuning = new ItemSpawnConfiguration();
        foreach (var definition in ItemRegistry.All)
        {
            tuning = tuning with { Weights = tuning.Weights.SetItem(definition.Identity, definition.Identity == item ? 1 : 0) };
        }

        var host = new HostVehicleSession(99);
        host.RegisterSpawns(PrototypeArena.Configuration, tuning);
        var state = host.World.GetVehicle(1);
        var pose = new VehiclePhysicsState(PrototypeArena.Configuration.Items[0].Position, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        host.World.Restore(new(host.World.State.Tick, default, [new VehicleSnapshot(1, state.LifeId, new VehicleState(host.World.State.Tick, pose, false, false, 0, 0), state.Damage, pose)], host.World.State.Match));
        Assert.That(host.Spawns!.TryPickup(host.World, PrototypeArena.Configuration.Items[0].Id, 1), Is.True);
        ulong random = host.Spawns.RandomState;
        Assert.That(host.Spawns.TryPickup(host.World, PrototypeArena.Configuration.Items[0].Id, 1), Is.False);
        Assert.That(host.Spawns.RandomState, Is.EqualTo(random));
        var publication = ItemCodec.DecodeState(ItemCodec.EncodeState(new(1, host.Snapshot(), host.Items.Slots, [], [], host.Spawns.States)));
        Assert.That(publication.Slots.Single().Item, Is.EqualTo(item));
        Assert.That(publication.Spawns[0].Item, Is.EqualTo(item));
        Assert.That(publication.Spawns[0].Token, Is.EqualTo(publication.Slots.Single().Token));
    }

    private static VehicleObservation Observe(VehicleSnapshot state) => new(state.Movement.Physics, Vector3.UnitY);
}
