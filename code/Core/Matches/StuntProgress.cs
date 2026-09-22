namespace Trackstorm.Core.Matches;

/// <summary>Uninterrupted fixed-step duration and unbanked base points for one category.</summary>
/// <param name="Ticks">Number of qualifying observations.</param>
/// <param name="BasePoints">Pending points before the current shared K/D multiplier.</param>
public readonly record struct StuntProgress(ulong Ticks, double BasePoints)
{
    internal bool IsValid(ulong tick) => Ticks <= tick && double.IsFinite(BasePoints) && BasePoints >= 0 && (Ticks != 0 || BasePoints == 0);
}
