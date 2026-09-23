using Trackstorm.Core.Events;
using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Host-owned inventory, use validation and projectile simulation, committed with the vehicle world.</summary>
public sealed class ItemAuthority
{
    /// <summary>Bounds simultaneous blast batches within the existing 16 KiB vehicle envelope.</summary>
    public const int MaximumProjectiles = 16;
    private readonly Dictionary<ulong, ItemSlot> _slots = new();
    private readonly Dictionary<ulong, ulong> _pending = new();
    private readonly List<MissileState> _missiles = new();
    /// <summary>Portable hard ceiling on match hazards.</summary>
    public const int MaximumPatches = 32;
    private readonly List<OilPatch> _patches = new();
    private readonly List<OilContact> _contacts = new();
    private ulong _token;

    /// <summary>Creates a match-scoped item authority.</summary>
    /// <param name="configuration">Immutable host tuning.</param>
    public ItemAuthority(ItemConfiguration? configuration = null)
    {
        Configuration = configuration ?? new();
        Configuration.Validate();
    }

    /// <summary>Validated immutable tuning.</summary>
    public ItemConfiguration Configuration { get; private set; }
    /// <summary>Changed ownership/projectile state revision.</summary>
    public ulong Revision { get; private set; }
    /// <summary>Read-only detached inventory.</summary>
    public IReadOnlyList<ItemSlot> Slots => _slots.Values.OrderBy(slot => slot.Vehicle).ToArray();
    /// <summary>Read-only detached projectile state.</summary>
    public IReadOnlyList<MissileState> Missiles => _missiles.ToArray();
    /// <summary>Only the last committed step's presentation outcomes.</summary>
    public IReadOnlyList<ItemEvent> Events { get; private set; } = Array.Empty<ItemEvent>();

    /// <summary>Highest issued token, including consumed and departed ownership.</summary>
    public ulong TokenHighWater => _token;
    /// <summary>Complete persistent hazards, independent of deployer lifetime.</summary>
    public IReadOnlyList<OilPatch> Patches => _patches.ToArray();
    /// <summary>Current entry latches for authority continuation.</summary>
    public IReadOnlyList<OilContact> OilContacts => _contacts.ToArray();

    /// <summary>Restores committed ownership without pending commands or historical effects.</summary>
    /// <param name="publication">Validated current world and item state.</param>
    /// <param name="revision">Authority mutation revision.</param>
    /// <param name="token">Highest ever issued token in this match.</param>
    public void Restore(ItemPublication publication, ulong revision, ulong token)
    {
        if (publication.Events.Count != 0 || publication.Patches.Any(patch => patch.Id > token) || publication.Slots.Any(slot => slot.Token > token) ||
            publication.Spawns.Any(spawn => spawn.Token > token) || publication.Missiles.Any(missile => missile.Id > token ||
                !publication.World.Vehicles.Any(vehicle => vehicle.State.VehicleId == missile.Owner && vehicle.State.CanInteract)))
        {
            throw new ArgumentException("Invalid item authority continuation.");
        }

        _slots.Clear();
        foreach (var slot in publication.Slots)
        {
            _slots.Add(slot.Vehicle, slot);
        }

        _missiles.Clear();
        _missiles.AddRange(publication.Missiles);
        _patches.Clear();
        _patches.AddRange(publication.Patches);
        _contacts.Clear();
        _contacts.AddRange(publication.OilContacts);
        _pending.Clear();
        Events = Array.Empty<ItemEvent>();
        Revision = revision;
        _token = token;
    }

    /// <summary>Discards an uncommitted use when its transport owner is suspended.</summary>
    /// <param name="vehicle">Authoritative player identity.</param>
    public void CancelPending(ulong vehicle) => _pending.Remove(vehicle);

