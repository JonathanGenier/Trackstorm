using Trackstorm.Client.Networking;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;
using Trackstorm.Core.Simulation;

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
        Assert.That(client.Match!.Phase, Is.EqualTo(MatchPhase.Countdown));
        Assert.That(client.Match.CountdownAtTick, Is.EqualTo(host.Match!.CountdownAtTick));
        Assert.That(client.AllowsParticipation, Is.False);

        TransportMessage? activation = null;
        ulong deadline = host.Match.CountdownAtTick!.Value;
        while (host.Host.World.State.Tick < deadline + 10)
        {
            Transfer(clientWire, hostWire, ServerPeer, 2);
            host.Advance(Drive(), Observe);
            foreach (var message in hostWire.Sent.Where(message => message.RemotePeerId == 2).ToArray())
            {
                if (message.Payload.Length > 27 && MatchCodec.IsMatch(message.Payload.Span[27..]) &&
                    MatchCodec.Decode(message.Payload.Span[27..]).State.Phase == MatchPhase.Active)
                {
                    activation = message;
                    hostWire.Sent.Remove(message);
                }
            }

            Transfer(hostWire, clientWire, 2, ServerPeer);
            client.Advance(Drive(), Observe);
            Assert.That(client.AllowsParticipation, Is.False, "Neither prediction ticks nor a newer world snapshot can activate gameplay.");
            Assert.That(client.Inputs!.Pending.All(command => command.Frame.Accelerate == 0), Is.True);
        }

        Assert.That(activation, Is.Not.Null);
        Assert.That(client.Latest!.Tick, Is.GreaterThan(deadline));
        Assert.That(client.Match.Phase, Is.EqualTo(MatchPhase.Countdown));
        clientWire.Receive(new TransportMessage(ServerPeer, activation!.Value.Payload, activation.Value.Delivery));
        client.Advance(Drive(), Observe);
        Assert.That(client.AllowsParticipation, Is.True);
        Assert.That(client.Match.Phase, Is.EqualTo(MatchPhase.Active));
        Assert.That(client.Inputs!.Pending.Last().Frame.Accelerate, Is.EqualTo(ushort.MaxValue));
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

    /// <summary>Retaining an offline participant must not leave the remaining host waiting for its acknowledgement.</summary>
    [Test]
    public void DisconnectedLoadingParticipantDoesNotBlockCountdown()
    {
        using var wire = new DriverGateway();
        var lobby = StartJoinHost(wire);
        var host = new VehicleNetworkDriver(wire, lobby.State!.Match, lobby: lobby, applicationEntry: true);
        host.Advance(Drive(), Observe);
        Assert.That(host.EntryReady, Is.False);
        wire.Disconnect(2);
        host.Advance(Drive(), Observe);
        Assert.That(host.EntryReady, Is.True);
        Assert.That(lobby.State.Players.Single(player => player.Id == 2).Connected, Is.False);
        Assert.That(host.Host!.World.State.Vehicles.Count, Is.EqualTo(2));
        ulong deadline = host.Match!.CountdownAtTick!.Value;
        while (host.Host.World.State.Tick < deadline)
        {
            host.Advance(Drive(), Observe);
        }

        Assert.That(host.Match.Phase, Is.EqualTo(MatchPhase.Active));
        Assert.That(host.Host.World.State.Tick, Is.EqualTo(deadline));
        Assert.That(host.Host.World.State.Vehicles.Count, Is.EqualTo(2));
    }

    /// <summary>A retained player installs the current phase before sending controls on its new binding.</summary>
    /// <param name="finished">Resume the frozen result instead of an active match.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void ApplicationResumeReceivesCurrentPhaseAndAcknowledgesSynchronization(bool finished)
    {
        using var hostWire = new DriverGateway();
        var lobby = StartJoinHost(hostWire);
        var host = new VehicleNetworkDriver(hostWire, lobby.State!.Match, lobby: lobby, applicationEntry: true);
        ReleaseEntry(hostWire, lobby, host);
        for (int tick = 0; tick < 190; tick++)
        {
            host.Advance(default, Observe);
        }

        hostWire.Disconnect(2);
        host.Advance(default, Observe);
        if (finished)
        {
            var world = host.Host!.World.State;
            int target = world.Match!.KillTarget;
            var result = new MatchState(world.Tick, world.Match.Revision + 1, target, MatchPhase.Finished, null, 1, [new(1, target, 0, 1, 0), new(2, 0, target, 0, (ulong)target)]);
            host.Host.World.Restore(new SimulationState(world.Tick, world.LastInput, world.Vehicles, result));
        }

        ulong tickBeforeResume = host.Host!.World.State.Tick;
        var expected = host.Host.World.State.Match!;
        hostWire.ConnectPeer(50);
        Assert.That(lobby.Authority!.Resume(50, GameVersion.Current.ToString(), Session, 2, 1, "existing"), Is.True);
        using var clientWire = ConnectedGateway();
        var clientLobby = new LobbyNetworkDriver(clientWire, 0, ServerPeer, "Existing", expectedSession: Session);
        clientLobby.BeginResume(2, 1);
        clientWire.Receive(new TransportMessage(ServerPeer, LobbyCodec.EncodeState(lobby.State, 2), TransportDelivery.Reliable));
        clientLobby.Pump(0);
        clientWire.Sent.Clear();
        var client = new VehicleNetworkDriver(clientWire, 0, ServerPeer, clientLobby, applicationEntry: true);
        client.Advance(Drive(), Observe);
        Assert.That(client.AllowsParticipation, Is.False);
        Assert.That(client.Inputs!.Pending, Is.Empty);
        host.Advance(default, Observe);

        // Even an authenticated peer cannot submit controls before acknowledging its checkpoint.
        hostWire.Receive(new TransportMessage(50, ConnectionEnvelope.Encode(Session, 2, VehicleNetworkCodec.EncodeInputs(lobby.State.Match, [new(1, Drive())], 1)), TransportDelivery.Unreliable));
        int rejected = host.RejectedPackets;
        host.Advance(default, Observe);
        Assert.That(host.RejectedPackets, Is.EqualTo(rejected + 1));

        Transfer(hostWire, clientWire, 50, ServerPeer);
        client.Advance(Drive(), Observe);
        Assert.That(client.EntryReady, Is.True);
        Assert.That(client.Match!.Phase, Is.EqualTo(expected.Phase));
        Assert.That(client.Match.Players, Is.EqualTo(expected.Players));
        Assert.That(client.Match.Winner, Is.EqualTo(expected.Winner));
        Assert.That(client.AllowsParticipation, Is.EqualTo(!finished));
        Assert.That(client.Latest!.Tick, Is.GreaterThanOrEqualTo(tickBeforeResume));
        Transfer(clientWire, hostWire, ServerPeer, 50);
        host.Advance(default, Observe);
        Assert.That(host.Host.Snapshot().Vehicles.Single(vehicle => vehicle.State.VehicleId == 2).AcknowledgedInput, Is.EqualTo(1));
        Assert.That(host.Host.World.State.Match!.Phase, Is.EqualTo(expected.Phase));
        Assert.That(host.Host.World.State.Tick, Is.GreaterThan(tickBeforeResume));
        Assert.That(host.Host.CanJoin, Is.EqualTo(!finished));
    }
}
