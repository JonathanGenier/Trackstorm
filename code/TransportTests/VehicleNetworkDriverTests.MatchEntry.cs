using Trackstorm.Client.Networking;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Transport.Tests;

/// <summary>Application entry cannot mistake local loading for completed multiplayer synchronization.</summary>
internal sealed partial class VehicleNetworkDriverTests
{
    /// <summary>The host freezes at tick zero until the loaded client installs and acknowledges its checkpoint.</summary>
    [Test]
    public void InitialEntryWaitsForCheckpointAndHostRelease()
    {
        using var hostWire = new DriverGateway();
        hostWire.ConnectPeer(2);
        var lobby = new LobbyNetworkDriver(hostWire, Session, 0, "Host");
        lobby.Authority!.Join(2, GameVersion.Current.ToString(), "Client");
        using var clientWire = ConnectedGateway();
        var clientLobby = new LobbyNetworkDriver(clientWire, 0, ServerPeer, "Client");
        clientWire.Receive(new TransportMessage(ServerPeer, LobbyCodec.EncodeState(lobby.State!, 2), TransportDelivery.Reliable));
        clientLobby.Pump(0);
        clientWire.Sent.Clear();
        lobby.Authority.SetReady(0, true);
        lobby.Authority.SetReady(2, true);
        lobby.Request(LobbyCommand.Start);
        Transfer(hostWire, clientWire, 2, ServerPeer);
        clientLobby.Pump(0);
        var host = new VehicleNetworkDriver(hostWire, lobby.State!.Match, lobby: lobby, applicationEntry: true);
        for (int tick = 0; tick < 30; tick++)
        {
            host.Advance(Drive(), Observe);
        }

        Assert.That(host.Host!.World.State.Tick, Is.Zero);
        Assert.That(host.EntryContext, Is.Null);
        var client = new VehicleNetworkDriver(clientWire, 0, ServerPeer, clientLobby, applicationEntry: true);
        client.Advance(Drive(), Observe);
        Assert.That(client.EntryReady, Is.False);
        Assert.That(client.Inputs!.Pending, Is.Empty);
        Transfer(clientWire, hostWire, ServerPeer, 2);
        host.Advance(Drive(), Observe);
        Transfer(hostWire, clientWire, 2, ServerPeer);
        client.Advance(Drive(), Observe);
        Assert.That(client.Prediction, Is.Not.Null);
        Assert.That(client.EntryReady, Is.False, "Installed resources and checkpoint still need host release.");
        Assert.That(client.Inputs!.Pending, Is.Empty);
        Assert.That(host.Host.World.State.Tick, Is.Zero);
        Transfer(clientWire, hostWire, ServerPeer, 2);
        host.Advance(default, Observe);
        Assert.That(host.EntryReady, Is.True);
        Assert.That(host.Host.World.MatchEntry, Is.SameAs(host.EntryContext));
        Transfer(hostWire, clientWire, 2, ServerPeer);
        client.Advance(default, Observe);
        Assert.That(client.EntryReady, Is.True);
        Assert.That(client.EntryContext!.MatchId, Is.EqualTo(lobby.State.Match));
    }

    /// <summary>A missing peer cannot leave a partially simulated host running forever.</summary>
    [Test]
    public void InitialSynchronizationHasABoundedFailure()
    {
        using var wire = new DriverGateway();
        var lobby = StartJoinHost(wire);
        var driver = new VehicleNetworkDriver(wire, lobby.State!.Match, lobby: lobby, applicationEntry: true);
        for (int tick = 0; tick < 1802; tick++)
        {
            driver.Advance(Drive(), Observe);
        }

        Assert.That(driver.Failure, Does.Contain("timed out"));
        Assert.That(driver.Host!.World.State.Tick, Is.Zero);
        Assert.That(driver.EntryReady, Is.False);
    }
}
