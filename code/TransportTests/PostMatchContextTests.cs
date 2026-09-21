using Trackstorm.Client.Networking;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Transport.Tests;

[TestFixture]
internal sealed class PostMatchContextTests
{
    [Test]
    public void HandoffKeepsModeOrderAndNamesWhileConnectivityChanges()
    {
        var roster = new LobbySnapshot(100, 2, 101, SessionPhase.Arena,
            [new SessionPlayer(1, "Host", false), new SessionPlayer(2, "Winner", false)]);
        // The mode winner deliberately has fewer kills: presentation must never rank by kills.
        var results = new FinalMatchResults(10, new MatchOutcome("objective", 2),
            [new FinalMatchStanding(2, 1, 0, 0, 5, 1), new FinalMatchStanding(1, 2, 0, 9, 0, 0)]);
        var context = new PostMatchContext(roster, results);
        var offline = new LobbySnapshot(100, 3, 101, SessionPhase.Arena,
            [new SessionPlayer(1, "Host", false), new SessionPlayer(2, "Winner", false, false)]);
        Assert.That(context.Results, Is.SameAs(results));
        Assert.That(context.Results.Standings[0].PlayerId, Is.EqualTo(2));
        Assert.That(context.Participant(2, offline).Connected, Is.False);
        Assert.That(context.Participant(2, roster).Connected, Is.True);
        Assert.That(context.Participant(2, null).Name, Is.EqualTo("Winner"));
        Assert.That(context.Matches(new LobbySnapshot(100, 4, 102, SessionPhase.Arena, roster.Players)), Is.False);
        Assert.That(context.Matches(new LobbySnapshot(200, 4, 201, SessionPhase.Arena, roster.Players)), Is.False);
        Assert.That(context.Matches(new LobbySnapshot(100, 4, 101, SessionPhase.Lobby, roster.Players)), Is.False);
    }
}
