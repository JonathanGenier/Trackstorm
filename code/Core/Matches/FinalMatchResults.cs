namespace Trackstorm.Core.Matches;

/// <summary>Immutable mode-completed results ready for Application Flow to retain independently of the match owner.</summary>
public sealed class FinalMatchResults
{
    /// <summary>Detaches mode-owned final rows without deriving or changing their ranking.</summary>
    /// <param name="tick">Authoritative completion tick, never presentation time.</param>
    /// <param name="outcome">Validated mode outcome.</param>
    /// <param name="standings">Complete mode-ranked rows, or no rows for an outcome-only mode.</param>
    public FinalMatchResults(ulong tick, MatchOutcome outcome, IEnumerable<FinalMatchStanding> standings)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(standings);
        FinalMatchStanding[] rows = standings.Take(MatchState.MaximumPlayers + 1).ToArray();
        if (rows.Length > MatchState.MaximumPlayers ||
            rows.Any(row => row is null || row.PlayerId == 0 || row.Rank < 1 || row.Kills < 0 || row.Deaths < 0 || row.Wins is < 0 or > 1) ||
            rows.Select(row => row.PlayerId).Distinct().Count() != rows.Length ||
            !rows.Select(row => row.Rank).Order().SequenceEqual(Enumerable.Range(1, rows.Length)) ||
            (rows.Length > 0 && outcome.Winner is ulong winner && !rows.Any(row => row.PlayerId == winner && row.Rank == 1)))
        {
            throw new ArgumentException("Final results require bounded unique participants and complete authoritative ranks.", nameof(standings));
        }

        Tick = tick;
        Outcome = outcome;
        Standings = Array.AsReadOnly(rows.OrderBy(row => row.Rank).ToArray());
    }

    /// <summary>Frozen authoritative completion tick.</summary>
    public ulong Tick { get; }
    /// <summary>Frozen reason and optional winner.</summary>
    public MatchOutcome Outcome { get; }
    /// <summary>Ready-to-render order and statistics, independent of later roster connectivity or teardown.</summary>
    public IReadOnlyList<FinalMatchStanding> Standings { get; }

    internal static FinalMatchResults From(MatchState match)
    {
        var scores = match.Players.ToDictionary(player => player.Player);
        return new(match.Tick, match.Lifecycle.Outcome!, MatchRanking.Create(match, scores.Keys)
            .Select(row => new FinalMatchStanding(row.PlayerId, row.Rank, row.Kills, row.Deaths, scores[row.PlayerId].Wins)));
    }
}
