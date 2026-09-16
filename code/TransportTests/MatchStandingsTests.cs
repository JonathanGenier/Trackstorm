using Trackstorm.Client.Hud;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Transport.Tests;

/// <summary>Read-only standings and replicated diagnostics without a native runtime.</summary>
[TestFixture]
internal sealed class MatchStandingsTests
{
    /// <summary>Held intent controls Active only, Finished forces results, and each HUD position is the corresponding Core rank.</summary>
    [Test]
    public void EightRowsShareRankAndVisibilityWithHud()
    {
        LobbySnapshot roster = Roster();
        foreach (MatchPhase phase in Enum.GetValues<MatchPhase>())
        {
            var match = new MatchState(10, 1, 5, phase, phase == MatchPhase.Countdown ? 100ul : null, phase == MatchPhase.Finished ? 8ul : null, roster.Players.Select(player => new PlayerScore(player.Id, phase == MatchPhase.Finished && player.Id == 8 ? 5 : 0, phase == MatchPhase.Finished ? 1 : 0, phase == MatchPhase.Finished && player.Id == 8 ? 1 : 0, 1)));
            foreach (bool held in new[] { true, false })
            {
                foreach (SessionPlayer local in roster.Players)
                {
                    var view = MatchStandingsView.From(roster, match, local.Id, held ? InputButtons.Leaderboard : InputButtons.None, _ => null);
                    Assert.That(view.Visible, Is.EqualTo(phase == MatchPhase.Finished || (phase == MatchPhase.Active && held)));
                    Assert.That(view.Rows.Count, Is.EqualTo(8));
                    Assert.That(view.Position, Is.EqualTo(view.Rows.Single(row => row.PlayerId == local.Id).Rank.ToString()));
                    Assert.That(view.Rows.Select(row => row.PlayerId), Is.EqualTo(MatchRanking.Create(match, roster.Players.Select(player => player.Id)).Select(row => row.PlayerId)));
                    Assert.That(view.Rows.All(row => row.Ping == "--"), Is.True);
                    Assert.That(view.Rows.Where(row => row.Winner).Select(row => row.PlayerId), Is.EqualTo(phase == MatchPhase.Finished ? new ulong[] { 8 } : Array.Empty<ulong>()));
                }
            }
        }

        Assert.That(MatchStandingsView.From(null, null, 0, InputButtons.Leaderboard, _ => 12).Visible, Is.False);
    }

    /// <summary>Bindings, session revisions, unavailable adapters and sample expiry cannot leak old diagnostics into another player.</summary>
    [Test]
    public void PingPublicationExpiresRejectsOldRosterAndResamplesRebind()
    {
        using var gateway = new LatencyGateway();
        var host = new PlayerLatency();
        double now = 0;
        var client = new PlayerLatency(() => now);
        LobbySnapshot roster = Roster();
        var peers = new Dictionary<ulong, ulong> { [20] = 2, [30] = 3 };
        byte[] first = host.Sample(roster, peers, gateway);
        Assert.That(client.Accept(first, roster), Is.True);
        Assert.That(client.Get(roster, 1), Is.Null);
        Assert.That(client.Get(roster, 2), Is.EqualTo(20));
        Assert.That(client.Get(roster, 3), Is.EqualTo(30));
        Assert.That(client.Accept(first, roster), Is.False);
        now = 3.01;
        Assert.That(client.Get(roster, 2), Is.Null);
        peers[20] = 3;
        peers[30] = 2;
        Assert.That(client.Accept(host.Sample(roster, peers, gateway), roster), Is.True);
        Assert.That(client.Get(roster, 2), Is.EqualTo(30));
        Assert.That(client.Get(roster, 3), Is.EqualTo(20));
        var newer = new LobbySnapshot(roster.Session, 2, roster.Match, roster.Phase, roster.Players);
        Assert.That(client.Get(newer, 2), Is.Null);
        var disconnected = new LobbySnapshot(roster.Session, 3, roster.Match, roster.Phase, roster.Players.Select(player => player.Id == 2 ? player with { Connected = false, Ready = false } : player));
        Assert.That(client.Accept(host.Sample(disconnected, peers, gateway), disconnected), Is.True);
        Assert.That(client.Get(disconnected, 2), Is.Null, "Reserved disconnected players cannot retain a ping even with a retired peer mapping.");
        client.Clear();
        Assert.That(client.Get(disconnected, 3), Is.Null, "Local interruption invalidates all remote samples immediately.");
        Assert.That(client.Accept(first, newer), Is.False);
        gateway.Available = false;
        Assert.That(client.Accept(host.Sample(newer, peers, gateway), newer), Is.True);
        Assert.That(client.Get(newer, 2), Is.Null);
        Assert.That(client.Accept(first.AsSpan(0, 20), roster), Is.False);
        Assert.That(client.Accept(first.Concat(new byte[] { 0 }).ToArray(), roster), Is.False);
    }

