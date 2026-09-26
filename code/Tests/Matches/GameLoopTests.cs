using Trackstorm.Core.Matches;

namespace Trackstorm.Core.Tests.Matches;

/// <summary>Explicit post-sync lifecycle, authority, deterministic timing and mode completion contracts.</summary>
[TestFixture]
internal sealed class GameLoopTests
{
    [Test]
    public void GenericModeTimerStartsAtActiveAndModeReportsCompletionOnce()
    {
        using var loop = new GameLoop(true, 60);
        Assert.That(loop.Initialize(new(1, 500, [1])), Is.True);
        Assert.That(loop.StartCountdown(3), Is.True);
        for (ulong tick = 501; tick <= 503; tick++) Assert.That(loop.Advance(tick), Is.True);
        Assert.That(loop.State!.ActiveStartedAtTick, Is.EqualTo(503));
        Assert.That(loop.State.RemainingMatchTicks(503), Is.EqualTo(60));
        for (ulong tick = 504; tick <= 563; tick++) Assert.That(loop.Advance(tick), Is.True);
        Assert.That(loop.State.RemainingMatchTicks(563), Is.Zero);
        Assert.That(loop.State.Phase, Is.EqualTo(GameLoopPhase.Active), "Only mode completion selects an outcome.");
        Assert.That(loop.ReportCompletion(new("time-limit", 1)), Is.True);
        Assert.That(loop.ReportCompletion(new("time-limit", 1)), Is.False);
        Assert.That(loop.State.RemainingMatchTicks(0), Is.Zero);
    }
    /// <summary>Every phase is observable and gameplay is permitted only in Active.</summary>
    [Test]
    public void SynchronizedHandoffProgressesThroughEveryPhase()
    {
        var loop = new GameLoop(true);
        Assert.That(loop.State, Is.Null);
        Assert.That(loop.AllowsGameplay, Is.False);
        var context = new SynchronizedMatchContext(71, 100, [2, 1]);
        Assert.That(loop.Initialize(context), Is.True);
        Assert.That(loop.Context, Is.SameAs(context));
        Assert.That(loop.State!.Phase, Is.EqualTo(GameLoopPhase.Initialization));
        Assert.That(loop.AllowsGameplay, Is.False);
        Assert.That(loop.StartCountdown(3), Is.True);
        Assert.That(loop.State.Phase, Is.EqualTo(GameLoopPhase.Countdown));
        Assert.That(loop.State.CountdownAtTick, Is.EqualTo(103));
        Assert.That(loop.Advance(101), Is.True);
        Assert.That(loop.Advance(102), Is.True);
        Assert.That(loop.AllowsGameplay, Is.False);
        Assert.That(loop.Revision, Is.EqualTo(2));
        Assert.That(loop.Advance(103), Is.True);
        Assert.That(loop.AllowsGameplay, Is.True);
        Assert.That(loop.State.CountdownAtTick, Is.Null);
        Assert.That(loop.Revision, Is.EqualTo(3));

        var modeOutcome = new MatchOutcome("objective-completed", 2);
        Assert.That(loop.ReportCompletion(modeOutcome), Is.True);
        Assert.That(loop.State.Phase, Is.EqualTo(GameLoopPhase.Finished));
        Assert.That(loop.State.Outcome, Is.SameAs(modeOutcome));
        Assert.That(loop.AllowsGameplay, Is.False);
        Assert.That(loop.Revision, Is.EqualTo(4));
    }

    /// <summary>Clients cannot enter or advance any authoritative lifecycle phase.</summary>
    [Test]
    public void NonAuthorityCannotMutateLifecycle()
    {
        var client = new GameLoop(false);
        Assert.That(client.Initialize(new(1, 0, [1])), Is.False);
        Assert.That(client.StartCountdown(1), Is.False);
        Assert.That(client.Advance(1), Is.False);
        Assert.That(client.ReportCompletion(new("client-claim", 1)), Is.False);
        Assert.That(client.State, Is.Null);
        Assert.That(client.Context, Is.Null);
        Assert.That(client.Revision, Is.Zero);
        Assert.That(client.AllowsGameplay, Is.False);
    }

    /// <summary>Duplicate and invalid commands preserve the committed boundary and revision.</summary>
    [Test]
    public void InvalidTransitionsCannotSkipOrRestartPhases()
    {
        var loop = new GameLoop(true);
        Assert.That(loop.StartCountdown(1), Is.False);
        Assert.That(loop.Advance(1), Is.False);
        Assert.That(loop.ReportCompletion(new("too-early")), Is.False);
        Assert.That(loop.Initialize(new(1, 0, [1])), Is.True);
        GameLoopState initialized = loop.State!;
        Assert.That(loop.Initialize(new(2, 20, [2])), Is.False);
        Assert.That(loop.Advance(1), Is.False);
        Assert.That(loop.ReportCompletion(new("too-early")), Is.False);
        Assert.That(loop.StartCountdown(0), Is.False);
        Assert.That(loop.State, Is.SameAs(initialized));
        Assert.That(loop.Revision, Is.EqualTo(1));
        Assert.That(loop.StartCountdown(2), Is.True);
        GameLoopState countdown = loop.State!;
        Assert.That(loop.StartCountdown(10), Is.False);
        Assert.That(loop.Advance(0), Is.False);
        Assert.That(loop.Advance(2), Is.False);
        Assert.That(loop.ReportCompletion(new("too-early")), Is.False);
        Assert.That(loop.State, Is.SameAs(countdown));
        Assert.That(loop.Advance(1), Is.True);
        GameLoopState next = loop.State!;
        Assert.That(loop.Advance(1), Is.False);
        Assert.That(loop.State, Is.SameAs(next));
        Assert.That(loop.Advance(2), Is.True);
        Assert.That(loop.StartCountdown(2), Is.False);
        Assert.That(loop.Initialize(new(3, 2, [1])), Is.False);
    }

