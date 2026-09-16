using System.Globalization;
using Trackstorm.Core.Input;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Hud;

/// <summary>Read-only projection shared by the leaderboard and HUD position, with no gameplay mutation.</summary>
/// <param name="Visible">Held or forced visible.</param>
/// <param name="Finished">Final results state.</param>
/// <param name="Rows">Ordered participants.</param>
/// <param name="Position">Local authoritative rank.</param>
/// <param name="WinnerName">Winner label independent of current row order.</param>
internal sealed record MatchStandingsView(bool Visible, bool Finished, IReadOnlyList<StandingsRow> Rows, string Position, string WinnerName)
{
    /// <summary>Builds presentation from a single authoritative match/roster pair and local held intent.</summary>
    /// <param name="roster">Current authoritative roster.</param>
    /// <param name="match">Current authoritative totals.</param>
    /// <param name="localPlayer">Local identity.</param>
    /// <param name="held">Logical held controls.</param>
    /// <param name="ping">Fresh diagnostics provider.</param>
    /// <returns>Detached presentation.</returns>
    internal static MatchStandingsView From(LobbySnapshot? roster, MatchState? match, ulong localPlayer, InputButtons held, Func<ulong, int?> ping)
    {
        if (roster?.Phase != SessionPhase.Arena || match is null)
        {
            return new(false, false, Array.Empty<StandingsRow>(), "--", string.Empty);
        }

        var names = roster.Players.ToDictionary(player => player.Id, player => player.Name);
        var ranks = MatchRanking.Create(match, names.Keys);
        StandingsRow[] rows = ranks.Select(rank => new StandingsRow(rank.PlayerId, rank.Rank, names[rank.PlayerId], rank.Kills, rank.Deaths, PingFormatter.Format(ping(rank.PlayerId)), rank.PlayerId == match.Winner, rank.PlayerId == localPlayer)).ToArray();
        bool finished = match.Phase == MatchPhase.Finished;
        return new(finished || (match.Phase == MatchPhase.Active && held.HasFlag(InputButtons.Leaderboard)), finished, Array.AsReadOnly(rows), rows.SingleOrDefault(row => row.PlayerId == localPlayer)?.Rank.ToString(CultureInfo.InvariantCulture) ?? "--", match.Winner is ulong winner ? names.GetValueOrDefault(winner, $"Player {winner}") : string.Empty);
    }
}
