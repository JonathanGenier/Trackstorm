using Trackstorm.Core.Sessions;

namespace Trackstorm.Core.Tests.Sessions;

[TestFixture]
internal sealed class RematchTests
{
    [Test]
    public void RestartRetainsConnectedSessionAndReleasesOldReservationsAcrossCycles()
    {
        var lobby = new LobbyAuthority(100, "Host");
        lobby.SelectMap(0, MatchMap.OldMap);
        lobby.Join(10, GameVersion.Current.ToString(), "Connected", "connected");
        ulong absent = lobby.Join(20, GameVersion.Current.ToString(), "Absent", "absent");
        foreach (ulong peer in new ulong[] { 0, 10, 20 }) lobby.SetReady(peer, true);
        Assert.That(lobby.Start(0, [10, 20]), Is.True);
        lobby.Disconnect(20);
        var finalRoster = lobby.State;
        for (int cycle = 0; cycle < 3; cycle++)
        {
            var previous = lobby.State;
            Assert.That(lobby.Restart(0, [10]), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(lobby.State.Session, Is.EqualTo(previous.Session));
                Assert.That(lobby.State.Match, Is.EqualTo(previous.Match + 1));
                Assert.That(lobby.State.Phase, Is.EqualTo(SessionPhase.Arena));
                Assert.That(lobby.State.Map, Is.EqualTo(MatchMap.OldMap));
                Assert.That(lobby.State.Players.Select(player => player.Id), Is.EqualTo(new ulong[] { 1, 2 }));
                Assert.That(lobby.State.Players.All(player => !player.Ready && player.Connected), Is.True);
                Assert.That(lobby.State.Departed, Is.Empty);
                Assert.That(lobby.Resume(30, GameVersion.Current.ToString(), 100, absent, 1, "absent"), Is.False);
            });
            var replica = new LobbyReplica();
            Assert.That(replica.Accept(previous, 2, 10, 10), Is.True);
            Assert.That(replica.Accept(LobbyCodec.DecodeState(LobbyCodec.EncodeState(lobby.State, 2)).State, 2, 10, 10), Is.True);
        }
        Assert.That(finalRoster.Players.Single(player => player.Id == absent).Connected, Is.False);
    }

    [Test]
    public void RestartRejectsRemoteHostLobbyAndUnadmittedTransportWithoutMutation()
    {
        var lobby = new LobbyAuthority(100, "Host");
        Assert.That(lobby.Restart(0, []), Is.False);
        lobby.SetReady(0, true);
        lobby.Start(0, []);
        var previous = lobby.State;
        Assert.That(lobby.Restart(9, []), Is.False);
        Assert.That(lobby.Restart(0, [9]), Is.False);
        Assert.That(lobby.State, Is.SameAs(previous));
    }
}