    /// <summary>Read-only UI timing cannot activate gameplay even when its timer reaches zero.</summary>
    [Test]
    public void CountdownProjectionNeverAdvancesAuthoritativeState()
    {
        var loop = new GameLoop(true);
        loop.Initialize(new(1, 50, [1]));
        loop.StartCountdown(2);
        GameLoopState state = loop.State!;
        Assert.That(state.RemainingCountdownTicks(50), Is.EqualTo(2));
        Assert.That(state.RemainingCountdownTicks(51), Is.EqualTo(1));
        Assert.That(state.RemainingCountdownTicks(52), Is.Zero);
        Assert.That(state.RemainingCountdownTicks(ulong.MaxValue), Is.Zero);
        Assert.That(loop.State, Is.SameAs(state));
        Assert.That(loop.AllowsGameplay, Is.False);
        Assert.That(loop.Advance(51), Is.True);
        Assert.That(loop.Advance(52), Is.True);
        Assert.That(loop.AllowsGameplay, Is.True);
        Assert.That(state.AllowsGameplay, Is.False);
    }

    /// <summary>A mode may finish without kills or a player winner; subsequent reports never replace its result.</summary>
    [Test]
    public void ModeCompletionIsStableAndEnteredExactlyOnce()
    {
        var loop = new GameLoop(true);
        loop.Initialize(new(1, 0, [1]));
        loop.StartCountdown(1);
        loop.Advance(1);
        Assert.That(loop.Advance(2), Is.True);
        var outcome = new MatchOutcome("time-limit-draw");
        Assert.That(loop.ReportCompletion(outcome), Is.True);
        GameLoopState final = loop.State!;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Assert.That(loop.ReportCompletion(outcome), Is.False);
            Assert.That(loop.ReportCompletion(new("different-result", 1)), Is.False);
            Assert.That(loop.Advance(3), Is.False);
            Assert.That(loop.StartCountdown(1), Is.False);
            Assert.That(loop.Initialize(new(2, 0, [1])), Is.False);
        }

        Assert.That(loop.State, Is.SameAs(final));
        Assert.That(final.Tick, Is.EqualTo(2));
        Assert.That(final.Outcome!.Winner, Is.Null);
        Assert.That(loop.Revision, Is.EqualTo(4));
    }

    /// <summary>Representable edge deadlines work; overflow and malformed snapshots cannot partially commit.</summary>
    [Test]
    public void BoundaryValidationRejectsInvalidStateAndOverflow()
    {
        var loop = new GameLoop(true);
        loop.Initialize(new(1, ulong.MaxValue - 1, [1]));
        Assert.That(loop.StartCountdown(2), Is.False);
        Assert.That(loop.State!.Phase, Is.EqualTo(GameLoopPhase.Initialization));
        Assert.That(loop.StartCountdown(1), Is.True);
        Assert.That(loop.Advance(ulong.MaxValue), Is.True);
        Assert.That(loop.Advance(0), Is.False);
        Assert.That(loop.ReportCompletion(new("completed")), Is.True);
        Assert.Throws<ArgumentException>(() => new GameLoopState(0, (GameLoopPhase)99));
        Assert.Throws<ArgumentException>(() => new GameLoopState(1, GameLoopPhase.Countdown, 1));
        Assert.Throws<ArgumentException>(() => new GameLoopState(1, GameLoopPhase.Countdown));
        Assert.Throws<ArgumentException>(() => new GameLoopState(1, GameLoopPhase.Active, 2));
        Assert.Throws<ArgumentException>(() => new GameLoopState(1, GameLoopPhase.Finished));
        Assert.Throws<ArgumentException>(() => new GameLoopState(1, GameLoopPhase.Active, outcome: new("invalid")));
        Assert.Throws<ArgumentException>(() => new MatchOutcome(" "));
        Assert.Throws<ArgumentException>(() => new MatchOutcome(new string('x', 65)));
        Assert.Throws<ArgumentException>(() => new MatchOutcome("bad\nreason"));
        Assert.Throws<ArgumentException>(() => new MatchOutcome("invalid-winner", 0));
    }

    /// <summary>The handoff validates and detaches its roster instead of retaining a caller-owned array.</summary>
    [Test]
    public void SynchronizedContextIsValidatedAndImmutable()
    {
        ulong[] participants = [2, 1];
        var context = new SynchronizedMatchContext(8, 17, participants);
        participants[0] = 99;
        Assert.That(context.Participants, Is.EqualTo(new ulong[] { 1, 2 }));
        Assert.Throws<ArgumentException>(() => new SynchronizedMatchContext(0, 0, [1]));
        Assert.Throws<ArgumentException>(() => new SynchronizedMatchContext(1, 0, []));
        Assert.Throws<ArgumentException>(() => new SynchronizedMatchContext(1, 0, [0]));
        Assert.Throws<ArgumentException>(() => new SynchronizedMatchContext(1, 0, [1, 1]));
        Assert.Throws<ArgumentException>(() => new SynchronizedMatchContext(1, 0, Enumerable.Range(1, 9).Select(value => (ulong)value)));
    }
}
