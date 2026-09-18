using Trackstorm.Core.Sessions;

namespace Trackstorm.Core.Tests.Sessions;

/// <summary>Authenticated reservation boundaries and stable identity invariants.</summary>
[TestFixture]
internal sealed class ReconnectTests
{
    /// <summary>Loss clears readiness and ownership; one authenticated rebind retains the original record.</summary>
    [Test]
    public void RebindRetainsIdentityAndRejectsOldOwnershipAndReplay()
    {
        var lobby = new LobbyAuthority(100, "Host");
        ulong player = lobby.Join(10, GameVersion.Current.ToString(), "Original", "subject-a");
        lobby.SetReady(0, true);
        lobby.SetReady(10, true);
        Assert.That(lobby.Start(0, [10]), Is.True);
        Assert.That(lobby.Disconnect(10), Is.True);
        Assert.That(lobby.Disconnect(10), Is.False);
        Assert.That(lobby.State.Players.Single(p => p.Id == player), Is.EqualTo(new SessionPlayer(player, "Original", false, false)));
        Assert.That(lobby.State.CanStart, Is.False);
        Assert.That(lobby.PlayerId(10), Is.Zero);
        Assert.That(lobby.SetReady(10, true), Is.False);
        Assert.That(lobby.Join(20, GameVersion.Current.ToString(), "Duplicate", "subject-a"), Is.Zero);
        Assert.That(lobby.Resume(20, GameVersion.Current.ToString(), 101, player, 1, "subject-a"), Is.False);
        Assert.That(lobby.Resume(20, GameVersion.Current.ToString(), 100, player, 1, "subject-b"), Is.False);
        Assert.That(lobby.Resume(20, GameVersion.Current.ToString(), 100, player, 2, "subject-a"), Is.False);
        Assert.That(lobby.Resume(10, GameVersion.Current.ToString(), 100, player, 1, "subject-a"), Is.False);
        Assert.That(lobby.Resume(20, GameVersion.Current.ToString(), 100, player, 1, "subject-a"), Is.True);
        Assert.That(lobby.Resume(21, GameVersion.Current.ToString(), 100, player, 1, "subject-a"), Is.False);
        Assert.That(lobby.State.Players.Count, Is.EqualTo(2));
        Assert.That(lobby.State.Players.Single(p => p.Id == player), Is.EqualTo(new SessionPlayer(player, "Original", false, true, 2)));
        Assert.That(lobby.Remove(10), Is.False);
        Assert.That(lobby.PlayerId(20), Is.EqualTo(player));
        lobby.AdvanceTime(60);
        Assert.That(lobby.State.Players.Count, Is.EqualTo(2), "Successful resume keeps the same player.");
    }

    /// <summary>Every arena departure reserves capacity until Return, independently of elapsed time.</summary>
    /// <param name="intentional">Whether departure is an explicit Leave.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void MatchEndReleasesSlotsAndAuthorization(bool intentional)
    {
        var lobby = new LobbyAuthority(100, "Host");
        for (ulong peer = 1; peer < 8; peer++)
        {
            lobby.Join(peer, GameVersion.Current.ToString(), "Player", "subject-" + peer);
            lobby.SetReady(peer, true);
        }

        lobby.SetReady(0, true);
        Assert.That(lobby.Start(0, Enumerable.Range(1, 7).Select(value => (ulong)value)), Is.True);
        ulong id = lobby.PlayerId(1);
        Assert.That(intentional ? lobby.Remove(1) : lobby.Disconnect(1), Is.True);
        Assert.That(lobby.Join(8, GameVersion.Current.ToString(), "Full"), Is.Zero);
        ulong revision = lobby.State.Revision;
        foreach (ulong tick in new ulong[] { 1800, 7201, 216001, 1000000 })
        {
            lobby.AdvanceTime(tick);
            Assert.That(lobby.State.Revision, Is.EqualTo(revision));
            Assert.That(lobby.State.Players.Count, Is.EqualTo(8));
            Assert.That(lobby.FindPlayer("subject-1"), Is.EqualTo(id));
            Assert.That(lobby.State.Players.Single(player => player.Id == id).Generation, Is.EqualTo(1));
        }

        Assert.That(lobby.Resume(8, GameVersion.Current.ToString(), 100, id, 1, "subject-1"), Is.True);
        Assert.That(lobby.Disconnect(8), Is.True);
        Assert.That(lobby.Return(0), Is.True);
        Assert.That(lobby.State.Players.Count, Is.EqualTo(7));
        Assert.That(lobby.FindPlayer("subject-1"), Is.Zero);
        Assert.That(lobby.Resume(9, GameVersion.Current.ToString(), 100, id, 2, "subject-1"), Is.False);
        Assert.That(lobby.Join(8, GameVersion.Current.ToString(), "Replacement"), Is.Not.Zero);
        Assert.That(lobby.Execute(2, LobbyCommand.Leave, 100, 100, SessionPhase.Lobby, false, []), Is.True);
        Assert.That(lobby.FindPlayer("subject-2"), Is.Zero);
        Assert.That(lobby.Disconnect(2), Is.False);
        Assert.Throws<ArgumentOutOfRangeException>(() => lobby.AdvanceTime(59));
    }

