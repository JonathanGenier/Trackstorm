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
    private ulong _token;

    /// <summary>Creates a match-scoped item authority.</summary>
    /// <param name="configuration">Immutable host tuning.</param>
    public ItemAuthority(ItemConfiguration? configuration = null)
    {
        Configuration = configuration ?? new();
        Configuration.Validate();
    }

    /// <summary>Validated immutable tuning.</summary>
    public ItemConfiguration Configuration { get; }
    /// <summary>Changed ownership/projectile state revision.</summary>
    public ulong Revision { get; private set; }
    /// <summary>Read-only detached inventory.</summary>
    public IReadOnlyList<ItemSlot> Slots => _slots.Values.OrderBy(slot => slot.Vehicle).ToArray();
    /// <summary>Read-only detached projectile state.</summary>
    public IReadOnlyList<MissileState> Missiles => _missiles.ToArray();
    /// <summary>Only the last committed step's presentation outcomes.</summary>
    public IReadOnlyList<ItemEvent> Events { get; private set; } = Array.Empty<ItemEvent>();

    /// <summary>Grants an item only to an empty, active, living slot. Called by host acquisition/dev controls.</summary>
    /// <param name="world">Authoritative vehicle world.</param>
    /// <param name="vehicle">Recipient identity.</param>
    /// <param name="item">One of the two real items.</param>
    /// <returns>Whether ownership changed.</returns>
    public bool Grant(Simulation.Simulation world, ulong vehicle, HeldItem item)
    {
        VehicleSnapshot? state = world.State.Vehicles.SingleOrDefault(value => value.VehicleId == vehicle);
        if (state is null || !state.CanInteract || item is not (HeldItem.Wrench or HeldItem.Missile) ||
            (_slots.TryGetValue(vehicle, out var previous) && previous.Life == state.LifeId && previous.Item != HeldItem.None))
        {
            return false;
        }

        _slots[vehicle] = new ItemSlot(vehicle, state.LifeId, checked(++_token), item);
        Revision++;
        return true;
    }

    /// <summary>Queues at most one use of the exact issued slot; clients cannot select another player's identity.</summary>
    /// <param name="world">Authoritative active vehicle roster.</param>
    /// <param name="vehicle">Identity resolved from the actual transport sender.</param>
    /// <param name="life">Requested life generation.</param>
    /// <param name="token">Exact ownership token observed by the player.</param>
    /// <returns>Whether accepted for fixed-step validation.</returns>
    public bool RequestUse(Simulation.Simulation world, ulong vehicle, ulong life, ulong token)
    {
        VehicleSnapshot? state = world.State.Vehicles.SingleOrDefault(value => value.VehicleId == vehicle);
        return state is not null && state.CanInteract && state.LifeId == life &&
            _slots.TryGetValue(vehicle, out var slot) && slot.Life == life && slot.Token == token && slot.Item != HeldItem.None &&
            _pending.TryAdd(vehicle, token);
    }

    /// <summary>Evaluates uses and swept projectiles, then atomically commits item state after the vehicle batch succeeds.</summary>
    /// <param name="world">Sole HP/movement authority.</param>
    /// <param name="input">Next world tick.</param>
    /// <param name="requests">Complete native observation batch.</param>
    /// <param name="collide">Host collision adapter returning a hit fraction in [0,1], or null.</param>
    public void Step(Simulation.Simulation world, InputFrame input, IReadOnlyList<VehicleStepRequest> requests, Func<MissileState, Vector3, float?> collide)
    {
        var slots = new Dictionary<ulong, ItemSlot>(_slots);
        var missiles = new List<MissileState>(_missiles);
        var events = new List<ItemEvent>();
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
            if (request.Reset.HasValue || (slot.Item == HeldItem.Missile && missiles.Count >= MaximumProjectiles))
            {
                continue;
            }

            VehiclePhysicsState pose = request.Observation.Physics;
            Vector3 forward = Vector3.Transform(-Vector3.UnitZ, pose.Orientation);
            // Start at the vehicle center, exclude the owner in the sweep: no muzzle-offset wall tunneling.
            Vector3 origin = pose.Position;
            if (slot.Item == HeldItem.Wrench)
            {
                repair.Add(pair.Key, Configuration.WrenchHeal);
            }
            else
            {
                missiles.Add(new MissileState(slot.Token, slot.Vehicle, origin, forward * Configuration.MissileSpeed, Configuration.MissileLifetimeTicks));
            }

            events.Add(new ItemEvent(slot.Token, slot.Vehicle, slot.Item, origin, false));
            slots[pair.Key] = slot with { Item = HeldItem.None };
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

        world.Step(input, requests.Select(request => new VehicleStepRequest(request.VehicleId, request.Input, request.Observation, effects[request.VehicleId], request.Reset, request.Repair + repair.GetValueOrDefault(request.VehicleId))).ToArray());
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

        advanced.RemoveAll(missile => !world.State.Vehicles.Any(vehicle => vehicle.VehicleId == missile.Owner && vehicle.CanInteract));
        bool changed = !slots.OrderBy(pair => pair.Key).SequenceEqual(_slots.OrderBy(pair => pair.Key)) || missiles.Count > 0;
        _slots.Clear();
        foreach (var pair in slots)
        {
            _slots.Add(pair.Key, pair.Value);
        }

        _missiles.Clear();
        _missiles.AddRange(advanced);
        _pending.Clear();
        Events = events.AsReadOnly();
        if (changed)
        {
            Revision++;
        }
    }

    /// <summary>Identical falloff/direction math for vehicles and host-observed movable objects.</summary>
    /// <param name="center">Impact point.</param>
    /// <param name="target">Target center.</param>
    /// <returns>Damage and impulse, zero at/outside the radius.</returns>
    public DamageEffect Explosion(Vector3 center, Vector3 target) => VehicleDamageMath.Explosion(center, target, Configuration.ExplosionRadius, Configuration.MaximumDamage, Configuration.MaximumImpulse, Vector3.Zero);
}
