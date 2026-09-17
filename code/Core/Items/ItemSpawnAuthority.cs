using System.Numerics;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Events;

namespace Trackstorm.Core.Items;

/// <summary>Match-owned spawn clock and atomic distribution into the existing single-slot authority.</summary>
public sealed class ItemSpawnAuthority
{
    private readonly ItemAuthority _items;
    private readonly Func<HeldItem>? _select;
    private readonly Dictionary<string, ArenaSpawn> _markers;
    private readonly Dictionary<string, ItemSpawnState> _states;
    private Random _random;
    private ulong _tick;

    /// <summary>Registers exactly the validated arena markers and owns an independent selection stream.</summary>
    /// <param name="arena">Actual validated arena contract.</param>
    /// <param name="items">The match's sole inventory owner.</param>
    /// <param name="configuration">Host tuning.</param>
    /// <param name="selector">Optional deterministic test seam.</param>
    public ItemSpawnAuthority(ArenaConfiguration arena, ItemAuthority items, ItemSpawnConfiguration? configuration = null, Func<HeldItem>? selector = null)
    {
        ArgumentNullException.ThrowIfNull(arena);
        ArgumentNullException.ThrowIfNull(items);
        Configuration = configuration ?? new();
        Configuration.Validate();
        _items = items;
        _select = selector;
        _random = new Random(Configuration.Seed);
        _markers = arena.Items.ToDictionary(marker => marker.Id, StringComparer.Ordinal);
        _states = arena.Items.ToDictionary(marker => marker.Id, marker => new ItemSpawnState(marker.Id, true, 0, 0, 0, HeldItem.None), StringComparer.Ordinal);
    }

    /// <summary>Immutable validated host tuning.</summary>
    public ItemSpawnConfiguration Configuration { get; private set; }
    /// <summary>Changes only when a claim or activation commits.</summary>
    public ulong Revision { get; private set; }
    /// <summary>Detached state in canonical marker order.</summary>
    public IReadOnlyList<ItemSpawnState> States => _states.Values.OrderBy(state => state.Id, StringComparer.Ordinal).ToArray();

    /// <summary>Reactivates due spawns using only committed authoritative time.</summary>
    /// <param name="world">Match world.</param>
    public void Advance(Simulation.Simulation world)
    {
        if (world.State.Tick < _tick)
        {
            throw new ArgumentException("Spawn time cannot rewind.");
        }

        _tick = world.State.Tick;
        foreach (var state in States.Where(state => !state.Available && state.NextActivationTick <= _tick))
        {
            _states[state.Id] = state with { Available = true };
            Revision++;
            world.Events.Record(EventCategory.Item, "Pickup respawned", context: state.Id, tick: _tick);
        }

    }

    /// <summary>Validates host-observed contact against committed position, life and slot before one atomic award.</summary>
    /// <returns>Whether exactly one spawn and one slot changed.</returns>
    /// <param name="world">Match world at the current spawn boundary.</param>
    /// <param name="id">Reported marker ID.</param>
    /// <param name="vehicle">Host-observed vehicle identity.</param>
    public bool TryPickup(Simulation.Simulation world, string id, ulong vehicle)
    {
        if (world.State.Tick != _tick || !_states.TryGetValue(id, out var spawn) || !spawn.Available)
        {
            return false;
        }

        var player = world.State.Vehicles.SingleOrDefault(state => state.VehicleId == vehicle);
        if (player is null || !player.CanInteract ||
            Vector3.DistanceSquared(player.Movement.Physics.Position, _markers[id].Position) > Configuration.PickupRadius * Configuration.PickupRadius ||
            _items.Slots.Any(slot => slot.Vehicle == vehicle && slot.Life == player.LifeId && slot.Item != HeldItem.None))
        {
            return false;
        }

        ulong activation = checked(_tick + (ulong)Configuration.CooldownTicks);
        HeldItem item = _select?.Invoke() ?? (_random.Next(Configuration.WrenchWeight + Configuration.MissileWeight) < Configuration.WrenchWeight ? HeldItem.Wrench : HeldItem.Missile);
        if (item is not (HeldItem.Wrench or HeldItem.Missile))
        {
            throw new InvalidOperationException("Pickup selector returned an item outside the configured pool.");
        }

        if (!_items.Grant(world, vehicle, item, pickup: true))
        {
            return false;
        }

        ItemSlot granted = _items.Slots.Single(slot => slot.Vehicle == vehicle);
        _states[id] = new ItemSpawnState(id, false, activation, vehicle, granted.Token, item);
        Revision++;
        world.Events.Record(EventCategory.Item, "Picked up", actor: vehicle, cause: item.ToString(), context: id, tick: _tick);
        return true;
    }

    /// <summary>Updates future claims without resetting existing cooldowns or consuming a random draw.</summary>
    /// <param name="configuration">Validated effective gameplay tuning.</param>
    internal void ApplyConfiguration(ItemSpawnConfiguration configuration)
    {
        configuration.Validate();
        if (configuration.Seed != Configuration.Seed)
        {
            _random = new Random(configuration.Seed);
        }

        Configuration = configuration;
    }

}
