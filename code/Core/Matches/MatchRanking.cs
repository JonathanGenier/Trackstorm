namespace Trackstorm.Core.Matches;

/// <summary>Single ranking rule for all match consumers; never consumes network diagnostics.</summary>
public static class MatchRanking
{
    /// <summary>Ranks participants by kills descending, deaths ascending, then stable identity. The recorded winner is first.</summary>
    /// <param name="match">Authoritative immutable match boundary.</param>
    /// <param name="participants">Occupied roster and retained match-history identities.</param>
    /// <returns>Detached ordered standings, shared by HUD and results presentation.</returns>
    public static IReadOnlyList<MatchStanding> Create(MatchState match, IEnumerable<ulong> participants)
    {
        ArgumentNullException.ThrowIfNull(match);
        ulong[] ids = participants.Take(MatchState.MaximumPlayers + 1).ToArray();
        if (ids.Length > MatchState.MaximumPlayers || ids.Any(id => id == 0) || ids.Distinct().Count() != ids.Length)
        {
            throw new ArgumentException("Ranking requires bounded unique match participants.", nameof(participants));
        }

        var totals = match.Players.ToDictionary(player => player.Player);
        return Array.AsReadOnly(ids.Select(id => totals.GetValueOrDefault(id, new PlayerScore(id, 0, 0, 0, 0)))
            .OrderByDescending(player => player.Player == match.Winner)
            .ThenByDescending(player => player.Kills)
            .ThenBy(player => player.Deaths)
            .ThenBy(player => player.Player)
            .Select((player, index) => new MatchStanding(player.Player, index + 1, player.Kills, player.Deaths)).ToArray());
    }
}
