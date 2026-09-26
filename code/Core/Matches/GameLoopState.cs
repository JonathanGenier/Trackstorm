namespace Trackstorm.Core.Matches;

/// <summary>Immutable engine-independent phase boundary shared by lifecycle authority and legacy scoring.</summary>
public sealed class GameLoopState
{
    /// <summary>Validates a complete phase boundary for restoration or read-only presentation.</summary>
    /// <param name="tick">Latest accepted authoritative tick.</param>
    /// <param name="phase">Current phase.</param>
    /// <param name="countdownAtTick">Exclusive countdown deadline, only during Countdown.</param>
    /// <param name="outcome">Stable completion, only during Finished.</param>
    /// <param name="activeStartedAtTick">Retained Active entry tick.</param>
    /// <param name="durationTicks">Mode time limit, or zero for untimed modes.</param>
    /// <param name="recoveryElapsedTicks">Authoritative elapsed time accounted for during checkpoint recovery.</param>
    public GameLoopState(ulong tick, GameLoopPhase phase, ulong? countdownAtTick = null, MatchOutcome? outcome = null, ulong? activeStartedAtTick = null, ulong durationTicks = 0, ulong recoveryElapsedTicks = 0)
    {
        if (!Enum.IsDefined(phase) || (phase == GameLoopPhase.Countdown) != countdownAtTick.HasValue ||
            (countdownAtTick.HasValue && countdownAtTick <= tick) || (phase == GameLoopPhase.Finished) != (outcome is not null) ||
            (activeStartedAtTick.HasValue && (phase is GameLoopPhase.Initialization or GameLoopPhase.Countdown || activeStartedAtTick > tick || durationTicks > ulong.MaxValue - activeStartedAtTick.Value)))
        {
            throw new ArgumentException("Invalid Game Loop boundary.");
        }

        Tick = tick;
        Phase = phase;
        CountdownAtTick = countdownAtTick;
        Outcome = outcome;
        ActiveStartedAtTick = activeStartedAtTick;
        DurationTicks = durationTicks;
        RecoveryElapsedTicks = recoveryElapsedTicks;
    }

    /// <summary>Latest accepted fixed tick; freezes on completion.</summary>
    public ulong Tick { get; }
    /// <summary>Authoritative lifecycle phase.</summary>
    public GameLoopPhase Phase { get; }
    /// <summary>Absolute authoritative activation tick.</summary>
    public ulong? CountdownAtTick { get; }
    /// <summary>Final mode outcome; null until completion.</summary>
    public MatchOutcome? Outcome { get; }
    /// <summary>Authoritative Active entry boundary, retained through Finished and recovery.</summary>
    public ulong? ActiveStartedAtTick { get; }
    /// <summary>Mode-configured Active duration; zero means no time limit.</summary>
    public ulong DurationTicks { get; }
    /// <summary>Consumed match ticks across authority recovery pauses and rollback.</summary>
    public ulong RecoveryElapsedTicks { get; }
    /// <summary>Authoritative time remaining; presentation cannot advance phases.</summary>
    public ulong RemainingMatchTicks(ulong authoritativeTick)
    {
        if (Phase == GameLoopPhase.Finished) return 0;
        if (ActiveStartedAtTick is not ulong start) return DurationTicks;
        ulong remaining = DurationTicks - Math.Min(DurationTicks, authoritativeTick > start ? authoritativeTick - start : 0);
        return remaining - Math.Min(remaining, RecoveryElapsedTicks);
    }
    /// <summary>Core participation policy; readiness and vehicle-life checks remain additional gates.</summary>
    public bool AllowsGameplay => Phase == GameLoopPhase.Active;

    /// <summary>Read-only timer projection; reaching zero never advances the phase.</summary>
    /// <param name="authoritativeTick">Last observed host tick.</param>
    /// <returns>Ticks remaining, or zero outside Countdown.</returns>
    public ulong RemainingCountdownTicks(ulong authoritativeTick) => CountdownAtTick is ulong deadline && authoritativeTick < deadline ? deadline - authoritativeTick : 0;

    /// <summary>Shared transition rule; caller commits the returned candidate atomically.</summary>
    /// <param name="duration">Positive authoritative countdown duration.</param>
    /// <returns>Countdown candidate.</returns>
    internal GameLoopState StartCountdown(ulong duration)
    {
        if (Phase != GameLoopPhase.Initialization || duration == 0)
        {
            throw new InvalidOperationException("Countdown requires initialization and a positive duration.");
        }

        return new(Tick, GameLoopPhase.Countdown, checked(Tick + duration), durationTicks: DurationTicks, recoveryElapsedTicks: RecoveryElapsedTicks);
    }

    /// <summary>Evaluates a later authoritative tick without mutating the committed boundary.</summary>
    /// <param name="tick">Later fixed tick, validated sequentially by the owning simulation.</param>
    /// <returns>Candidate phase at that tick.</returns>
    internal GameLoopState Advance(ulong tick)
    {
        if (Phase is not (GameLoopPhase.Countdown or GameLoopPhase.Active) || tick <= Tick)
        {
            throw new InvalidOperationException("Only a running match can advance to a later tick.");
        }

        bool counting = Phase == GameLoopPhase.Countdown && tick < CountdownAtTick;
        return new(tick, counting ? GameLoopPhase.Countdown : GameLoopPhase.Active, counting ? CountdownAtTick : null,
            activeStartedAtTick: counting ? null : ActiveStartedAtTick ?? CountdownAtTick ?? Tick, durationTicks: DurationTicks, recoveryElapsedTicks: RecoveryElapsedTicks);
    }

    /// <summary>Applies a mode-reported outcome only to Active.</summary>
    /// <param name="outcome">Authoritative mode result.</param>
    /// <returns>Terminal candidate.</returns>
    internal GameLoopState Finish(MatchOutcome outcome)
    {
        if (!AllowsGameplay)
        {
            throw new InvalidOperationException("Only an active match can finish.");
        }

        return new(Tick, GameLoopPhase.Finished, outcome: outcome, activeStartedAtTick: ActiveStartedAtTick, durationTicks: DurationTicks, recoveryElapsedTicks: RecoveryElapsedTicks);
    }
}
