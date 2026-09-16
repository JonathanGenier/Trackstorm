namespace Trackstorm.Core.Sessions;

/// <summary>Reliable client intents; no command carries a client-selected player identity.</summary>
public enum LobbyCommand
{
    /// <summary>Request admission with a display name.</summary>
    Join,
    /// <summary>Set the sender's readiness.</summary>
    Ready,
    /// <summary>Request a host-only start.</summary>
    Start,
    /// <summary>Request a host-only return.</summary>
    Return,
    /// <summary>Explicit departure; never starts automatic reconnect.</summary>
    Leave,
    /// <summary>Reclaim an authenticated reserved slot.</summary>
    Resume,
}
