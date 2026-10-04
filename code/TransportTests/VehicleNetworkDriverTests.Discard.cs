using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Transport.Tests;

internal sealed partial class VehicleNetworkDriverTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void DiscardUsesReliableAuthorityAndRejectsDelayedRequestsAndPublications(bool second)
    {
        using var hostWire = ConnectedGateway();
        using var host = new VehicleNetworkDriver(hostWire, Session);
        using var clientWire = ConnectedGateway();
        using var client = new VehicleNetworkDriver(clientWire, 0, ServerPeer);
        void Transfer()
        {
            foreach (var packet in hostWire.Sent) { clientWire.Receive(packet with { RemotePeerId = ServerPeer }); }
            hostWire.Sent.Clear(); client.Advance(default, Observe); clientWire.Sent.Clear();
        }
        host.Advance(default, Observe); Transfer();
        var authority = host.Host!;
        authority.Items.Grant(authority.World, 2, HeldItem.Shield);
        authority.Items.Grant(authority.World, 2, HeldItem.MachineGun);
        host.Advance(default, Observe); Transfer();
        var initial = client.ItemState!;
        client.Advance(new InputFrame(0, 0, 0, 0, 0, InputButtons.DiscardItem | InputButtons.UseItem | (second ? InputButtons.SwitchItem : 0), 0), Observe);
        var commands = clientWire.Sent.Where(packet => ItemCodec.IsItem(packet.Payload.Span)).ToArray();
        Assert.That(commands.All(packet => packet.Delivery == TransportDelivery.Reliable), Is.True);
        Assert.That(ItemCodec.IsDiscard(commands[second ? 1 : 0].Payload.Span), Is.True, "Switch then discard precede use in a shared frame.");
        Assert.That(authority.Items.Slots.Single().Full, Is.True, "Remote capture cannot mutate authority.");
        foreach (var packet in commands) { hostWire.Receive(packet with { RemotePeerId = ServerPeer }); }
        host.Advance(default, Observe); Transfer();
        var cleared = client.LocalItem!;
        Assert.That(cleared.Active.Item, Is.EqualTo(HeldItem.None));
        Assert.That(second ? cleared.Item : cleared.SecondItem, Is.EqualTo(second ? HeldItem.Shield : HeldItem.MachineGun));
        Assert.That(authority.Items.Events, Is.Empty, "Use after discard is rejected.");
        Assert.That(authority.Items.Shields.Count, Is.EqualTo(second ? 1 : 0));
        Assert.That(client.RequestItemDiscard(), Is.False, "Empty discard emits no request.");
        authority.Items.Grant(authority.World, 2, HeldItem.Wrench);
        var replacement = authority.Items.Slots.Single();
        var staleDiscard = commands.Single(packet => ItemCodec.IsDiscard(packet.Payload.Span));
        hostWire.Receive(staleDiscard with { RemotePeerId = ServerPeer });
        hostWire.Receive(staleDiscard with { RemotePeerId = ServerPeer, Delivery = TransportDelivery.Unreliable });
        hostWire.Receive(staleDiscard with { RemotePeerId = 123 });
        host.Advance(default, Observe); Transfer();
        Assert.That(authority.Items.Slots.Single().Item, Is.EqualTo(replacement.Item));
        Assert.That(authority.Items.Slots.Single().SecondItem, Is.EqualTo(replacement.SecondItem));
        var current = client.ItemState;
        Assert.That(current!.DiscardRevision, Is.EqualTo(1));
        var regressed = new ItemPublication(current.Revision + 1, current.World, current.Slots, current.Missiles, [], current.Spawns, current.Patches, current.OilContacts, current.Balances, current.Mines, current.Shields);
        clientWire.Receive(new(ServerPeer, ItemCodec.EncodeState(regressed), TransportDelivery.Reliable));
        clientWire.Receive(new(ServerPeer, ItemCodec.EncodeState(initial), TransportDelivery.Reliable));
        clientWire.Receive(new(ServerPeer, ItemCodec.EncodeState(current!), TransportDelivery.Reliable));
        clientWire.Receive(new(123, ItemCodec.EncodeState(initial), TransportDelivery.Reliable));
        client.Advance(default, Observe);
        Assert.That(client.ItemState, Is.SameAs(current), "Old, repeated and foreign publications cannot resurrect discarded ownership.");
        Assert.That(host.RejectedPackets, Is.GreaterThanOrEqualTo(4));
        Assert.That(client.RejectedPackets, Is.GreaterThanOrEqualTo(4));
    }
}
