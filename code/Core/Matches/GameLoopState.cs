namespace Trackstorm.Core.Matches;

/// <summary>Immutable engine-independent phase boundary shared by lifecycle authority and legacy scoring.</summary>
public sealed class GameLoopState
{
    /// <summary>Validates a complete phase boundary for restoration or read-only presentation.</summary>
    /// <param name="tick">Latest accepted authoritative tick.</param>
    /// <param name="phase">Current phase.</param>
    /// <param name="countdownAtTick">Exclusive countdown deadline, only during Countdown.</param>
    /// <param name="outcome">Stable completion, only during Finished.</param>
    public GameLoopState(ulong tick, GameLoopPhase phase, ulong? countdownAtTick = null, MatchOutcome? outcome = null)
    {
        if (!Enum.IsDefined(phase) || (phase == GameLoopPhase.Countdown) != countdownAtTick.HasValue ||
            (countdownAtTick.HasValue && countdownAtTick <= tick) || (phase == GameLoopPhase.Finished) != (outcome is not null))
        {
            throw new ArgumentException("Invalid Game Loop boundary.");
        }

        Tick = tick;
        Phase = phase;
        CountdownAtTick = countdownAtTick;
        Outcome = outcome;
    }

    /// <summary>Latest accepted fixed tick; freezes on completion.</summary>
    public ulong Tick { get; }
    /// <summary>Authoritative lifecycle phase.</summary>
    public GameLoopPhase Phase { get; }
    /// <summary>Absolute authoritative activation tick.</summary>
    public ulong? CountdownAtTick { get; }
    /// <summary>Final mode outcome; null until completion.</summary>
    public MatchOutcome? Outcome { get; }
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

        return new(Tick, GameLoopPhase.Countdown, checked(Tick + duration));
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
        return new(tick, counting ? GameLoopPhase.Countdown : GameLoopPhase.Active, counting ? CountdownAtTick : null);
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

        return new(Tick, GameLoopPhase.Finished, outcome: outcome);
    }
}
