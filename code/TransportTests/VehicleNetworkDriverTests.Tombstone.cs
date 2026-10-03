using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Transport.Tests;

internal sealed partial class VehicleNetworkDriverTests
{
    [Test]
    public void TombstoneHealthAndRemovalReplicateWhileForgedOutcomesAndDuplicatePublicationsAreRejected()
    {
        using var hostWire = ConnectedGateway();
        using var host = new VehicleNetworkDriver(hostWire, Session,
            configuration: new() { Match = new() { MinimumPlayers = 1, CountdownTicks = 1 } });
        using var clientWire = ConnectedGateway();
        using var client = new VehicleNetworkDriver(clientWire, 0, ServerPeer);
        void Transfer()
        {
            foreach (var packet in hostWire.Sent) { clientWire.Receive(packet with { RemotePeerId = ServerPeer }); }
            hostWire.Sent.Clear();
            client.Advance(default, Observe);
        }
        host.Advance(default, Observe); Transfer();
        host.Advance(default, Observe); Transfer();
        var authority = host.Host!;
        authority.Items.Grant(authority.World, 2, HeldItem.Tombstone);
        host.Advance(default, Observe); Transfer();
        var initial = client.ItemState!;
        var stone = initial.Tombstones.Single();
        Assert.That(stone.HP, Is.EqualTo(1000));
        Assert.That(stone.Stage, Is.EqualTo(TombstoneStage.RearShield));
        Assert.That(authority.UseItem(ServerPeer, Session, stone.Life, stone.Token), Is.True);
        var forged = new ItemPublication(initial.Revision + 1, initial.World, initial.Slots, [], [], tombstones: [stone with { HP = 999, DamageSequence = 1 }]);
        hostWire.Receive(new(ServerPeer, ItemCodec.EncodeState(forged), TransportDelivery.Reliable));
        host.Advance(default, Observe); Transfer();
        Assert.That(authority.Items.Tombstones.Single().HP, Is.EqualTo(1000));
        Assert.That(client.ItemState!.Tombstones.Single().Stage, Is.EqualTo(TombstoneStage.RearShield));
        Assert.That(host.RejectedPackets, Is.GreaterThan(0));
        int observed = 0;
        client.ItemsReceived += _ => observed++;
        authority.Items.DamageTombstone(authority.World, stone.Id, 1, 125, new DamageContext("world", 0, "test"));
        host.Advance(default, Observe); Transfer();
        Assert.That(client.ItemState!.Tombstones.Single().HP, Is.EqualTo(875));
        Assert.That(client.ItemState.Tombstones.Single().Stage, Is.EqualTo(TombstoneStage.RearShield));
        for (ulong revision = 1; revision <= 12; revision++)
        {
            Assert.That(authority.SwitchItem(ServerPeer, Session, stone.Life, revision), Is.True);
            Assert.That(authority.SwitchItem(ServerPeer, Session, stone.Life, revision), Is.False);
            host.Advance(default, Observe); Transfer();
            Assert.That(client.ItemState!.Tombstones.Single(), Is.EqualTo(stone with { HP = 875, DamageSequence = 1,
                Stage = revision % 2 == 0 ? TombstoneStage.RearShield : TombstoneStage.Held }));
        }
        observed = 1;
        byte[] duplicate = ItemCodec.EncodeState(client.ItemState);
        clientWire.Receive(new(ServerPeer, duplicate, TransportDelivery.Reliable));
        clientWire.Receive(new(123, ItemCodec.EncodeState(forged), TransportDelivery.Reliable));
        clientWire.Receive(new(ServerPeer, ItemCodec.EncodeState(initial), TransportDelivery.Reliable));
        client.Advance(default, Observe);
        Assert.That(observed, Is.EqualTo(1));
        Assert.That(client.ItemState.Tombstones.Single().HP, Is.EqualTo(875));
        host.PlaceTombstone = (_, _, _) => new(new(0, 2, 10), System.Numerics.Quaternion.Identity, new(2, 0, 0), System.Numerics.Vector3.Zero);
        var use = ItemCodec.EncodeUse(Session, stone.Life, stone.Token);
        hostWire.Receive(new(ServerPeer, use, TransportDelivery.Reliable));
        hostWire.Receive(new(ServerPeer, use, TransportDelivery.Reliable));
        host.Advance(default, Observe); Transfer();
        Assert.That(client.ItemState.Tombstones.Single(), Is.EqualTo(stone with { HP = 875, DamageSequence = 1,
            Stage = TombstoneStage.WorldWall, Life = 0, Token = 0, Position = new(0, 2, 10), LinearVelocity = new(2, 0, 0), ExpiresAtTick = host.Host!.World.State.Tick + 7200 }));
        Assert.That(client.ItemState.Slots.Single(s => s.Vehicle == 2).Item, Is.EqualTo(HeldItem.None));
        clientWire.Receive(new(ServerPeer, duplicate, TransportDelivery.Reliable));
        client.Advance(default, Observe);
        Assert.That(client.ItemState.Tombstones.Single().Stage, Is.EqualTo(TombstoneStage.WorldWall), "Old shield publication cannot resurrect attachment");
        authority.Items.DamageTombstone(authority.World, stone.Id, 2, 1000, new DamageContext("world", 0, "test"));
        host.Advance(default, Observe); Transfer();
        Assert.That(client.ItemState.Tombstones, Is.Empty);
        Assert.That(client.ItemState.Slots.Single(s => s.Vehicle == 2).Item, Is.EqualTo(HeldItem.None));
        clientWire.Receive(new(ServerPeer, duplicate, TransportDelivery.Reliable));
        client.Advance(default, Observe);
        Assert.That(observed, Is.EqualTo(3));
        Assert.That(client.ItemState.Tombstones, Is.Empty);
    }
}
