namespace Trackstorm.Client.Hud;

/// <summary>Consistent semantic colors for the approved player-facing outcomes.</summary>
internal enum ActivityFeedTone
{
    /// <summary>Join or successful reconnection.</summary>
    Arrival,
    /// <summary>Leave or interrupted connection.</summary>
    Departure,
    /// <summary>Kill or unattributed death.</summary>
    Death,
}