    /// <summary>Removes departed ownership before a checkpoint can observe an absent vehicle.</summary>
    /// <param name="vehicle">Finalized departing player.</param>
    public void RemovePlayer(ulong vehicle)
    {
        _pending.Remove(vehicle);
        bool changed = _slots.Remove(vehicle);
        changed |= _contacts.RemoveAll(contact => contact.Vehicle == vehicle) > 0;
        changed |= _missiles.RemoveAll(missile => missile.Owner == vehicle) > 0;
        if (changed)
        {
            Revision++;
        }
    }

    /// <summary>Grants an item only to an empty, active, living slot. Called by host acquisition/dev controls.</summary>
    /// <returns>Whether ownership changed.</returns>
    /// <param name="world">Authoritative vehicle world.</param>
    /// <param name="vehicle">Recipient identity.</param>
    /// <param name="item">A registered item identity.</param>
    /// <param name="pickup">Whether the spawn authority will publish the contextual pickup outcome.</param>
    public bool Grant(Simulation.Simulation world, ulong vehicle, HeldItem item, bool pickup = false)
    {
        VehicleSnapshot? state = world.State.Vehicles.SingleOrDefault(value => value.VehicleId == vehicle);
        if (state is null || !state.CanInteract || ItemRegistry.Find(item) is null ||
            (_slots.TryGetValue(vehicle, out var previous) && previous.Life == state.LifeId && previous.Item != HeldItem.None))
        {
            return false;
        }

        _slots[vehicle] = new ItemSlot(vehicle, state.LifeId, checked(++_token), item);
        Revision++;
        if (!pickup)
        {
            world.Events.Record(EventCategory.Item, "Granted", target: vehicle, cause: item.ToString(), life: state.LifeId, tick: world.State.Tick);
        }

        return true;
    }

    /// <summary>Queues at most one use of the exact issued slot; clients cannot select another player's identity.</summary>
    /// <returns>Whether accepted for fixed-step validation.</returns>
    /// <param name="world">Authoritative active vehicle roster.</param>
    /// <param name="vehicle">Identity resolved from the actual transport sender.</param>
    /// <param name="life">Requested life generation.</param>
    /// <param name="token">Exact ownership token observed by the player.</param>
    public bool RequestUse(Simulation.Simulation world, ulong vehicle, ulong life, ulong token)
    {
        VehicleSnapshot? state = world.State.Vehicles.SingleOrDefault(value => value.VehicleId == vehicle);
        return state is not null && state.CanInteract && state.LifeId == life &&
            _slots.TryGetValue(vehicle, out var slot) && slot.Life == life && slot.Token == token && ItemRegistry.Find(slot.Item)?.CanUse == true &&
            _pending.TryAdd(vehicle, token);
    }