    /// <summary>Repeated reconnects preserve one arena identity, increment generations and never replay old intents.</summary>
    [Test]
    public void RepeatedArenaCyclesPreserveOnePlayer()
    {
        var lobby = new LobbyAuthority(100, "Host");
        ulong id = lobby.Join(10, GameVersion.Current.ToString(), "Client", "subject");
        lobby.SetReady(0, true);
        lobby.SetReady(10, true);
        lobby.Start(0);
        ulong peer = 10;
        for (ulong generation = 1; generation <= 100; generation++)
        {
            lobby.Disconnect(peer);
            Assert.That(lobby.Resume(++peer, GameVersion.Current.ToString(), 100, id, generation, "subject"), Is.True);
            Assert.That(lobby.State.Players.Count, Is.EqualTo(2));
            Assert.That(lobby.State.Match, Is.EqualTo(101));
            Assert.That(lobby.Peers.Count, Is.EqualTo(1));
            Assert.That(lobby.Resume(peer + 1, GameVersion.Current.ToString(), 100, id, generation, "subject"), Is.False);
        }

        Assert.That(lobby.Join(peer + 1, GameVersion.Current.ToString(), "New player", "different"), Is.Zero);
        Assert.That(lobby.Execute(peer, LobbyCommand.Leave, 99, 100, SessionPhase.Lobby, false, []), Is.False);
        Assert.That(lobby.Execute(peer, LobbyCommand.Leave, 100, 100, SessionPhase.Lobby, false, []), Is.True, "Intentional departure must survive a concurrent lobby-to-arena transition.");
        Assert.That(lobby.FindPlayer("subject"), Is.EqualTo(id));
        Assert.That(lobby.Return(0), Is.True);
        Assert.That(lobby.FindPlayer("subject"), Is.Zero);
    }

    /// <summary>Wire boundaries retain disconnected state and reject gameplay from retired generations.</summary>
    [Test]
    public void WireRetainsDisconnectedStateAndRejectsRetiredGeneration()
    {
        var lobby = new LobbyAuthority(100, "Host");
        ulong player = lobby.Join(10, GameVersion.Current.ToString(), "Client", "subject");
        lobby.SetReady(0, true);
        lobby.SetReady(10, true);
        Assert.That(lobby.Start(0, [10]), Is.True);
        lobby.Disconnect(10);
        var state = LobbyCodec.DecodeState(LobbyCodec.EncodeState(lobby.State, player));
        Assert.That(state.State.Players, Is.EqualTo(lobby.State.Players));
        byte[] bytes = ConnectionEnvelope.Encode(100, 1, [1, 2, 3]);
        Assert.That(ConnectionEnvelope.Decode(bytes, 100, 1), Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.Throws<ArgumentException>(() => ConnectionEnvelope.Decode(bytes, 100, 2));
        Assert.Throws<ArgumentException>(() => ConnectionEnvelope.Decode(bytes, 101, 1));
    }
}
