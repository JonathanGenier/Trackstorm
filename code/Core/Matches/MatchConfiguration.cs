namespace Trackstorm.Core.Matches;

/// <summary>Host-owned match rules; timers use simulation ticks exclusively.</summary>
public sealed record MatchConfiguration
{
    /// <summary>Scoring mode, selected before countdown and retained for this match.</summary>
    public MatchMode Mode { get; init; } = MatchMode.Circus;
    /// <summary>Required kills for FirstToTarget only; Circus ignores this threshold.</summary>
    public int KillTarget { get; init; } = 5;
    /// <summary>Active Circus duration in fixed ticks; ten minutes at the production 60 Hz rate.</summary>
    public ulong DurationTicks { get; init; } = 36000;
    /// <summary>Three seconds at the production 60 Hz rate.</summary>
    public ulong CountdownTicks { get; init; } = 180;
    /// <summary>Participants needed to begin the countdown.</summary>
    public int MinimumPlayers { get; init; } = 2;

    /// <summary>Base points per second of authoritative overspeed above the normal forward limit.</summary>
    public double NitroPointsPerSecond { get; init; } = 10;

    /// <summary>Base Circus points per valid kill.</summary>
    public double BaseKillPoints { get; init; } = 100;
    /// <summary>Additional base points per consecutive kill after the first.</summary>
    public double KillStreakBonusStep { get; init; } = 25;
    /// <summary>Base Circus points per actual collision HP removed from another participant.</summary>
    public double CollisionPointsPerDamage { get; init; } = 1;

    /// <summary>Minimum horizontal metres per second for a physical drift.</summary>
    public double DriftMinimumSpeed { get; init; } = 5;

    /// <summary>Minimum uninterrupted drift duration required to bank.</summary>
    public double DriftMinimumSeconds { get; init; } = 0.25;

    /// <summary>Initial drift base points per second.</summary>
    public double DriftRate { get; init; } = 5;

    /// <summary>Added drift points per second per escalation tier.</summary>
    public double DriftTierStep { get; init; } = 5;

    /// <summary>Uninterrupted seconds between drift rate tiers.</summary>
    public double DriftTierSeconds { get; init; } = 2;

    /// <summary>Minimum uninterrupted flight duration required for a valid landing award.</summary>
    public double AirtimeMinimumSeconds { get; init; } = 0.25;

    /// <summary>Initial airtime base points per second.</summary>
    public double AirtimeRate { get; init; } = 5;

    /// <summary>Added airtime points per second per escalation tier.</summary>
    public double AirtimeTierStep { get; init; } = 5;

    /// <summary>Uninterrupted seconds between airtime rate tiers.</summary>
    public double AirtimeTierSeconds { get; init; } = 2;

    /// <summary>Long Jump base points per horizontal metre from takeoff to landing.</summary>
    public double JumpPointsPerMetre { get; init; } = 2;

    /// <summary>Fraction of configured forward speed required to enter Top Speed.</summary>
    public double TopSpeedEnterRatio { get; init; } = 0.95;

    /// <summary>Lower speed fraction sustaining an active Top Speed event.</summary>
    public double TopSpeedExitRatio { get; init; } = 0.92;

    /// <summary>Maximum additional rate tiers for Drift and Airtime.</summary>
    public int StuntMaximumTier { get; init; } = 4;

    /// <summary>Rejects unusable or unbounded configuration.</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(Mode) || DurationTicks is < 1 or > 216000 || KillTarget is < 1 or > 1000000 || CountdownTicks is < 1 or > 36000 || MinimumPlayers is < 1 or > 8 ||
            !ValidPoints(NitroPointsPerSecond) || !ValidPoints(BaseKillPoints) || !ValidPoints(KillStreakBonusStep) || !ValidPoints(CollisionPointsPerDamage) || !ValidPoints(DriftRate) || !ValidPoints(DriftTierStep) ||
            !ValidPoints(AirtimeRate) || !ValidPoints(AirtimeTierStep) || !ValidPoints(JumpPointsPerMetre) ||
            !Bounded(DriftMinimumSpeed, 0.1, 65) || !Bounded(DriftMinimumSeconds, 0.01, 60) ||
            !Bounded(AirtimeMinimumSeconds, 0.01, 60) || !Bounded(DriftTierSeconds, 0.01, 60) || !Bounded(AirtimeTierSeconds, 0.01, 60) ||
            StuntMaximumTier is < 1 or > 100 || !Bounded(TopSpeedEnterRatio, 0.5, 1) ||
            !Bounded(TopSpeedExitRatio, 0.5, 1) || TopSpeedExitRatio >= TopSpeedEnterRatio)
        {
            throw new ArgumentException("Invalid match rules or Circus scoring tuning.");
        }
    }

    private static bool Bounded(double value, double minimum, double maximum) => double.IsFinite(value) && value >= minimum && value <= maximum;

    private static bool ValidPoints(double value) => double.IsFinite(value) && value is >= 0 and <= 1000000;
}
