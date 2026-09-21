namespace Trackstorm.Core.Matches;

/// <summary>Committed banked-score delta for read-only presentation consumers.</summary>
/// <param name="Player">Stable participant identity receiving the award.</param>
/// <param name="Category">Authoritative scoring source.</param>
/// <param name="Points">Positive points actually added after the authoritative multiplier.</param>
public readonly record struct CircusScoreAward(ulong Player, CircusScoreCategory Category, double Points);
