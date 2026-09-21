namespace Trackstorm.Core.Matches;

/// <summary>Detached final rank and all currently implemented per-match statistics; no live connection data.</summary>
/// <param name="PlayerId">Stable session participant identity, including departed players.</param>
/// <param name="Rank">One-based authoritative position.</param>
/// <param name="CircusScore">Final banked Circus points.</param>
/// <param name="Kills">Final credited kills.</param>
/// <param name="Deaths">Final scored deaths.</param>
/// <param name="Wins">Final per-match wins, not a session total.</param>
public sealed record FinalMatchStanding(ulong PlayerId, int Rank, double CircusScore, int Kills, int Deaths, int Wins);
