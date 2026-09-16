using Trackstorm.Client.Hud;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Settings;
using Trackstorm.Core.Input;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Transport.Tests;

/// <summary>Checks shared published latency selection and replacement with no independent HUD sample.</summary>
[TestFixture]
internal sealed class TransportDiagnosticsTests
{
    /// <summary>Both providers use the same projection, and replacing provider/session cannot reuse a previous sample.</summary>
    /// <param name="name">Provider-owned neutral name.</param>
    [TestCase("EOS P2P")]
    [TestCase("Direct-IP")]
    public void ProjectsCurrentSessionAndInvalidatesReplacement(string name)
    {
        using var gateway = new DiagnosticGateway(name);
        var lobby = new LobbyNetworkDriver(gateway, 0, 7, "Client");
        Assert.That(TransportDiagnostics.Capture(gateway, lobby).State, Is.EqualTo(ConnectionDiagnosticState.Connecting));
        gateway.Admit(12, 1);
        lobby.Pump(0);
        Assert.That(TransportDiagnostics.Capture(gateway, lobby).Statistics.PingMilliseconds, Is.Null, "Local transport RTT cannot replace a missing publication.");
        var publisher = new PlayerLatency();
        lobby.Latency.Accept(publisher.Sample(lobby.State!, new Dictionary<ulong, ulong> { [7] = 2 }, gateway), lobby.State!);
        gateway.Ping = 999;
        var sample = TransportDiagnostics.Capture(gateway, lobby);
        Assert.That(sample.Statistics.PingMilliseconds, Is.EqualTo(42));
        Assert.That(sample.Transport, Is.EqualTo(name));
        Assert.That(sample.Statistics.IncomingQuality, Is.EqualTo(0.9f));
        lobby.NeedsArenaCheckpoint = true;
        Assert.That(TransportDiagnostics.Capture(gateway, lobby).Statistics.PingMilliseconds, Is.Null);
        lobby.NeedsArenaCheckpoint = false;
        gateway.States.Clear();
        Assert.That(TransportDiagnostics.Capture(gateway, lobby).State, Is.EqualTo(ConnectionDiagnosticState.Disconnected));
        Assert.That(TransportDiagnostics.Capture(gateway, lobby).Statistics.PingMilliseconds, Is.Null);
        using var replacement = new DiagnosticGateway("Replacement") { Ping = null };
        var newLobby = new LobbyNetworkDriver(replacement, 0, 7, "Client");
        replacement.Admit(13, 2);
        newLobby.Pump(0);
        var fresh = TransportDiagnostics.Capture(replacement, newLobby);
        Assert.That(fresh.Statistics.PingMilliseconds, Is.Null);
        Assert.That(fresh.Transport, Is.EqualTo("Replacement"));
        Assert.That(newLobby.State!.Session, Is.EqualTo(13));
        Assert.That(newLobby.Generation, Is.EqualTo(2));
        Assert.That(TransportDiagnostics.Capture(null, null).State, Is.EqualTo(ConnectionDiagnosticState.Disconnected));
    }

    /// <summary>Both labels share published values and invalidation even when local RTT differs.</summary>
    /// <param name="ping">Published bounded sample.</param>
    [TestCase(0)]
    [TestCase(123)]
    [TestCase(60000)]
    [TestCase(null)]
    public void HudAndLeaderboardSharePublishedPing(int? ping)
    {
        using var gateway = new DiagnosticGateway("EOS P2P") { Ping = ping };
        var lobby = new LobbyNetworkDriver(gateway, 0, 7, "Client");
        gateway.Admit(12, 1);
        lobby.Pump(0);
        var publisher = new PlayerLatency();
        byte[] publication = publisher.Sample(lobby.State!, new Dictionary<ulong, ulong> { [7] = 2 }, gateway);
        Assert.That(lobby.Latency.Accept(publication, lobby.State!), Is.True);
        gateway.Ping = 999;
        AssertShared(ping);
        lobby.Latency.Clear();
        AssertShared(null);
        Assert.That(lobby.Latency.Accept(publication, lobby.State!), Is.True);
        gateway.Admit(12, 2);
        lobby.Pump(0);
        AssertShared(null);

        void AssertShared(int? expected)
        {
            var state = lobby.State!;
            var arena = new LobbySnapshot(state.Session, state.Revision, state.Match + 1, SessionPhase.Arena, state.Players);
            var match = new MatchState(10, 1, 5, MatchPhase.Active, null, null, Array.Empty<PlayerScore>());
            var board = MatchStandingsView.From(arena, match, lobby.LocalPlayerId, InputButtons.Leaderboard, id => lobby.Latency.Get(state, id));
            var diagnostic = TransportDiagnostics.Capture(gateway, lobby);
            Assert.That(diagnostic.Statistics.PingMilliseconds, Is.EqualTo(expected));
            Assert.That(DiagnosticsView.Create(new(), null, diagnostic).Ping, Is.EqualTo("Ping  " + board.Rows.Single(row => row.Local).Ping));
        }
    }

    /// <summary>A host has no upstream peer, even when remote peers have samples.</summary>
    [Test]
    public void HostDoesNotDisplayArbitraryClientPing()
    {
        using var gateway = new DiagnosticGateway("Host");
        var lobby = new LobbyNetworkDriver(gateway, 12, 0, "Host");
        Assert.That(TransportDiagnostics.Capture(gateway, lobby).State, Is.EqualTo(ConnectionDiagnosticState.Unavailable));
        Assert.That(TransportDiagnostics.Capture(gateway, lobby).Statistics.PingMilliseconds, Is.Null);
    }

    private sealed class DiagnosticGateway(string name) : ITransportGateway
    {
        private readonly Queue<TransportMessage> _messages = new();
        public event Action<TransportConnectionChange>? ConnectionChanged
        {
            add { }
            remove { }
        }

        public bool IsListening => false;
        public string Name => name;
        public Dictionary<ulong, TransportConnectionState> States { get; } = new() { [7] = TransportConnectionState.Connected };
        public IReadOnlyDictionary<ulong, TransportConnectionState> Connections => States;
        public TransportConnectionState ConnectionState => TransportConnectionState.Connected;
        internal int? Ping { get; set; } = 42;
        public TransportStatistics GetStatistics(ulong peerId)
        {
            Assert.That(peerId, Is.EqualTo(7));
            return new(Ping, 0.9f, null);
        }

        public void Dispose()
        {
        }

        public void Poll()
        {
        }

        public void Send(TransportMessage message)
        {
        }

        public bool TryReceive(out TransportMessage message) => _messages.TryDequeue(out message);
        public void Listen(TransportEndpoint endpoint) => throw new NotSupportedException();
        public ulong Connect(TransportEndpoint endpoint) => throw new NotSupportedException();
        public void Disconnect(ulong peerId) => throw new NotSupportedException();
        public void Stop() => throw new NotSupportedException();
        public void ConfigureSimulation(NetworkSimulation simulation) => throw new NotSupportedException();

        internal void Admit(ulong session, ulong generation)
        {
            var state = new LobbySnapshot(session, generation, session, SessionPhase.Lobby, [new(1, "Host", false), new(2, "Client", false, true, generation)]);
            _messages.Enqueue(new(7, LobbyCodec.EncodeState(state, 2), TransportDelivery.Reliable));
        }
    }
}
