using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Networking.Replication;

/// <summary>Authority-only continuation supplement to the existing complete resync state.</summary>
public sealed record HostRestoreState
{
    /// <summary>Immutable vehicle health and collision tuning.</summary>
    public DamageConfiguration Damage { get; init; } = new();
    /// <summary>Immutable item tuning.</summary>
    public ItemConfiguration Items { get; init; } = new();
    /// <summary>Immutable lifecycle tuning.</summary>
    public RespawnConfiguration Respawn { get; init; } = new();
    /// <summary>Immutable match rules.</summary>
    public MatchConfiguration Match { get; init; } = new();
    /// <summary>Registered pickup tuning, absent in standalone vehicle worlds.</summary>
    public ItemSpawnConfiguration? Spawns { get; init; }
    /// <summary>Highest issued item/projectile identity, including already consumed grants.</summary>
    public ulong Token { get; init; }
    /// <summary>Committed item mutation revision.</summary>
    public ulong ItemRevision { get; init; }
    /// <summary>Committed pickup mutation revision.</summary>
    public ulong SpawnRevision { get; init; }
    /// <summary>Complete match-owned item-selection stream continuation.</summary>
    public ulong RandomState { get; init; }
    /// <summary>Highest allocated vehicle identity.</summary>
    public ulong NextVehicle { get; init; }
}
