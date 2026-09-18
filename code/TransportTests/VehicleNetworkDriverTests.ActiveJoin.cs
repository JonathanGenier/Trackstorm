using Trackstorm.Client.Networking;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Transport.Tests;

/// <summary>Fresh arena admission through the shared production lobby/checkpoint drivers.</summary>
internal sealed partial class VehicleNetworkDriverTests
{
    /// <summary>Production drivers bootstrap fresh current state before input, then create one live vehicle.</summary>
    [Test]
    public void ActiveJoinBootstrapsWithoutChangingExistingWorldAndActivatesOnce()
    {
        using var hostWire = new DriverGateway();
        var lobby = StartJoinHost(hostWire);
        var host = new VehicleNetworkDriver(hostWire, lobby.State!.Match, lobby: lobby);
        host.Host!.RegisterSpawns(PrototypeArena.Configuration);
        for (int tick = 0; tick < 190; tick++)
        {
            host.Advance(default, Observe);
        }

        Assert.That(host.Host.World.State.Match!.Phase, Is.EqualTo(MatchPhase.Active));
        host.Host.GiveItem(0, HeldItem.Missile);
        using var clientWire = ConnectedGateway();
        var clientLobby = new LobbyNetworkDriver(clientWire, 0, ServerPeer, "Fresh");
        hostWire.ConnectPeer(50);
        clientLobby.Pump(0);
        Transfer(clientWire, hostWire, ServerPeer, 50);
        host.Advance(default, Observe);
        Transfer(hostWire, clientWire, 50, ServerPeer, checkpoints: false);
        clientLobby.Pump(0);
        Assert.That(clientLobby.LocalPlayerId, Is.EqualTo(3));
        Assert.That(clientLobby.JoiningArena, Is.True);
        var client = new VehicleNetworkDriver(clientWire, 0, ServerPeer, clientLobby);
        client.Advance(Drive(), Observe);
        Assert.That(client.IsActive, Is.False);
        Assert.That(client.Prediction, Is.Null);
        Assert.That(client.Inputs!.Pending, Is.Empty);
        Assert.That(clientWire.Sent, Is.Empty, "No gameplay input while the complete checkpoint is delayed.");
        Assert.That(host.Host.World.State.Vehicles.Count, Is.EqualTo(2), "Pending admission must not create a live vehicle.");
        var existing = host.Host.World.State;

        Transfer(hostWire, clientWire, 50, ServerPeer);
        int resyncs = 0;
        client.Resynchronized += _ => resyncs++;
        client.Advance(default, Observe);
        Assert.That(client.IsActive, Is.False, "Installed state still waits for authoritative activation confirmation.");
        Assert.That(client.Prediction, Is.Not.Null);
        Assert.That(resyncs, Is.EqualTo(1));
        Assert.That(client.Match!.Phase, Is.EqualTo(MatchPhase.Active));
        Assert.That(client.ItemState!.Slots.Single(slot => slot.Vehicle == 1).Item, Is.EqualTo(HeldItem.Missile));
        Assert.That(client.ItemState.Spawns, Is.EqualTo(host.Host.Spawns!.States));
        Assert.That(client.ItemState.Events, Is.Empty);
        Assert.That(client.Match.Changes, Is.Empty);
        Assert.That(client.Latest!.Vehicles.Count, Is.EqualTo(3));
        Assert.That(host.Host.World.State, Is.EqualTo(existing), "Preparing/applying a bootstrap cannot mutate the running world.");
        Assert.That(client.History!.Snapshots.Count, Is.EqualTo(1));

        Transfer(clientWire, hostWire, ServerPeer, 50);
        hostWire.Receive(new TransportMessage(50, LobbyCodec.EncodeCommand(LobbyCommand.Activate, lobby.State), TransportDelivery.Reliable));
        hostWire.Receive(new TransportMessage(50, LobbyCodec.EncodeCommand(LobbyCommand.Join, null, name: "Retry"), TransportDelivery.Reliable));
        host.Advance(default, Observe);
        Transfer(hostWire, clientWire, 50, ServerPeer);
        client.Advance(default, Observe);
        Assert.That(client.IsActive, Is.True);
        Assert.That(lobby.State.Players.Count, Is.EqualTo(3));
        Assert.That(host.Host.World.State.Vehicles.Select(vehicle => vehicle.VehicleId), Is.EqualTo(new ulong[] { 1, 2, 3 }));
        Assert.That(host.Host.World.State.Match!.Phase, Is.EqualTo(MatchPhase.Active));
        Assert.That(host.Host.World.State.Match.Players.Take(2), Is.EqualTo(existing.Match!.Players));
        Assert.That(host.Host.World.State.Tick, Is.EqualTo(existing.Tick + 1));
        Assert.That(lobby.Authority!.IsPendingJoin(50), Is.False);
        Assert.That(hostWire.Sent.Any(message => message.RemotePeerId == 2 && message.Delivery == TransportDelivery.Reliable && !LobbyCodec.IsLobby(message.Payload.Span)), Is.True, "Existing streams receive normal authoritative publications.");
        Assert.That(lobby.Events.Entries.Count(entry => entry.Kind == "Joined" && entry.Actor == 3), Is.EqualTo(1));
        Assert.That(lobby.Events.Entries.Count(entry => entry.Kind == "Spawned" && entry.Target == 3), Is.EqualTo(1));
    }

    /// <summary>Transport loss or bounded timeout before checkpoint receipt leaves no simulation or score participant.</summary>
    /// <param name="timeout">Keep the socket connected but withhold checkpoint acknowledgement.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void InterruptedActiveBootstrapLeavesNoGhost(bool timeout)
    {
        using var wire = new DriverGateway();
        var lobby = StartJoinHost(wire);
        var host = new VehicleNetworkDriver(wire, lobby.State!.Match, lobby: lobby);
        wire.ConnectPeer(50);
        wire.Receive(new TransportMessage(50, LobbyCodec.EncodeCommand(LobbyCommand.Join, null, name: "Fresh"), TransportDelivery.Reliable));
        host.Advance(default, Observe);
        Assert.That(lobby.Authority!.IsPendingJoin(50), Is.True);
        if (timeout)
        {
            lobby.Pump(16);
        }
        else
        {
            wire.Disconnect(50);
        }

        host.Advance(default, Observe);
        Assert.That(lobby.State.Players.Count, Is.EqualTo(2));
        Assert.That(host.Host!.World.State.Vehicles.Count, Is.EqualTo(2));
        Assert.That(host.Host.World.State.Match!.Players.Count, Is.EqualTo(2));
        Assert.That(lobby.Authority.Peers.ContainsKey(50), Is.False);
    }

    private static LobbyNetworkDriver StartJoinHost(DriverGateway wire)
    {
        wire.ConnectPeer(2);
        var lobby = new LobbyNetworkDriver(wire, Session, 0, "Host");
        lobby.Authority!.Join(2, "Existing", "existing");
        lobby.Authority.SetReady(0, true);
        lobby.Authority.SetReady(2, true);
        Assert.That(lobby.Authority.Start(0), Is.True);
        return lobby;
    }

    private static void Transfer(DriverGateway from, DriverGateway to, ulong recipient, ulong sender, bool checkpoints = true)
    {
        foreach (var message in from.Sent.Where(message => message.RemotePeerId == recipient).ToArray())
        {
            if (!checkpoints && !LobbyCodec.IsLobby(message.Payload.Span))
            {
                continue;
            }

            to.Receive(new TransportMessage(sender, message.Payload, message.Delivery));
            from.Sent.Remove(message);
        }
    }
}
