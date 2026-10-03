using System.Numerics;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Transport.Tests;

internal sealed partial class VehicleNetworkDriverTests
{
    [Test]
    public void AimRequestsCannotClaimAnotherPlayerOrWrongCapabilityAndSamplesExpire()
    {
        using var wire = ConnectedGateway();
        using var host = new VehicleNetworkDriver(wire, Session);
        host.Advance(default, Observe);
        host.Host!.Items.Grant(host.Host.World, 1, HeldItem.MachineGun);
        host.Host.Items.Grant(host.Host.World, 2, HeldItem.MachineGun);
        var local = host.Host.Items.Slots.Single(slot => slot.Vehicle == 1);
        var remote = host.Host.Items.Slots.Single(slot => slot.Vehicle == 2);
        wire.Receive(new(ServerPeer, ItemCodec.EncodeAim(Session, local.Life, local.Token, 0, 1, Vector3.UnitX), TransportDelivery.Unreliable));
        wire.Receive(new(ServerPeer, ItemCodec.EncodeAim(Session, remote.Life, remote.Token, 0, 1, Vector3.UnitX), TransportDelivery.Reliable));
        host.Advance(default, Observe);
        Assert.That(host.RejectedPackets, Is.EqualTo(2));
        Assert.That(host.Host.Items.Aims, Is.Empty);
        wire.Receive(new(ServerPeer, ItemCodec.EncodeAim(Session, remote.Life, remote.Token, 0, 2, Vector3.UnitX), TransportDelivery.Unreliable));
        host.Advance(default, Observe);
        Assert.That(host.Host.Items.Aims.Single().Vehicle, Is.EqualTo(2));
        var accepted = host.Host.Items.Aims.Single();
        wire.Receive(new(ServerPeer, ItemCodec.EncodeAim(Session, remote.Life, remote.Token, 0, 1, -Vector3.UnitX), TransportDelivery.Unreliable));
        host.Advance(default, Observe);
        Assert.That(host.RejectedPackets, Is.EqualTo(3));
        Assert.That(host.Host.Items.Aims.Single().Yaw, Is.LessThan(accepted.Yaw), "Older counter cannot reverse accepted yaw intent.");
        for (int i = 0; i < 20; i++) { host.Advance(default, Observe); }
        Assert.That(host.Host.Items.Aims, Is.Empty);
    }

    [Test]
    public void AcceptedAimsRequireCurrentHostGenerationConfigurationAndMonotonicTick()
    {
        using var hostWire = ConnectedGateway();
        using var clientWire = ConnectedGateway();
        double seconds = 0;
        using var host = new VehicleNetworkDriver(hostWire, Session);
        using var client = new VehicleNetworkDriver(clientWire, 0, ServerPeer, seconds: () => seconds);
        host.Advance(default, Observe);
        foreach (var packet in hostWire.Sent) { clientWire.Receive(packet); }
        hostWire.Sent.Clear(); client.Advance(default, Observe);
        var aim = new WeaponAimSolution(1, 1, 1, 2, 0, 0, WeaponAim.Pivot, -Vector3.UnitZ, true);
        ulong configuration = host.Configuration.Revision;
        byte[] valid = ItemCodec.EncodeAims(Session, 2, configuration, [aim]);
        foreach (var packet in new[] {
            new TransportMessage(ServerPeer + 1, valid, TransportDelivery.Unreliable),
            new TransportMessage(ServerPeer, valid, TransportDelivery.Reliable),
            new TransportMessage(ServerPeer, ItemCodec.EncodeAims(Session + 1, 2, configuration, [aim]), TransportDelivery.Unreliable),
            new TransportMessage(ServerPeer, ItemCodec.EncodeAims(Session, 2, configuration + 1, [aim]), TransportDelivery.Unreliable) })
        { clientWire.Receive(packet); }
        client.Advance(default, Observe);
        Assert.That(client.AcceptedAims, Is.Empty);
        Assert.That(client.RejectedPackets, Is.EqualTo(4));
        clientWire.Receive(new(ServerPeer, valid, TransportDelivery.Unreliable)); client.Advance(default, Observe);
        Assert.That(client.AcceptedAims, Is.EqualTo(new[] { aim }));
        clientWire.Receive(new(ServerPeer, valid, TransportDelivery.Unreliable)); client.Advance(default, Observe);
        Assert.That(client.RejectedPackets, Is.EqualTo(5));
        seconds = .31;
        Assert.That(client.AcceptedAims, Is.Empty, "A transport outage cannot freeze a live firing marker.");
    }
}
