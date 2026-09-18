using Trackstorm.Core.Sessions;

namespace Trackstorm.Core.Tests.Sessions;

/// <summary>Fresh arena identity, serialized admission races and pending-bootstrap cleanup.</summary>
[TestFixture]
internal sealed class ActiveJoinTests
{
    /// <summary>Either arrival order has one final-slot winner and never reuses a retained identity.</summary>
    /// <param name="reverse">Reverse the competing transport arrival order.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void FinalSlotRaceIncludesRetainedAndPendingPlayers(bool reverse)
    {
        var lobby = Arena(7);
        lobby.Disconnect(2);
        var retained = lobby.State.Players.Single(player => player.Id == 2);
        ulong[] peers = reverse ? [91, 90] : [90, 91];
        var results = new List<ulong>();
        foreach (ulong peer in peers)
        {
            results.Add(lobby.Join(peer, "New", $"subject-{peer}"));
            Assert.That(lobby.State.Players.Count, Is.LessThanOrEqualTo(8));
        }

        Assert.That(results.Count(id => id != 0), Is.EqualTo(1));
        Assert.That(results[0], Is.EqualTo(8));
        Assert.That(lobby.Join(peers[0], "Retry", $"subject-{peers[0]}"), Is.EqualTo(8));
        Assert.That(lobby.State.Players.Count, Is.EqualTo(8));
        Assert.That(lobby.State.Players.Single(player => player.Id == 2), Is.EqualTo(retained));
        Assert.That(lobby.Join(99, "Impersonation", "subject-2"), Is.Zero);
        Assert.That(lobby.CompleteJoin(peers[0]), Is.True);
        Assert.That(lobby.CompleteJoin(peers[0]), Is.False);
        lobby.Disconnect(peers[0]);
        Assert.That(lobby.State.Players.Count, Is.EqualTo(8), "Activated participants retain their own slot.");
    }

    /// <summary>Partial joins have no reconnect reservation; retry allocates another monotonic identity.</summary>
    /// <param name="timeout">Expire the pending handshake instead of losing its transport.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void InterruptedBootstrapReleasesSubjectAndCapacity(bool timeout)
    {
        var lobby = Arena(2);
        ulong id = lobby.Join(50, "New", "new-subject");
        Assert.That(lobby.IsPendingJoin(50), Is.True);
        Assert.That(lobby.Capture("host").State.Players.Any(player => player.Id == id), Is.False, "Migration cannot retain a participant without a vehicle.");
        if (timeout)
        {
            lobby.AdvanceTime(899);
            Assert.That(lobby.IsPendingJoin(50), Is.True);
            lobby.AdvanceTime(900);
        }
        else
        {
            lobby.Disconnect(50);
        }

        Assert.That(lobby.FindPlayer("new-subject"), Is.Zero);
        Assert.That(lobby.State.Players.Count, Is.EqualTo(2));
        Assert.That(lobby.Events.Entries.Any(entry => entry.Actor == id && entry.Kind is "Joined" or "Disconnected"), Is.False, "Uncommitted players produce no presence feed entries.");
        Assert.That(lobby.Resume(51, 100, id, 1, "new-subject"), Is.False);
        Assert.That(lobby.Join(51, "Retry", "new-subject"), Is.GreaterThan(id));
    }

    /// <summary>Terminal/closing eligibility rejects without changing any roster boundary.</summary>
    [Test]
    public void ClosedAdmissionDoesNotMutateRoster()
    {
        var lobby = Arena(2);
        lobby.AdmissionOpen = false;
        var before = lobby.State;
        Assert.That(lobby.Join(50, "New", "new-subject"), Is.Zero);
        Assert.That(lobby.State, Is.SameAs(before));
    }

    /// <summary>All eight connected participants count just as retained reservations do.</summary>
    [Test]
    public void FullActiveArenaRejectsNinthWithoutMutation()
    {
        var lobby = Arena(8);
        var before = lobby.State;
        Assert.That(lobby.Join(99, "Ninth", "ninth"), Is.Zero);
        Assert.That(lobby.State, Is.SameAs(before));
        Assert.That(lobby.State.Players.Count, Is.EqualTo(8));
    }

    /// <summary>Failure controls expose only stable reasons and malformed data remains a protocol rejection.</summary>
    [Test]
    public void RejectionReasonsAreBoundedAndMalformedTypesAreRejected()
    {
        Assert.That(LobbyCodec.DecodeRejection(LobbyCodec.EncodeRejection("Session full")), Is.EqualTo("Session full"));
        Assert.That(LobbyCodec.DecodeRejection(LobbyCodec.EncodeRejection("untrusted details")), Is.EqualTo("Resume rejected"));
        var packet = LobbyCodec.EncodeRejection("x");
        byte[] malformed = [.. packet.Take(4), (byte)'1'];
        Assert.Throws<ArgumentException>(() => LobbyCodec.DecodeRejection(malformed));
    }

    private static LobbyAuthority Arena(int players)
    {
        var lobby = new LobbyAuthority(100, "Host");
        lobby.SetReady(0, true);
        for (ulong peer = 2; peer <= (ulong)players; peer++)
        {
            lobby.Join(peer, "Player", $"subject-{peer}");
            lobby.SetReady(peer, true);
        }

        Assert.That(lobby.Start(0), Is.True);
        return lobby;
    }
}
