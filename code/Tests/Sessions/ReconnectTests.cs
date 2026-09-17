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
        var lobby = new LobbyAuthority(100, "Host", 60);
        ulong player = lobby.Join(10, GameVersion.Current.ToString(), "Original", "subject-a");
        lobby.SetReady(0, true);
        lobby.SetReady(10, true);
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
        Assert.That(lobby.State.Players.Count, Is.EqualTo(2), "Successful resume cancels expiry.");
    }

    /// <summary>The exact deadline releases capacity once; explicit leave immediately revokes resume authority.</summary>
    [Test]
    public void ExpiryAndIntentionalLeaveReleaseSlotsAndAuthorization()
    {
        var lobby = new LobbyAuthority(100, "Host", 60);
        for (ulong peer = 1; peer < 8; peer++)
        {
            lobby.Join(peer, GameVersion.Current.ToString(), "Player", "subject-" + peer);
        }

        ulong id = lobby.PlayerId(1);
        lobby.Disconnect(1);
        Assert.That(lobby.Join(8, GameVersion.Current.ToString(), "Full"), Is.Zero);
        lobby.AdvanceTime(59);
        Assert.That(lobby.State.Players.Count, Is.EqualTo(8));
        lobby.AdvanceTime(60);
        ulong revision = lobby.State.Revision;
        lobby.AdvanceTime(61);
        Assert.That(lobby.State.Revision, Is.EqualTo(revision));
        Assert.That(lobby.Resume(8, GameVersion.Current.ToString(), 100, id, 1, "subject-1"), Is.False);
        Assert.That(lobby.FindPlayer("subject-1"), Is.Zero);
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
        var lobby = new LobbyAuthority(100, "Host", 60);
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
        Assert.That(lobby.FindPlayer("subject"), Is.Zero);
    }

    /// <summary>Wire boundaries retain grace state and reject gameplay from retired generations.</summary>
    [Test]
    public void WireRetainsDisconnectedStateAndRejectsRetiredGeneration()
    {
        var lobby = new LobbyAuthority(100, "Host");
        ulong player = lobby.Join(10, GameVersion.Current.ToString(), "Client", "subject");
        lobby.Disconnect(10);
        var state = LobbyCodec.DecodeState(LobbyCodec.EncodeState(lobby.State, player));
        Assert.That(state.State.Players, Is.EqualTo(lobby.State.Players));
        byte[] bytes = ConnectionEnvelope.Encode(100, 1, [1, 2, 3]);
        Assert.That(ConnectionEnvelope.Decode(bytes, 100, 1), Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.Throws<ArgumentException>(() => ConnectionEnvelope.Decode(bytes, 100, 2));
        Assert.Throws<ArgumentException>(() => ConnectionEnvelope.Decode(bytes, 101, 1));
    }
}
