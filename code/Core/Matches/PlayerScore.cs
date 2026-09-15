namespace Trackstorm.Core.Matches;

/// <summary>Per-match totals and a life watermark that prevents duplicate death scoring after restoration.</summary>
/// <param name="Player">Stable player/vehicle identity.</param>
/// <param name="Kills">Credited kills.</param>
/// <param name="Deaths">Scored deaths.</param>
/// <param name="Wins">Zero or one win in this match.</param>
/// <param name="ProcessedLife">Highest destroyed life consumed, including deaths outside Active.</param>
public readonly record struct PlayerScore(ulong Player, int Kills, int Deaths, int Wins, ulong ProcessedLife);