    /// <summary>Malformed identities, values, counts and session boundaries never partially overwrite a good publication.</summary>
    [Test]
    public void InvalidDiagnosticsCannotReplaceAcceptedSamples()
    {
        using var gateway = new LatencyGateway();
        var host = new PlayerLatency();
        var client = new PlayerLatency();
        LobbySnapshot roster = Roster();
        var peers = new Dictionary<ulong, ulong> { [20] = 2, [30] = 3 };
        Assert.That(client.Accept(host.Sample(roster, peers, gateway), roster), Is.True);
        byte[] next = host.Sample(roster, peers, gateway);
        foreach (int offset in new[] { 2, 3, 4, 12, 20, 36, 44, 48, 56 })
        {
            byte[] invalid = (byte[])next.Clone();
            invalid[offset] = 255;
            if (offset is 44 or 56)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(offset), -2);
            }

            Assert.That(client.Accept(invalid, roster), Is.False, $"Reject corrupt field at {offset}");
            Assert.That(client.Get(roster, 2), Is.EqualTo(20), "Atomic rejection preserves prior samples");
        }

        Assert.That(client.Accept(next, roster), Is.True);
        var match = new MatchState(10, 1, 5, MatchPhase.Active, null, null, Array.Empty<PlayerScore>());
        foreach (int? latency in new int?[] { null, -1, 60001 })
        {
            Assert.That(MatchStandingsView.From(roster, match, 1, InputButtons.Leaderboard, _ => latency).Rows.All(row => row.Ping == "--"), Is.True);
        }
    }

    private static LobbySnapshot Roster() => new(100, 1, 101, SessionPhase.Arena, Enumerable.Range(1, 8).Select(id => new SessionPlayer((ulong)id, $"Player {id}", true)));

    private sealed class LatencyGateway : ITransportGateway
    {
        public event Action<TransportConnectionChange>? ConnectionChanged
        {
            add { }
            remove { }
        }

        public bool Available { get; set; } = true;
        public bool IsListening => true;
        public TransportConnectionState ConnectionState => TransportConnectionState.Connected;
        public IReadOnlyDictionary<ulong, TransportConnectionState> Connections { get; } = new Dictionary<ulong, TransportConnectionState> { [20] = TransportConnectionState.Connected, [30] = TransportConnectionState.Connected };
        public TransportStatistics GetStatistics(ulong peerId) => new(Available ? (int)peerId : null, null, null);
        public void Dispose()
        {
        }

        public void Listen(TransportEndpoint endpoint) => throw new NotSupportedException();
        public ulong Connect(TransportEndpoint endpoint) => throw new NotSupportedException();
        public void Disconnect(ulong peerId) => throw new NotSupportedException();
        public void Poll()
        {
        }

        public void Stop()
        {
        }

        public void ConfigureSimulation(NetworkSimulation simulation) => throw new NotSupportedException();
        public void Send(TransportMessage message) => throw new NotSupportedException();
        public bool TryReceive(out TransportMessage message)
        {
            message = default;
            return false;
        }
    }
}
