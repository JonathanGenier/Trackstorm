namespace Trackstorm.Core.Matches;

/// <summary>Host-owned match rules; timers use simulation ticks exclusively.</summary>
public sealed record MatchConfiguration
{
    /// <summary>Required kills, defaulting to first-to-five.</summary>
    public int KillTarget { get; init; } = 5;
    /// <summary>Three seconds at the production 60 Hz rate.</summary>
    public ulong CountdownTicks { get; init; } = 180;
    /// <summary>Participants needed to begin the countdown.</summary>
    public int MinimumPlayers { get; init; } = 2;

    /// <summary>Rejects unusable or unbounded configuration.</summary>
    public void Validate()
    {
        if (KillTarget is < 1 or > 1000000 || CountdownTicks is < 1 or > 36000 || MinimumPlayers is < 1 or > 8)
        {
            throw new ArgumentException("Invalid match target, countdown or participant count.");
        }
    }
}
