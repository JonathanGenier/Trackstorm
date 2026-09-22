namespace Trackstorm.Core.Matches;

/// <summary>Shared Core award calculation for banked Circus sources.</summary>
public static class CircusScoring
{
    /// <summary>Banks nonnegative base points using the participant's current authoritative K/D.</summary>
    /// <param name="player">Current authoritative totals.</param>
    /// <param name="basePoints">Source-specific base award.</param>
    /// <returns>Updated immutable totals.</returns>
    public static PlayerScore Bank(PlayerScore player, double basePoints)
        => Bank(player, basePoints, null, default);

    /// <summary>Banks points and records the exact committed delta for presentation.</summary>
    internal static PlayerScore Bank(PlayerScore player, double basePoints, ICollection<CircusScoreAward>? awards, CircusScoreCategory category)
    {
        double points = basePoints * player.KdMultiplier;
        double total = player.CircusScore + points;
        if (!double.IsFinite(basePoints) || basePoints < 0 || !double.IsFinite(points) || !double.IsFinite(total) || total < 0)
        {
            throw new ArgumentException("Invalid Circus award.", nameof(basePoints));
        }

        if (points > 0 && awards is not null)
        {
            awards.Add(new CircusScoreAward(player.Player, category, points));
        }

        return player with { CircusScore = total };
    }
}
