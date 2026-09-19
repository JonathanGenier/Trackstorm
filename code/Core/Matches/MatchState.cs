namespace Trackstorm.Core.Matches;

/// <summary>Complete immutable scoring/restoration boundary; retains departed players until the arena ends.</summary>
public sealed class MatchState
{
    /// <summary>Bound on lifetime participants, including departed players, for reliable packet sizing.</summary>
    public const int MaximumPlayers = 256;

    /// <summary>Copies and validates a complete match boundary before it can be published.</summary>
    /// <param name="tick">Simulation tick of the latest match change.</param>
    /// <param name="revision">Monotonic publication identity.</param>
    /// <param name="killTarget">Positive winning threshold.</param>
    /// <param name="phase">Current lifecycle phase.</param>
    /// <param name="countdownAtTick">Active deadline, present only during Countdown.</param>
    /// <param name="winner">Winning player, present only after Finished.</param>
    /// <param name="players">Detached score totals and consumed-life memory.</param>
    /// <param name="changes">Deaths scored in this publication.</param>
    public MatchState(ulong tick, ulong revision, int killTarget, MatchPhase phase, ulong? countdownAtTick, ulong? winner, IEnumerable<PlayerScore> players, IEnumerable<ScoredDeath>? changes = null)
    {
        PlayerScore[] scores = players.OrderBy(player => player.Player).ToArray();
        ScoredDeath[] deaths = changes?.ToArray() ?? [];
        if (killTarget is < 1 or > 1000000 || !Enum.IsDefined(phase) || scores.Length > MaximumPlayers ||
            scores.Any(player => player.Player == 0 || player.Kills < 0 || player.Kills > killTarget || player.Deaths < 0 || player.Wins is < 0 or > 1 || (player.Deaths > 0 && player.ProcessedLife == 0)) ||
            scores.Select(player => player.Player).Distinct().Count() != scores.Length ||
            (phase == MatchPhase.Countdown) != countdownAtTick.HasValue || (countdownAtTick.HasValue && countdownAtTick <= tick) ||
            (phase == MatchPhase.Finished) != winner.HasValue ||
            (winner.HasValue && !scores.Any(player => player.Player == winner && player.Kills == killTarget && player.Wins == 1)) ||
            scores.Any(player => player.Wins != (player.Player == winner ? 1 : 0) || (player.Kills == killTarget && player.Player != winner)) ||
            (phase is MatchPhase.Waiting or MatchPhase.Countdown && scores.Any(player => player.Kills != 0 || player.Deaths != 0)) ||
            scores.Sum(player => (long)player.Kills) > scores.Sum(player => (long)player.Deaths) ||
            deaths.Length > 8 || deaths.Select(death => death.Victim).Distinct().Count() != deaths.Length ||
            deaths.Any(death => death.Life == 0 || !scores.Any(player => player.Player == death.Victim && player.Deaths > 0 && player.ProcessedLife == death.Life) ||
                (death.Killer != 0 && (death.Killer == death.Victim || !scores.Any(player => player.Player == death.Killer && player.Kills > 0)))))
        {
            throw new ArgumentException("Invalid match snapshot.");
        }

        Tick = tick;
        Revision = revision;
        KillTarget = killTarget;
        Phase = phase;
        CountdownAtTick = countdownAtTick;
        Winner = winner;
        Players = Array.AsReadOnly(scores);
        Changes = Array.AsReadOnly(deaths);
        GameLoopPhase lifecyclePhase = phase switch
        {
            MatchPhase.Waiting => GameLoopPhase.Initialization,
            MatchPhase.Countdown => GameLoopPhase.Countdown,
            MatchPhase.Active => GameLoopPhase.Active,
            MatchPhase.Finished => GameLoopPhase.Finished,
            _ => throw new ArgumentException("Invalid match phase."),
        };
        Lifecycle = new GameLoopState(tick, lifecyclePhase, countdownAtTick, winner.HasValue ? new MatchOutcome("kill-target", winner) : null);
        FinalResults = phase == MatchPhase.Finished ? FinalMatchResults.From(this) : null;
    }

    /// <summary>Latest match mutation tick, independent of movement packet arrival.</summary>
    public ulong Tick { get; }
    /// <summary>Reliable publication sequence.</summary>
    public ulong Revision { get; }
    /// <summary>Authoritative winning threshold.</summary>
    public int KillTarget { get; }
    /// <summary>Current phase.</summary>
    public MatchPhase Phase { get; }
    /// <summary>Core deadline; null outside countdown.</summary>
    public ulong? CountdownAtTick { get; }
    /// <summary>Immutable winner after finish.</summary>
    public ulong? Winner { get; }
    /// <summary>Current-match scores, including departed participants.</summary>
    public IReadOnlyList<PlayerScore> Players { get; }
    /// <summary>Once-per-revision score deltas; late joiners can reconstruct totals without replaying these.</summary>
    public IReadOnlyList<ScoredDeath> Changes { get; }
    /// <summary>Reusable phase rules; legacy Waiting projects to pre-countdown Initialization.</summary>
    public GameLoopState Lifecycle { get; }
    /// <summary>Frozen Core-ranked results; restored from the same authoritative checkpoint without UI rules.</summary>
    public FinalMatchResults? FinalResults { get; }
}
