using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Transport.Tests;

internal sealed partial class VehicleNetworkDriverTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void TombstoneUseWaitsForPresentationAndCoalescesRepeatedTaps(bool tapOnReady)
    {
        using var wire = ConnectedGateway();
        using var driver = new VehicleNetworkDriver(wire, Session,
            configuration: new() { Match = new() { MinimumPlayers = 1, CountdownTicks = 1 } });
        bool ready = false;
        driver.TombstoneReady = _ => ready;
        int placements = 0;
        driver.PlaceTombstone = (_, _, _) => { placements++; return new(new(0, 2, 10), System.Numerics.Quaternion.Identity, default, default); };
        driver.Advance(default, Observe); driver.Advance(default, Observe);
        var authority = driver.Host!;
        authority.Items.Grant(authority.World, 1, HeldItem.Wrench);
        authority.Items.Grant(authority.World, 1, HeldItem.Tombstone);
        driver.Advance(new(0, 0, 0, 0, 0, InputButtons.SwitchItem | InputButtons.UseItem, 0), Observe);
        for (int i = 0; i < 30; i++) { driver.Advance(new(0, 0, 0, 0, 0, InputButtons.UseItem, 0), Observe); }
        Assert.That(placements, Is.Zero);
        Assert.That(authority.Items.Tombstones.Single().Stage, Is.EqualTo(TombstoneStage.RearShield));
        ready = true;
        driver.Advance(new(0, 0, 0, 0, 0, tapOnReady ? InputButtons.UseItem : 0, 0), Observe);
        for (int i = 0; i < 5; i++) { driver.Advance(default, Observe); }
        Assert.That(placements, Is.EqualTo(1));
        Assert.That(authority.Items.Tombstones.Single().Stage, Is.EqualTo(TombstoneStage.WorldWall));
        Assert.That(driver.LocalItem!.Item, Is.EqualTo(HeldItem.Wrench));
    }

    [TestCase("switch")]
    [TestCase("discard")]
    [TestCase("destroy")]
    [TestCase("death")]
    [TestCase("life")]
    public void QueuedTombstoneUseCannotSurviveCapabilityCancellation(string cancellation)
    {
        using var wire = ConnectedGateway();
        using var driver = new VehicleNetworkDriver(wire, Session,
            configuration: new() { Match = new() { MinimumPlayers = 1, CountdownTicks = 1 } });
        bool ready = false;
        driver.TombstoneReady = _ => ready;
        int placements = 0;
        driver.PlaceTombstone = (_, _, _) => { placements++; return null; };
        driver.Advance(default, Observe); driver.Advance(default, Observe);
        var authority = driver.Host!;
        authority.Items.Grant(authority.World, 1, HeldItem.Tombstone);
        Assert.That(driver.RequestItemUse(), Is.True);
        if (cancellation == "switch") { driver.RequestItemSwitch(); driver.RequestItemSwitch(); }
        else if (cancellation == "discard") { driver.RequestItemDiscard(); authority.Items.Grant(authority.World, 1, HeldItem.Tombstone); }
        else if (cancellation == "destroy") { authority.Items.DamageTombstone(authority.World, authority.Items.Tombstones.Single().Id, 1, 1000, new("world", 0, "test")); authority.Items.Grant(authority.World, 1, HeldItem.Tombstone); }
        else
        {
            var world = authority.World.State;
            authority.World.Restore(new(world.Tick, world.LastInput, world.Vehicles.Select(v => v.VehicleId != 1 ? v :
                new VehicleSnapshot(v.VehicleId, cancellation == "life" ? v.LifeId + 1 : v.LifeId, v.Movement,
                    cancellation == "death" ? new(v.Damage.MaxHP, 0, null, null) : v.Damage, v.ObservedPhysics)), world.Match));
        }
        ready = true;
        for (int i = 0; i < 5; i++) { driver.Advance(default, Observe); }
        Assert.That(placements, Is.Zero);
        if (cancellation is "life" or "death") { Assert.That(authority.Items.Tombstones, Is.Empty); }
        else { Assert.That(authority.Items.Tombstones.Single().Stage, Is.EqualTo(TombstoneStage.RearShield)); }
    }

    [Test]
    public void BlockedQueuedDeploymentRequiresAnotherPressInsteadOfRetryingForever()
    {
        using var wire = ConnectedGateway();
        using var driver = new VehicleNetworkDriver(wire, Session,
            configuration: new() { Match = new() { MinimumPlayers = 1, CountdownTicks = 1 } });
        bool ready = false;
        driver.TombstoneReady = _ => ready;
        int placements = 0;
        driver.PlaceTombstone = (_, _, _) => { placements++; return null; };
        driver.Advance(default, Observe); driver.Advance(default, Observe);
        driver.Host!.Items.Grant(driver.Host.World, 1, HeldItem.Tombstone);
        driver.RequestItemUse(); ready = true;
        for (int i = 0; i < 5; i++) { driver.Advance(default, Observe); }
        Assert.That(placements, Is.EqualTo(1));
        Assert.That(driver.LocalItem!.Active.Item, Is.EqualTo(HeldItem.Tombstone));
        driver.RequestItemUse(); driver.Advance(default, Observe);
        Assert.That(placements, Is.EqualTo(2));
    }

    [Test]
    public void RemoteQueuedTombstoneUseWaitsForConfirmedSelectionThenSendsOnceReliably()
    {
        using var wire = ConnectedGateway();
        using var client = new VehicleNetworkDriver(wire, 0, ServerPeer);
        var host = new HostVehicleSession(Session);
        host.Join(ServerPeer);
        host.Items.Grant(host.World, 2, HeldItem.Wrench);
        host.Items.Grant(host.World, 2, HeldItem.Tombstone);
        wire.Receive(new(ServerPeer, VehicleNetworkCodec.EncodeWelcome(Session, 2), TransportDelivery.Reliable));
        wire.Receive(new(ServerPeer, Core.Development.GameplayConfigurationCodec.Encode(Session, new(0, new())), TransportDelivery.Reliable));
        void Publish(ulong revision) => wire.Receive(new(ServerPeer, ItemCodec.EncodeState(new(revision, host.Snapshot(), host.Items.Slots, [], [], tombstones: host.Items.Tombstones)), TransportDelivery.Reliable));
        Publish(1); client.Advance(default, Observe);
        bool ready = false;
        client.TombstoneReady = _ => ready;
        client.Advance(new(0, 0, 0, 0, 0, InputButtons.SwitchItem | InputButtons.UseItem, 0), Observe);
        var selection = wire.Sent.Single(m => ItemCodec.IsItem(m.Payload.Span));
        Assert.That(ItemCodec.DecodeSwitch(selection.Payload.Span).Revision, Is.EqualTo(1));
        wire.Sent.Clear(); ready = true;
        client.Advance(default, Observe);
        Assert.That(wire.Sent.Any(m => ItemCodec.IsItem(m.Payload.Span)), Is.False, "Old presentation cannot release unconfirmed selection.");
        host.SwitchItem(ServerPeer, Session, 1, 1);
        Publish(2); client.Advance(default, Observe);
        for (int i = 0; i < 5; i++) { client.Advance(default, Observe); }
        var use = wire.Sent.Single(m => ItemCodec.IsItem(m.Payload.Span));
        Assert.That(use.Delivery, Is.EqualTo(TransportDelivery.Reliable));
        Assert.That(ItemCodec.DecodeUse(use.Payload.Span).Token, Is.EqualTo(client.LocalItem!.SecondToken));
        Assert.That(host.Items.Tombstones.Single().Stage, Is.EqualTo(TombstoneStage.RearShield), "Client submission cannot create the authoritative wall.");
    }
}
