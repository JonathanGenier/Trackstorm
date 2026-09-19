using Trackstorm.Core.Matches;

namespace Trackstorm.Core.Tests.Matches;

/// <summary>Core owns deterministic rank independently of roster enumeration and presentation.</summary>
[TestFixture]
internal sealed class MatchRankingTests
{
    /// <summary>Performance and identity resolve ties; current participants alone receive contiguous ranks.</summary>
    [Test]
    public void RanksKillsThenDeathsThenIdentityAcrossEightPlayers()
    {
        var scores = Enumerable.Range(1, 8).Select(id => new PlayerScore((ulong)id, id is 2 or 3 ? 3 : id == 4 ? 2 : 0, id == 3 ? 4 : 3, 0, 4));
        var match = new MatchState(10, 1, 5, MatchPhase.Active, null, null, scores);
        ulong[] ids = Enumerable.Range(1, 8).Select(id => (ulong)id).ToArray();
        var ranks = MatchRanking.Create(match, ids.Reverse());
        Assert.That(ranks.Select(row => row.PlayerId), Is.EqualTo(new ulong[] { 2, 3, 4, 1, 5, 6, 7, 8 }));
        Assert.That(ranks.Select(row => row.Rank), Is.EqualTo(Enumerable.Range(1, 8)));
        Assert.That(MatchRanking.Create(match, ids), Is.EqualTo(ranks));
        Assert.That(MatchRanking.Create(match, new ulong[] { 1, 3 }).Select(row => row.PlayerId), Is.EqualTo(new ulong[] { 3, 1 }));
    }

    /// <summary>Ranks recompute from accepted match revisions and retain winner identity.</summary>
    [Test]
    public void UpdatedTotalsAndWinnerDriveRanking()
    {
        var active = new MatchState(10, 1, 5, MatchPhase.Active, null, null, new[] { new PlayerScore(1, 1, 5, 0, 5), new PlayerScore(2, 0, 5, 0, 5) });
        var finished = new MatchState(11, 2, 5, MatchPhase.Finished, null, 2, new[] { new PlayerScore(1, 1, 5, 0, 5), new PlayerScore(2, 5, 5, 1, 5) });
        Assert.That(MatchRanking.Create(active, new ulong[] { 1, 2 })[0].PlayerId, Is.EqualTo(1));
        Assert.That(MatchRanking.Create(finished, new ulong[] { 1, 2 })[0].PlayerId, Is.EqualTo(2));
        Assert.Throws<ArgumentException>(() => MatchRanking.Create(active, new ulong[] { 1, 1 }));
        Assert.Throws<ArgumentException>(() => MatchRanking.Create(active, Enumerable.Range(1, MatchState.MaximumPlayers + 1).Select(id => (ulong)id)));
    }
}
