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
    public void ShieldUseWaitsForPresentationAndCoalescesRepeatedTaps(bool tapOnReady)
    {
        using var wire = ConnectedGateway();
        using var driver = new VehicleNetworkDriver(wire, Session,
            configuration: new() { Match = new() { MinimumPlayers = 1, CountdownTicks = 1 } });
        bool ready = false;
        driver.ShieldReady = _ => ready;
        int placements = 0;
        driver.PlaceShield = (_, _, _) => { placements++; return new(new(0, 2, 10), System.Numerics.Quaternion.Identity, default, default); };
        driver.Advance(default, Observe); driver.Advance(default, Observe);
        var authority = driver.Host!;
        authority.Items.Grant(authority.World, 1, HeldItem.Wrench);
        authority.Items.Grant(authority.World, 1, HeldItem.Shield);
        driver.Advance(new(0, 0, 0, 0, 0, InputButtons.SwitchItem | InputButtons.UseItem, 0), Observe);
        for (int i = 0; i < 30; i++) { driver.Advance(new(0, 0, 0, 0, 0, InputButtons.UseItem, 0), Observe); }
        Assert.That(placements, Is.Zero);
        Assert.That(authority.Items.Shields.Single().Stage, Is.EqualTo(ShieldStage.RearShield));
        ready = true;
        driver.Advance(new(0, 0, 0, 0, 0, tapOnReady ? InputButtons.UseItem : 0, 0), Observe);
        for (int i = 0; i < 5; i++) { driver.Advance(default, Observe); }
        Assert.That(placements, Is.EqualTo(1));
        Assert.That(authority.Items.Shields.Single().Stage, Is.EqualTo(ShieldStage.WorldWall));
        Assert.That(driver.LocalItem!.Item, Is.EqualTo(HeldItem.Wrench));
    }

    [TestCase("switch")]
    [TestCase("discard")]
    [TestCase("destroy")]
    [TestCase("death")]
    [TestCase("life")]
    public void QueuedShieldUseCannotSurviveCapabilityCancellation(string cancellation)
    {
        using var wire = ConnectedGateway();
        using var driver = new VehicleNetworkDriver(wire, Session,
            configuration: new() { Match = new() { MinimumPlayers = 1, CountdownTicks = 1 } });
        bool ready = false;
        driver.ShieldReady = _ => ready;
        int placements = 0;
        driver.PlaceShield = (_, _, _) => { placements++; return null; };
        driver.Advance(default, Observe); driver.Advance(default, Observe);
        var authority = driver.Host!;
        authority.Items.Grant(authority.World, 1, HeldItem.Shield);
        Assert.That(driver.RequestItemUse(), Is.True);
        if (cancellation == "switch") { driver.RequestItemSwitch(); driver.RequestItemSwitch(); }
        else if (cancellation == "discard") { driver.RequestItemDiscard(); authority.Items.Grant(authority.World, 1, HeldItem.Shield); }
        else if (cancellation == "destroy") { authority.Items.DamageShield(authority.World, authority.Items.Shields.Single().Id, 1, 1000, new("world", 0, "test")); authority.Items.Grant(authority.World, 1, HeldItem.Shield); }
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
        if (cancellation is "life" or "death") { Assert.That(authority.Items.Shields, Is.Empty); }
        else { Assert.That(authority.Items.Shields.Single().Stage, Is.EqualTo(ShieldStage.RearShield)); }
    }

    [Test]
    public void BlockedQueuedDeploymentRequiresAnotherPressInsteadOfRetryingForever()
    {
        using var wire = ConnectedGateway();
        using var driver = new VehicleNetworkDriver(wire, Session,
            configuration: new() { Match = new() { MinimumPlayers = 1, CountdownTicks = 1 } });
        bool ready = false;
        driver.ShieldReady = _ => ready;
        int placements = 0;
        driver.PlaceShield = (_, _, _) => { placements++; return null; };
        driver.Advance(default, Observe); driver.Advance(default, Observe);
        driver.Host!.Items.Grant(driver.Host.World, 1, HeldItem.Shield);
        driver.RequestItemUse(); ready = true;
        for (int i = 0; i < 5; i++) { driver.Advance(default, Observe); }
        Assert.That(placements, Is.EqualTo(1));
        Assert.That(driver.LocalItem!.Active.Item, Is.EqualTo(HeldItem.Shield));
        driver.RequestItemUse(); driver.Advance(default, Observe);
        Assert.That(placements, Is.EqualTo(2));
    }

    [Test]
    public void RemoteQueuedShieldUseWaitsForConfirmedSelectionThenSendsOnceReliably()
    {
        using var wire = ConnectedGateway();
        using var client = new VehicleNetworkDriver(wire, 0, ServerPeer);
        var host = new HostVehicleSession(Session);
        host.Join(ServerPeer);
        host.Items.Grant(host.World, 2, HeldItem.Wrench);
        host.Items.Grant(host.World, 2, HeldItem.Shield);
        wire.Receive(new(ServerPeer, VehicleNetworkCodec.EncodeWelcome(Session, 2), TransportDelivery.Reliable));
        wire.Receive(new(ServerPeer, Core.Development.GameplayConfigurationCodec.Encode(Session, new(0, new())), TransportDelivery.Reliable));
        void Publish(ulong revision) => wire.Receive(new(ServerPeer, ItemCodec.EncodeState(new(revision, host.Snapshot(), host.Items.Slots, [], [], shields: host.Items.Shields)), TransportDelivery.Reliable));
        Publish(1); client.Advance(default, Observe);
        bool ready = false;
        client.ShieldReady = _ => ready;
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
        Assert.That(host.Items.Shields.Single().Stage, Is.EqualTo(ShieldStage.RearShield), "Client submission cannot create the authoritative wall.");
    }
}
