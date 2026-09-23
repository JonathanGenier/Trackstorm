using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Complete reliable ownership/outcome boundary and bounded projectile presentation state.</summary>
public sealed class ItemPublication
{
    /// <summary>Copies and validates every nested record before publication.</summary>
    /// <param name="revision">Strictly increasing host publication identity.</param>
    /// <param name="world">Authoritative HP/movement outcome at the same boundary.</param>
    /// <param name="slots">At most one slot per active player.</param>
    /// <param name="missiles">Bounded active projectiles.</param>
    /// <param name="events">This step's launch/repair/impact effects.</param>
    /// <param name="spawns">Complete configured spawn state.</param>
    public ItemPublication(ulong revision, WorldSnapshot world, IEnumerable<ItemSlot> slots, IEnumerable<MissileState> missiles, IEnumerable<ItemEvent> events, IEnumerable<ItemSpawnState>? spawns = null)
    {
        var inventory = slots.ToArray();
        var projectiles = missiles.ToArray();
        var outcomes = events.ToArray();
        if (revision == 0 || inventory.Length > 8 || inventory.Select(slot => slot.Vehicle).Distinct().Count() != inventory.Length ||
            inventory.Any(slot => slot.Token == 0 || (slot.Item != HeldItem.None && ItemRegistry.Find(slot.Item) is null) || !world.Vehicles.Any(vehicle => vehicle.State.VehicleId == slot.Vehicle && vehicle.State.LifeId == slot.Life)) ||
            projectiles.Length > ItemAuthority.MaximumProjectiles || projectiles.Select(missile => missile.Id).Distinct().Count() != projectiles.Length ||
            projectiles.Any(missile => missile.Id == 0 || missile.Owner == 0 || !VehiclePhysicsState.IsFinite(missile.Position) || !VehiclePhysicsState.IsFinite(missile.Velocity) || missile.Velocity.Length() is <= 0 or > 301 || missile.RemainingTicks is < 1 or > 3600) ||
            outcomes.Length > ItemAuthority.MaximumProjectiles + 8 || outcomes.Any(outcome => outcome.Token == 0 || outcome.Owner == 0 || ItemRegistry.Find(outcome.Item)?.CanUse != true || !VehiclePhysicsState.IsFinite(outcome.Position) || (outcome.Impact && outcome.Item != HeldItem.Missile)))
        {
            throw new ArgumentException("Invalid item publication.");
        }

        var pickups = spawns?.ToArray() ?? Array.Empty<ItemSpawnState>();
        if (pickups.Length > Arenas.ArenaConfiguration.MaximumItemSpawns || pickups.Select(spawn => spawn.Id).Distinct(StringComparer.Ordinal).Count() != pickups.Length ||
            pickups.Any(spawn => string.IsNullOrWhiteSpace(spawn.Id) || spawn.Id != spawn.Id.Trim() || System.Text.Encoding.UTF8.GetByteCount(spawn.Id) > 128 ||
                (spawn.Token == 0 ? spawn.ClaimedBy != 0 || spawn.Item != HeldItem.None || spawn.NextActivationTick != 0 || !spawn.Available :
                spawn.ClaimedBy == 0 || ItemRegistry.Find(spawn.Item) is null || spawn.NextActivationTick == 0) ||
                (spawn.Available ? spawn.NextActivationTick > world.Tick : spawn.NextActivationTick <= world.Tick)))
        {
            throw new ArgumentException("Invalid spawn publication.");
        }

        Spawns = Array.AsReadOnly(pickups);
        Revision = revision;
        World = world;
        Slots = Array.AsReadOnly(inventory);
        Missiles = Array.AsReadOnly(projectiles);
        Events = Array.AsReadOnly(outcomes);
    }

    /// <summary>Complete marker state and last claim.</summary>
    public IReadOnlyList<ItemSpawnState> Spawns { get; }

    /// <summary>Monotonic delivery identity.</summary>
    public ulong Revision { get; }
    /// <summary>Complete authoritative vehicle outcome.</summary>
    public WorldSnapshot World { get; }
    /// <summary>One slot per player.</summary>
    public IReadOnlyList<ItemSlot> Slots { get; }
    /// <summary>Authoritative projectiles.</summary>
    public IReadOnlyList<MissileState> Missiles { get; }
    /// <summary>Presentation-only events.</summary>
    public IReadOnlyList<ItemEvent> Events { get; }
}
