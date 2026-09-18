using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Core.Tests.Sessions;

/// <summary>Reservation release retires authorization and capacity independently from match history.</summary>
[TestFixture]
internal sealed class ReservationDecisionTests
{
    /// <summary>Only the exact authenticated disconnected assignment can be inspected or released.</summary>
    [Test]
    public void ReleasePreservesHistoryAndPermanentlyRetiresOldResume()
    {
        var lobby = new LobbyAuthority(100, "Host");
        for (ulong peer = 1; peer < 8; peer++)
        {
            lobby.Join(peer, "Player " + peer, "subject-" + peer);
            lobby.SetReady(peer, true);
        }

        lobby.SetReady(0, true);
        lobby.Start(0);
        Assert.That(lobby.Abandon(100, 2, 1, "subject-1"), Is.False);
        lobby.Disconnect(1);
        ulong revision = lobby.State.Revision;
        Assert.That(lobby.HasReservation(100, 2, 1, "subject-1"), Is.True);
        Assert.That(lobby.State.Revision, Is.EqualTo(revision), "Inspection cannot mutate the assignment.");
        Assert.That(lobby.Abandon(101, 2, 1, "subject-1"), Is.False);
        Assert.That(lobby.Abandon(100, 2, 2, "subject-1"), Is.False);
        Assert.That(lobby.Abandon(100, 2, 1, "subject-2"), Is.False);
        Assert.That(lobby.CanJoin, Is.False);
        Assert.That(lobby.Abandon(100, 2, 1, "subject-1"), Is.True);
        Assert.That(lobby.Abandon(100, 2, 1, "subject-1"), Is.False);
        Assert.That(lobby.State.Revision, Is.EqualTo(revision + 1));
        Assert.That(lobby.State.Departed, Is.EqualTo(new[] { new MatchParticipant(2, "Player 1") }));
        Assert.That(lobby.FindPlayer("subject-1"), Is.Zero);
        Assert.That(lobby.Resume(20, 100, 2, 1, "subject-1"), Is.False);
        Assert.That(lobby.Join(20, "New identity", "subject-1"), Is.EqualTo(9));
        Assert.That(lobby.State.Departed.Count, Is.EqualTo(1));
        Assert.That(lobby.Return(0), Is.True);
        Assert.That(lobby.State.Departed, Is.Empty);
    }

    /// <summary>Maximum Unicode participant history survives serialization and migration without granting subjects.</summary>
    [Test]
    public void BoundedHistorySurvivesCodecAndAuthorityContinuation()
    {
        string name = string.Concat(Enumerable.Repeat("𐐀", PlayerName.MaximumLength));
        var players = new[] { new SessionPlayer(1, name, false), new SessionPlayer(2, name, false) };
        var history = Enumerable.Range(3, 254).Select(id => new MatchParticipant((ulong)id, name)).ToArray();
        var state = new LobbySnapshot(100, 9, 101, SessionPhase.Arena, players, departed: history);
        byte[] bytes = LobbyCodec.EncodeState(state, 2);
        Assert.That(bytes.Length, Is.LessThanOrEqualTo(LobbyCodec.MaximumBytes));
        var decoded = LobbyCodec.DecodeState(bytes).State;
        Assert.That(decoded.Departed, Is.EqualTo(history));
        var capture = new LobbyRestoreState(decoded, 100, 256, new Dictionary<ulong, string> { [1] = "host", [2] = "survivor" });
        var migrated = LobbyAuthority.Restore(capture, 2, 2);
        Assert.That(migrated.State.Departed, Is.EqualTo(history));
        Assert.That(migrated.CanJoin, Is.False, "The existing match lifetime participant bound remains enforced.");
        Assert.That(migrated.HasReservation(100, 3, 1, "survivor"), Is.False);
        Assert.Throws<ArgumentException>(() => new LobbyRestoreState(decoded, 100, 2, capture.Subjects));
        var arena = new HostVehicleSession(101);
        arena.JoinPlayer(10, 2);
        var match = new MatchState(0, 1, arena.World.State.Match!.KillTarget, MatchPhase.Waiting, null, null, Enumerable.Range(1, 256).Select(id => new PlayerScore((ulong)id, 0, 0, 0, 0)));
        var resume = new ResumeCheckpoint(new ItemPublication(1, arena.Snapshot(), arena.Items.Slots, arena.Items.Missiles, []), match, null);
        var checkpoint = new MigrationCheckpoint(1, capture, resume, arena.CaptureAuthority());
        byte[] migrationBytes = MigrationCheckpointCodec.Encode(checkpoint);
        Assert.That(migrationBytes.Length, Is.LessThanOrEqualTo(MigrationCheckpointCodec.MaximumBytes));
        Assert.That(MigrationCheckpointCodec.Decode(migrationBytes).Lobby.State.Departed, Is.EqualTo(history));
        Assert.Throws<ArgumentException>(() => LobbyCodec.DecodeState(bytes[..^1]));
        Assert.Throws<ArgumentException>(() => LobbyCodec.DecodeState(bytes.Concat(new byte[] { 0 }).ToArray()));
        bytes[2]--;
        Assert.Throws<ArgumentException>(() => LobbyCodec.DecodeState(bytes));
    }
}
