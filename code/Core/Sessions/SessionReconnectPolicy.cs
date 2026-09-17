namespace Trackstorm.Core.Sessions;

/// <summary>Phase-specific player continuity after a transport or membership loss.</summary>
public enum SessionReconnectPolicy
{
    /// <summary>Lobby departures remove the player; a later return is a new admission.</summary>
    FreshJoin,

    /// <summary>Arena departures retain the player for authenticated resume.</summary>
    RetainedResume,
}
