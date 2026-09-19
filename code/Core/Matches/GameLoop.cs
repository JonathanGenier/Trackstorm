namespace Trackstorm.Core.Matches;

/// <summary>Host-owned lifecycle accepting a completed Application Flow handoff and mode-reported outcomes.</summary>
public sealed class GameLoop
{
    private readonly bool _isAuthority;

    /// <summary>Creates an uninitialized owner or read-only client; authority cannot change during its lifetime.</summary>
    /// <param name="isAuthority">Trusted composition decision, never a value taken from a client packet.</param>
    public GameLoop(bool isAuthority)
    {
        _isAuthority = isAuthority;
    }

    /// <summary>Completed loading handoff; absent before initialization.</summary>
    public SynchronizedMatchContext? Context { get; private set; }
    /// <summary>Read-only phase boundary; absent until the completed handoff is accepted.</summary>
    public GameLoopState? State { get; private set; }
    /// <summary>Monotonic phase revision, incremented exactly once per accepted transition.</summary>
    public ulong Revision { get; private set; }
    /// <summary>No gameplay before handoff, during initialization/countdown, or after completion.</summary>
    public bool AllowsGameplay => State?.AllowsGameplay == true;

    /// <summary>Enters Initialization exactly once after the caller has completed match loading/synchronization.</summary>
    /// <param name="context">Completed handoff from trusted Application Flow.</param>
    /// <returns>Whether initialization was accepted.</returns>
    public bool Initialize(SynchronizedMatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!_isAuthority || State is not null)
        {
            return false;
        }

        Context = context;
        State = new GameLoopState(context.Tick, GameLoopPhase.Initialization);
        Revision++;
        return true;
    }

    /// <summary>Starts one countdown from the current authoritative tick.</summary>
    /// <param name="durationTicks">Positive duration with a representable absolute deadline.</param>
    /// <returns>False for unauthorized, invalid, duplicate or out-of-order requests.</returns>
    public bool StartCountdown(ulong durationTicks)
    {
        if (!_isAuthority || State?.Phase != GameLoopPhase.Initialization || durationTicks == 0 || durationTicks > ulong.MaxValue - State.Tick)
        {
            return false;
        }

        State = State.StartCountdown(durationTicks);
        Revision++;
        return true;
    }

    /// <summary>Consumes exactly the next fixed tick; only authority can cross the countdown deadline.</summary>
    /// <param name="tick">Next authoritative simulation tick.</param>
    /// <returns>Whether the tick was accepted.</returns>
    public bool Advance(ulong tick)
    {
        if (!_isAuthority || State is null || State.Phase is GameLoopPhase.Initialization or GameLoopPhase.Finished || State.Tick == ulong.MaxValue || tick != State.Tick + 1)
        {
            return false;
        }

        GameLoopState next = State.Advance(tick);
        if (next.Phase != State.Phase)
        {
            Revision++;
        }

        State = next;
        return true;
    }

    /// <summary>Accepts a trusted active game mode's authoritative outcome once, without navigating or resetting.</summary>
    /// <param name="outcome">Mode-validated final result; no client command routes to this API.</param>
    /// <returns>Whether Finished was entered.</returns>
    public bool ReportCompletion(MatchOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (!_isAuthority || State?.Phase != GameLoopPhase.Active)
        {
            return false;
        }

        State = State.Finish(outcome);
        Revision++;
        return true;
    }
}
