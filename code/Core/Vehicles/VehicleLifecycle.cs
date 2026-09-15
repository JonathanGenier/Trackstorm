namespace Trackstorm.Core.Vehicles;

/// <summary>Authoritative participation state within the existing vehicle aggregate.</summary>
public enum VehicleLifecycle : byte
{
    /// <summary>May drive and participate in combat.</summary>
    Alive,
    /// <summary>The lethal fixed boundary; emitted once per life.</summary>
    Dead,
    /// <summary>Non-interactive while waiting for the authoritative respawn deadline.</summary>
    Respawning,
}
