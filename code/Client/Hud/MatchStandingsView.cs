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

        var participants = roster.Players.Concat(roster.Departed.Select(player => new SessionPlayer(player.Id, player.Name, false, false))).ToDictionary(player => player.Id);
        var ranks = match.FinalResults is { } final
            ? final.Standings.Select(row => new MatchStanding(row.PlayerId, row.Rank, row.CircusScore, row.Kills, row.Deaths))
            : MatchRanking.Create(match, participants.Keys);
        StandingsRow[] rows = ranks.Select(rank =>
        {
            SessionPlayer player = participants.GetValueOrDefault(rank.PlayerId) ?? new SessionPlayer(rank.PlayerId, $"Player {rank.PlayerId}", false, false);
            return new StandingsRow(rank.PlayerId, rank.Rank, player.Name, rank.CircusScore, rank.Kills, rank.Deaths, PingFormatter.Format(player.Connected ? ping(rank.PlayerId) : null), rank.PlayerId == match.Winner, rank.PlayerId == localPlayer, player.Connected);
        }).ToArray();
        bool finished = match.Phase == MatchPhase.Finished;
        return new(finished || (match.Phase == MatchPhase.Active && held.HasFlag(InputButtons.Leaderboard)), finished, Array.AsReadOnly(rows), rows.SingleOrDefault(row => row.PlayerId == localPlayer)?.Rank.ToString(CultureInfo.InvariantCulture) ?? "--", match.Winner is ulong winner ? participants.GetValueOrDefault(winner)?.Name ?? $"Player {winner}" : string.Empty);
    }
}
