namespace Trackstorm.Core.Matches;

/// <summary>Committed score delta for leaderboard/kill-feed consumers, identified by publication revision and victim life.</summary>
/// <param name="Victim">Player receiving one death.</param>
/// <param name="Life">Destroyed vehicle life.</param>
/// <param name="Killer">Player receiving one kill, or zero for self/world/invalid sources.</param>
public readonly record struct ScoredDeath(ulong Victim, ulong Life, ulong Killer);
