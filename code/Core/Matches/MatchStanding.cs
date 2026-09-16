namespace Trackstorm.Core.Matches;

/// <summary>Authoritative rank derived from the current match totals and participating roster.</summary>
/// <param name="PlayerId">Stable session identity.</param>
/// <param name="Rank">Unique one-based position.</param>
/// <param name="Kills">Authoritative kills.</param>
/// <param name="Deaths">Authoritative deaths.</param>
public sealed record MatchStanding(ulong PlayerId, int Rank, int Kills, int Deaths);