    /// <summary>Evaluates uses and swept projectiles, then atomically commits item state after the vehicle batch succeeds.</summary>
    /// <param name="world">Sole HP/movement authority.</param>
    /// <param name="input">Next world tick.</param>
    /// <param name="requests">Complete native observation batch.</param>
    /// <param name="collide">Host collision adapter returning a hit fraction in [0,1], or null.</param>
    /// <param name="placeOil">Host-only ground projection; null rejects deployment.</param>
    public void Step(Simulation.Simulation world, InputFrame input, IReadOnlyList<VehicleStepRequest> requests, Func<MissileState, Vector3, float?> collide, Func<ItemSlot, VehiclePhysicsState, OilPatch?>? placeOil = null)
    {
        var slots = new Dictionary<ulong, ItemSlot>(_slots);
        var missiles = new List<MissileState>(_missiles);
        var events = new List<ItemEvent>();
        var patches = new List<OilPatch>(_patches);
        var contacts = new List<OilContact>();
        var spins = new Dictionary<ulong, float>();
        var journal = new List<RuntimeEvent>();
        var repair = new Dictionary<ulong, float>();
        var effects = requests.ToDictionary(request => request.VehicleId, request => request.Effects.ToList());
        foreach (var pair in slots.ToArray())
        {
            VehicleSnapshot? state = world.State.Vehicles.SingleOrDefault(value => value.VehicleId == pair.Key);
            if (state is null || (!state.CanInteract && (world.Respawn?.ClearHeldItemOnDeath ?? true)) || state.LifeId != pair.Value.Life || requests.Any(request => request.VehicleId == pair.Key && request.Reset.HasValue))
            {
                slots.Remove(pair.Key);
            }
        }

        foreach (var pair in _pending.OrderBy(pair => pair.Key))
        {
            if (!slots.TryGetValue(pair.Key, out var slot) || !world.GetVehicle(pair.Key).CanInteract || slot.Token != pair.Value || slot.Item == HeldItem.None)
            {
                continue;
            }

            VehicleStepRequest request = requests.Single(value => value.VehicleId == pair.Key);
            var handler = ItemRegistry.Find(slot.Item)?.Handler;
            if (request.Reset.HasValue || handler is null ||
                !handler.Stage(slot, request.Observation.Physics, Configuration, missiles, repair, patches, placeOil))
            {
                continue;
            }

            events.Add(new ItemEvent(slot.Token, slot.Vehicle, slot.Item, request.Observation.Physics.Position, false));
            slots[pair.Key] = slot with { Item = HeldItem.None };
        }

        if (world.State.Match?.Phase != Matches.MatchPhase.Finished)
        {
            foreach (var request in requests.OrderBy(request => request.VehicleId))
            {
                var vehicle = world.GetVehicle(request.VehicleId);
                if (!vehicle.CanInteract || request.Reset.HasValue) { continue; }
                foreach (var patch in patches.OrderBy(patch => patch.Id))
                {
                    var contact = new OilContact(patch.Id, vehicle.VehicleId, vehicle.LifeId);
                    if (!patch.Contains(request.Observation)) { continue; }
                    contacts.Add(contact);
                    if (!_contacts.Contains(contact))
                    {
                        // Multiple simultaneous entries share one bounded physical response.
                        spins.TryAdd(vehicle.VehicleId, ((patch.Id ^ vehicle.VehicleId) & 1) == 0 ? 2.6f : -2.6f);
                        journal.Add(new RuntimeEvent { Category = EventCategory.Item, Kind = "Oil triggered", Actor = patch.Owner, Target = vehicle.VehicleId, Cause = "Oil", Tick = input.Tick });
                    }
                }
            }
        }

        var advanced = new List<MissileState>();
        foreach (MissileState missile in missiles)
        {
            if (!world.State.Vehicles.Any(vehicle => vehicle.VehicleId == missile.Owner && vehicle.CanInteract))
            {
                continue;
            }

            Vector3 end = missile.Position + (missile.Velocity / 60);
            float? hit = collide(missile, end);
            if (hit is float fraction)
            {
                if (!float.IsFinite(fraction) || fraction < 0 || fraction > 1)
                {
                    throw new ArgumentException("Collision fraction must lie on the swept segment.");
                }

                Vector3 center = Vector3.Lerp(missile.Position, end, fraction);
                events.Add(new ItemEvent(missile.Id, missile.Owner, HeldItem.Missile, center, true));
                foreach (VehicleStepRequest request in requests)
                {
                    DamageEffect effect = Explosion(center, request.Observation.Physics.Position);
                    if (effect.Damage > 0 || effect.Impulse != Vector3.Zero)
                    {
                        effects[request.VehicleId].Add(new VehicleEffectRequest(effect, new DamageContext("missile", missile.Owner, "radial-explosion")));
                    }
                }
            }
            else if (missile.RemainingTicks > 1)
            {
                advanced.Add(missile with { Position = end, RemainingTicks = missile.RemainingTicks - 1 });
            }
        }

        world.Step(input, requests.Select(request => new VehicleStepRequest(request.VehicleId, request.Input, request.Observation, effects[request.VehicleId], request.Reset, request.Repair + repair.GetValueOrDefault(request.VehicleId), repair.ContainsKey(request.VehicleId) ? "Wrench" : request.RepairCause, spins.GetValueOrDefault(request.VehicleId))).ToArray(), journal.Concat(events.Select(outcome => new RuntimeEvent { Category = EventCategory.Item, Kind = outcome.Impact ? "Impact" : "Used", Actor = outcome.Owner, Cause = outcome.Item.ToString(), Tick = input.Tick })).ToArray());
        foreach (var pair in slots.ToArray())
        {
            VehicleSnapshot state = world.GetVehicle(pair.Key);
            if (!state.CanInteract && (world.Respawn?.ClearHeldItemOnDeath ?? true))
            {
                slots.Remove(pair.Key);
            }
            else if (state.LifeId != pair.Value.Life)
            {
                slots[pair.Key] = new ItemSlot(pair.Key, state.LifeId, checked(++_token), pair.Value.Item);
            }
        }

        contacts.RemoveAll(contact => !world.State.Vehicles.Any(vehicle => vehicle.VehicleId == contact.Vehicle && vehicle.LifeId == contact.Life && vehicle.CanInteract));
        if (world.State.Match?.Phase == Matches.MatchPhase.Finished)
        {
            patches.Clear();
            contacts.Clear();
        }

        advanced.RemoveAll(missile => !world.State.Vehicles.Any(vehicle => vehicle.VehicleId == missile.Owner && vehicle.CanInteract));
        bool changed = !slots.OrderBy(pair => pair.Key).SequenceEqual(_slots.OrderBy(pair => pair.Key)) || missiles.Count > 0 || !patches.SequenceEqual(_patches) || !contacts.SequenceEqual(_contacts) || journal.Count > 0;
        foreach (var removed in _slots.Values.Where(slot => slot.Item != HeldItem.None && !slots.ContainsKey(slot.Vehicle)))
        {
            world.Events.Record(EventCategory.Item, "Removed", target: removed.Vehicle, cause: removed.Item.ToString(), context: "life ended or reset", tick: input.Tick);
        }

        _slots.Clear();
        foreach (var pair in slots)
        {
            _slots.Add(pair.Key, pair.Value);
        }

        foreach (var removed in missiles.Where(missile => !advanced.Any(value => value.Id == missile.Id) && !events.Any(value => value.Token == missile.Id && value.Impact)))
        {
            world.Events.Record(EventCategory.Item, "Projectile removed", actor: removed.Owner, cause: "Missile", context: removed.RemainingTicks <= 1 ? "lifetime expired" : "owner inactive", tick: input.Tick);
        }

        _missiles.Clear();
        _missiles.AddRange(advanced);
        _patches.Clear();
        _patches.AddRange(patches);
        _contacts.Clear();
        _contacts.AddRange(contacts);
        _pending.Clear();
        Events = events.AsReadOnly();
        if (changed)
        {
            Revision++;
        }
    }

    /// <summary>Identical falloff/direction math for vehicles and host-observed movable objects.</summary>
    /// <returns>Damage and impulse, zero at/outside the radius.</returns>
    /// <param name="center">Impact point.</param>
    /// <param name="target">Target center.</param>
    public DamageEffect Explosion(Vector3 center, Vector3 target) => VehicleDamageMath.Explosion(center, target, Configuration.ExplosionRadius, Configuration.MaximumDamage, Configuration.MaximumImpulse, Vector3.Zero);
    /// <summary>Updates the existing authority; in-flight speed changes preserve direction and remaining lifetime.</summary>
    /// <param name="configuration">Validated effective gameplay tuning.</param>
    internal void ApplyConfiguration(ItemConfiguration configuration)
    {
        configuration.Validate();
        if (configuration.MissileSpeed != Configuration.MissileSpeed)
        {
            for (int i = 0; i < _missiles.Count; i++)
            {
                var missile = _missiles[i];
                _missiles[i] = missile with { Velocity = Vector3.Normalize(missile.Velocity) * configuration.MissileSpeed };
            }

            Revision++;
        }

        Configuration = configuration;
    }

}
