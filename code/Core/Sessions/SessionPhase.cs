namespace Trackstorm.Core.Sessions;

/// <summary>Authoritative development session lifecycle.</summary>
public enum SessionPhase
{
    /// <summary>Players assemble and ready up.</summary>
    Lobby,
    /// <summary>The shared vehicle arena is active.</summary>
    Arena,
}
