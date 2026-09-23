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
    /// <param name="awards">Committed Circus awards aggregated by player and source category.</param>
    /// <param name="mode">Authoritative match-scoped scoring policy.</param>
    public MatchState(ulong tick, ulong revision, int killTarget, MatchPhase phase, ulong? countdownAtTick, ulong? winner, IEnumerable<PlayerScore> players, IEnumerable<ScoredDeath>? changes = null, IEnumerable<CircusScoreAward>? awards = null, MatchMode mode = MatchMode.Circus)
    {
        PlayerScore[] scores = players.OrderBy(player => player.Player).ToArray();
        ScoredDeath[] deaths = changes?.ToArray() ?? [];
        CircusScoreAward[] scoreAwards = awards?.ToArray() ?? [];
        if (!Enum.IsDefined(mode) || killTarget is < 1 or > 1000000 || !Enum.IsDefined(phase) || scores.Length > MaximumPlayers ||
            (mode != MatchMode.Circus && (scoreAwards.Length != 0 || scores.Any(player => player.CircusScore != 0 || player.KillStreak != 0 || player.Stunts is not null))) ||
            scores.Any(player => player.Player == 0 || player.Kills < 0 || player.Kills > killTarget || player.Deaths < 0 || player.Wins is < 0 or > 1 || (player.Deaths > 0 && player.ProcessedLife == 0)) ||
            scores.Any(player => !double.IsFinite(player.CircusScore) || player.CircusScore < 0 || player.KillStreak < 0 || player.KillStreak > player.Kills ||
                (player.ProcessedDamageLife == 0) != (player.ProcessedDamageSequence == 0)) ||
            scores.Count(player => player.Stunts is not null) > 8 ||
            scores.Any(player => player.Stunts is { } stunt && (phase != MatchPhase.Active || !stunt.IsValid(tick) || !double.IsFinite(player.PendingStuntScore))) ||
            scores.Select(player => player.Player).Distinct().Count() != scores.Length ||
            (phase == MatchPhase.Countdown) != countdownAtTick.HasValue || (countdownAtTick.HasValue && countdownAtTick <= tick) ||
            (phase == MatchPhase.Finished) != winner.HasValue ||
            (winner.HasValue && !scores.Any(player => player.Player == winner && player.Kills == killTarget && player.Wins == 1)) ||
            scores.Any(player => player.Wins != (player.Player == winner ? 1 : 0) || (player.Kills == killTarget && player.Player != winner)) ||
            (phase is MatchPhase.Waiting or MatchPhase.Countdown && scores.Any(player => player.Kills != 0 || player.Deaths != 0 || player.CircusScore != 0)) ||
            scores.Sum(player => (long)player.Kills) > scores.Sum(player => (long)player.Deaths) ||
            deaths.Length > 8 || deaths.Select(death => death.Victim).Distinct().Count() != deaths.Length ||
            deaths.Any(death => death.Life == 0 || !scores.Any(player => player.Player == death.Victim && player.Deaths > 0 && player.ProcessedLife == death.Life) ||
                (death.Killer != 0 && (death.Killer == death.Victim || !scores.Any(player => player.Player == death.Killer && player.Kills > 0)))) ||
            scoreAwards.Length > 56 || scoreAwards.Any(award => award.Player == 0 || !Enum.IsDefined(award.Category) || !double.IsFinite(award.Points) || award.Points <= 0 || !scores.Any(player => player.Player == award.Player)) ||
            scoreAwards.Select(award => (award.Player, award.Category)).Distinct().Count() != scoreAwards.Length ||
            scoreAwards.GroupBy(award => award.Player).Any(group => !double.IsFinite(group.Sum(award => award.Points)) || group.Sum(award => award.Points) > scores.Single(player => player.Player == group.Key).CircusScore))
        {
            throw new ArgumentException("Invalid match snapshot.");
        }

        Tick = tick;
        Mode = mode;
        Revision = revision;
        KillTarget = killTarget;
        Phase = phase;
        CountdownAtTick = countdownAtTick;
        Winner = winner;
        Players = Array.AsReadOnly(scores);
        Changes = Array.AsReadOnly(deaths);
        Awards = Array.AsReadOnly(scoreAwards);
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
    /// <summary>Scoring policy carried by publications and complete recovery boundaries.</summary>
    public MatchMode Mode { get; }
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
    /// <summary>Once-per-revision committed Circus award deltas, aggregated by player and category.</summary>
    public IReadOnlyList<CircusScoreAward> Awards { get; }
    /// <summary>Reusable phase rules; legacy Waiting projects to pre-countdown Initialization.</summary>
    public GameLoopState Lifecycle { get; }
    /// <summary>Frozen Core-ranked results; restored from the same authoritative checkpoint without UI rules.</summary>
    public FinalMatchResults? FinalResults { get; }
}
