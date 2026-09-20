using Trackstorm.Core.Matches;

namespace Trackstorm.Core.Tests.Matches;

/// <summary>Detached final results and explicit disposal/reconstruction of match lifecycle owners.</summary>
[TestFixture]
internal sealed class FinalMatchResultsTests
{
    /// <summary>Mode-supplied order is frozen once and survives disposal while three new handoffs start cleanly.</summary>
    [Test]
    public void RepeatedMatchesFreezeResultsAndRequireFreshOwners()
    {
        var retained = new List<FinalMatchResults>();
        for (ulong generation = 1; generation <= 3; generation++)
        {
            using var loop = new GameLoop(true);
            Assert.That(loop.FinalResults, Is.Null);
            Assert.That(loop.Revision, Is.Zero);
            Assert.That(loop.Initialize(new(generation, 0, [1, 2])), Is.True);
            Assert.That(loop.State!.Outcome, Is.Null);
            Assert.That(loop.State.CountdownAtTick, Is.Null);
            Assert.That(loop.StartCountdown(generation), Is.True);
            for (ulong tick = 1; tick <= generation; tick++)
            {
                loop.Advance(tick);
            }

            FinalMatchStanding[] rows = [new(2, 1, 0, 0, 0), new(1, 2, 0, 0, 0)];
            var result = new FinalMatchResults(generation, new("objective", 2), rows);
            rows[0] = new(99, 1, 99, 99, 1);
            Assert.That(loop.ReportFinalResults(new FinalMatchResults(generation + 1, new("wrong-tick"), [])), Is.False);
            Assert.That(loop.FinalResults, Is.Null);
            Assert.That(loop.ReportFinalResults(result), Is.True);
            Assert.That(loop.ReportCompletion(new MatchOutcome("competing", 1)), Is.False);
            Assert.That(loop.Advance(generation + 1), Is.False);
            Assert.That(loop.Revision, Is.EqualTo(4));
            Assert.That(loop.FinalResults, Is.SameAs(result));
            Assert.That(result.Standings[0].PlayerId, Is.EqualTo(2), "The lifecycle must not recalculate mode ranking from kills.");
            Assert.Throws<NotSupportedException>(() => ((IList<FinalMatchStanding>)result.Standings)[0] = rows[0]);
            retained.Add(result);
            loop.Dispose();
            loop.Dispose();
            Assert.That(loop.State, Is.Null);
            Assert.That(loop.Context, Is.Null);
            Assert.That(loop.FinalResults, Is.Null);
            Assert.That(loop.Revision, Is.Zero);
            Assert.That(loop.AllowsGameplay, Is.False);
            Assert.That(loop.Initialize(new(generation + 1, 0, [1])), Is.False);
            Assert.That(loop.StartCountdown(1), Is.False);
            Assert.That(loop.ReportFinalResults(result), Is.False);
        }

        Assert.That(retained.Select(result => result.Tick), Is.EqualTo(new ulong[] { 1, 2, 3 }));
        Assert.That(retained.All(result => result.Outcome.Winner == 2 && result.Standings[0].PlayerId == 2), Is.True);
    }

    /// <summary>Disposal also cancels incomplete lifecycles, with no reusable timer or phase remaining.</summary>
    /// <param name="phase">Phase to abandon.</param>
    [TestCase(GameLoopPhase.Initialization)]
    [TestCase(GameLoopPhase.Countdown)]
    [TestCase(GameLoopPhase.Active)]
    public void DisposeBeforeFinishClearsHandoffAndCountdown(GameLoopPhase phase)
    {
        var loop = new GameLoop(true);
        loop.Initialize(new(1, 0, [1]));
        if (phase != GameLoopPhase.Initialization)
        {
            loop.StartCountdown(1);
        }

        if (phase == GameLoopPhase.Active)
        {
            loop.Advance(1);
        }

        loop.Dispose();
        Assert.That(loop.State, Is.Null);
        Assert.That(loop.Context, Is.Null);
        Assert.That(loop.Advance(1), Is.False);
        Assert.That(loop.ReportCompletion(new MatchOutcome("late-callback")), Is.False);
    }

    /// <summary>Complete codecs restore identical ready-to-render statistics, including departed score identities.</summary>
    [Test]
    public void CombatResultsUseAllFrozenScoresAndRoundTripWithoutUiRanking()
    {
        PlayerScore[] scores = [new(4, 0, 1, 0, 1), new(2, 1, 0, 1, 0), new(3, 0, 0, 0, 0)];
        var match = new MatchState(40, 8, 1, MatchPhase.Finished, null, 2, scores);
        scores[0] = new(99, 0, 0, 0, 0);
        FinalMatchResults result = match.FinalResults!;
        Assert.That(result.Standings, Is.EqualTo(new FinalMatchStanding[] { new(2, 1, 1, 0, 1), new(3, 2, 0, 0, 0), new(4, 3, 0, 1, 0) }));
        Assert.That(match.FinalResults, Is.SameAs(result));
        var restored = MatchCodec.Decode(MatchCodec.Encode(7, match)).State.FinalResults!;
        Assert.That(restored.Tick, Is.EqualTo(40));
        Assert.That(restored.Outcome, Is.EqualTo(result.Outcome));
        Assert.That(restored.Standings, Is.EqualTo(result.Standings));
        Assert.That(new MatchState(0, 0, 1, MatchPhase.Waiting, null, null, []).FinalResults, Is.Null);
    }

    /// <summary>Malformed final rows fail before any lifecycle commit.</summary>
    [Test]
    public void InvalidResultsAreRejected()
    {
        var outcome = new MatchOutcome("objective", 1);
        Assert.Throws<ArgumentException>(() => new FinalMatchResults(0, outcome, [new(1, 1, 0, 0, 0), new(1, 2, 0, 0, 0)]));
        Assert.Throws<ArgumentException>(() => new FinalMatchResults(0, outcome, [new(1, 2, 0, 0, 0)]));
        Assert.Throws<ArgumentException>(() => new FinalMatchResults(0, outcome, [new(2, 1, 0, 0, 0)]));
        Assert.Throws<ArgumentException>(() => new FinalMatchResults(0, outcome, [new(1, 1, -1, 0, 0)]));
        Assert.Throws<ArgumentException>(() => new FinalMatchResults(0, outcome, [new(0, 1, 0, 0, 0)]));
        using var client = new GameLoop(false);
        Assert.That(client.ReportFinalResults(new FinalMatchResults(0, outcome, [])), Is.False);
        Assert.That(client.FinalResults, Is.Null);
    }
}
