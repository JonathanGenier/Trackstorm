namespace Trackstorm.Core.Matches;

/// <summary>Shared Core award calculation for banked Circus sources.</summary>
public static class CircusScoring
{
    /// <summary>Banks nonnegative base points using the participant's current authoritative K/D.</summary>
    /// <param name="player">Current authoritative totals.</param>
    /// <param name="basePoints">Source-specific base award.</param>
    /// <returns>Updated immutable totals.</returns>
    public static PlayerScore Bank(PlayerScore player, double basePoints)
    {
        double total = player.CircusScore + (basePoints * player.KdMultiplier);
        if (!double.IsFinite(basePoints) || basePoints < 0 || !double.IsFinite(total) || total < 0)
        {
            throw new ArgumentException("Invalid Circus award.", nameof(basePoints));
        }

        return player with { CircusScore = total };
    }
}
