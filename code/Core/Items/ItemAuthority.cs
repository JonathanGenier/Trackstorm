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
    private readonly Dictionary<ulong, (ulong Token, ulong Expires, uint? InputSequence)> _pending = new();
    private readonly List<MissileState> _missiles = new();
    /// <summary>Allocation bound derived from eight uses per step and the maximum 600-second lifetime, not a deployment gate.</summary>
    public const int MaximumPatches = 8 * 60 * 600;
    private readonly List<OilPatch> _patches = new();
    private readonly List<OilContact> _contacts = new();
    /// <summary>Bounded persistent magnetic hazards per match.</summary>
    public const int MaximumMines = 16;
    private readonly List<ProxyMineState> _mines = new();
    /// <summary>Complete mine motion and seating state for replication and recovery.</summary>
    public IReadOnlyList<ProxyMineState> Mines => _mines.ToArray();
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
    /// <summary>Patch-lifetime distinct enemy history for authority continuation.</summary>
    public IReadOnlyList<OilContact> OilContacts => _contacts.ToArray();

    /// <summary>Restores committed ownership without pending commands or historical effects.</summary>
    /// <param name="publication">Validated current world and item state.</param>
    /// <param name="revision">Authority mutation revision.</param>
    /// <param name="token">Highest ever issued token in this match.</param>
    public void Restore(ItemPublication publication, ulong revision, ulong token)
    {
        if (publication.Events.Count != 0 || publication.Mines.Any(mine => mine.Id > token) || publication.Patches.Any(patch => patch.Id > token) || publication.Slots.Any(slot => slot.Token > token || slot.SecondToken > token) ||
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
        _mines.Clear();
        _mines.AddRange(publication.Mines);
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
            (_slots.TryGetValue(vehicle, out var previous) && previous.Life == state.LifeId && previous.Full))
        {
            return false;
        }

        var inventory = _slots.GetValueOrDefault(vehicle);
        if (inventory is null || inventory.Life != state.LifeId) { inventory = new(vehicle, state.LifeId, 0, HeldItem.None); }
        ulong token = checked(++_token);
        _slots[vehicle] = inventory.Item == HeldItem.None
            ? inventory with { Token = token, Item = item, NitroCharge = item == HeldItem.Nitro ? 100 : 0, SalvoShots = item == HeldItem.Salvo ? Configuration.SalvoCount : 0, SalvoReadyTick = 0, Ammo = item == HeldItem.MachineGun ? new(Configuration.MachineGunCapacity, Configuration.MachineGunCapacity) : null }
            : inventory with { SecondToken = token, SecondItem = item, SecondNitroCharge = item == HeldItem.Nitro ? 100 : 0, SecondSalvoShots = item == HeldItem.Salvo ? Configuration.SalvoCount : 0, SecondSalvoReadyTick = 0, SecondAmmo = item == HeldItem.MachineGun ? new(Configuration.MachineGunCapacity, Configuration.MachineGunCapacity) : null };
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
    /// <param name="inputSequence">Optional originating input sequence, binding remote sustained activation to its captured frame.</param>
    public bool RequestUse(Simulation.Simulation world, ulong vehicle, ulong life, ulong token, uint? inputSequence = null)
    {
        VehicleSnapshot? state = world.State.Vehicles.SingleOrDefault(value => value.VehicleId == vehicle);
        return state is not null && state.CanInteract && state.LifeId == life &&
            _slots.TryGetValue(vehicle, out var slot) && slot.Life == life && slot.Active.Token == token && ItemRegistry.Find(slot.Active.Item)?.CanUse == true &&
            _pending.TryAdd(vehicle, (token, checked(world.State.Tick + 15), inputSequence));
    }

    /// <summary>Switches the sender's selected slot once per ordered, life-scoped command.</summary>
    public bool Switch(Simulation.Simulation world, ulong vehicle, ulong life, ulong revision)
    {
        var state = world.State.Vehicles.SingleOrDefault(value => value.VehicleId == vehicle);
        if (state is null || !state.CanInteract || state.LifeId != life || revision == 0) { return false; }
        var inventory = _slots.GetValueOrDefault(vehicle) ?? new ItemSlot(vehicle, life, 0, HeldItem.None);
        if (inventory.Life != life || revision <= inventory.SelectionRevision) { return false; }
        _slots[vehicle] = inventory with { ActiveSlot = (byte)(inventory.ActiveSlot ^ ((revision - inventory.SelectionRevision) & 1)), SelectionRevision = revision, EngagedToken = 0 };
        Revision++;
        return true;
    }

    /// <summary>Evaluates uses and swept projectiles, then atomically commits item state after the vehicle batch succeeds.</summary>
    /// <param name="world">Sole HP/movement authority.</param>
    /// <param name="input">Next world tick.</param>
    /// <param name="requests">Complete native observation batch.</param>
    /// <param name="collide">Host collision adapter returning a hit fraction in [0,1], or null.</param>
    /// <param name="placeOil">Host-only ground projection; null rejects deployment.</param>
    /// <param name="placeMine">Host terrain installation query.</param>
    /// <param name="moveMine">Host sweep and contact query.</param>
    /// <param name="acknowledgedInputs">Host-consumed remote input sequences; never client-authored acknowledgements.</param>
    /// <param name="raycastWeapon">Host closest collision on an authoritative weapon ray.</param>
    /// <param name="ground">Host terrain projection at the fixed salvo range; missing terrain rejects use.</param>
    public void Step(Simulation.Simulation world, InputFrame input, IReadOnlyList<VehicleStepRequest> requests, Func<MissileState, Vector3, float?> collide, Func<ItemSlot, VehiclePhysicsState, OilPatch?>? placeOil = null, Func<ItemSlot, VehiclePhysicsState, ProxyMineState?>? placeMine = null, Func<ProxyMineState, ProxyMineState, ProxyMineMotion>? moveMine = null, IReadOnlyDictionary<ulong, uint>? acknowledgedInputs = null, Func<Vector3, Vector3?>? ground = null, Func<ulong, Vector3, Vector3, WeaponRayHit?>? raycastWeapon = null)
    {
        ulong token = _token;
        ulong NextToken() => checked(++token);
        var slots = new Dictionary<ulong, ItemSlot>(_slots);
        var missiles = new List<MissileState>(_missiles);
        var events = new List<ItemEvent>();
        var patches = _patches.Where(patch => patch.ExpiresAtTick > input.Tick).ToList();
        var mines = new List<ProxyMineState>(_mines);
        var activePatchIds = patches.Select(patch => patch.Id).ToHashSet();
        var contacts = _contacts.Where(contact => activePatchIds.Contains(contact.Patch)).ToList();
        var affected = contacts.Select(contact => (contact.Patch, contact.Vehicle)).ToHashSet();
        var contactCounts = contacts.GroupBy(contact => contact.Patch).ToDictionary(group => group.Key, group => group.Count());
        var oilTriggers = new List<OilTrigger>();
        var oilVehicles = new HashSet<ulong>();
        var journal = new List<RuntimeEvent>();
        var repair = new Dictionary<ulong, float>();
        var boosts = new Dictionary<ulong, NitroState>();
        var waiting = new Dictionary<ulong, (ulong Token, ulong Expires, uint? InputSequence)>();
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
            if (!slots.TryGetValue(pair.Key, out var inventory) || !world.GetVehicle(pair.Key).CanInteract)
            {
                continue;
            }

            // Use is bound to the selection at request acceptance, even if a later switch arrives before the step.
            byte usedIndex = inventory.Token == pair.Value.Token ? (byte)0 : (byte)1;
            ItemSlot slot = (inventory with { ActiveSlot = usedIndex }).Active;
            if (slot.Token != pair.Value.Token || slot.Item == HeldItem.None) { continue; }
            VehicleStepRequest request = requests.Single(value => value.VehicleId == pair.Key);
            var handler = ItemRegistry.Find(slot.Item)?.Handler;
            if (ItemRegistry.Find(slot.Item)?.Sustained == true)
            {
                // Tie network presses to their input boundary so a rapid re-press cannot be
                // consumed by the preceding held/release frame on the other delivery channel.
                if (pair.Value.InputSequence is uint sequence && acknowledgedInputs is not null &&
                    Networking.Replication.NetworkSequence.IsNewer(sequence, acknowledgedInputs.GetValueOrDefault(pair.Key)))
                {
                    if (input.Tick < pair.Value.Expires) { waiting.Add(pair.Key, pair.Value); }
                    continue;
                }
                // Reliable use and sequenced driving input can arrive in either order.
                if ((request.Input.Held & InputButtons.UseItem) == 0 && (request.Input.Released & InputButtons.UseItem) == 0 && input.Tick < pair.Value.Expires)
                {
                    waiting.Add(pair.Key, pair.Value);
                }
                if (!request.Reset.HasValue && inventory.Active.Token == slot.Token &&
                    world.State.Match is not { Phase: not Matches.MatchPhase.Active } &&
                    (request.Input.Held & InputButtons.UseItem) != 0 && inventory.EngagedToken != slot.Token)
                {
                    slots[pair.Key] = inventory with { EngagedToken = slot.Token };
                    if (slot.Item != HeldItem.MachineGun) { events.Add(new ItemEvent(slot.Token, slot.Vehicle, slot.Item, request.Observation.Physics.Position, false)); }
                }
                continue;
            }
            if (request.Reset.HasValue || handler is null || (slot.Item == HeldItem.Salvo && input.Tick < slot.SalvoReadyTick) ||
                !handler.Stage(slot, request.Observation.Physics, Configuration, missiles, repair, patches, placeOil, boosts, mines, moveMine is null ? null : placeMine, NextToken, ground))
            {
                continue;
            }

            if (slot.Item != HeldItem.Salvo) { events.Add(new ItemEvent(slot.Token, slot.Vehicle, slot.Item, request.Observation.Physics.Position, false)); }
            if (slot.Item == HeldItem.Salvo)
            {
                int remaining = slot.SalvoShots - 1;
                // Retire each shot's capability so delayed/replayed requests cannot spend the next round.
                ulong next = remaining > 0 ? NextToken() : slot.Token;
                ulong ready = remaining > 0 ? checked(input.Tick + (ulong)Configuration.SalvoIntervalTicks) : 0;
                slots[pair.Key] = usedIndex == 0
                    ? inventory with { Item = remaining > 0 ? HeldItem.Salvo : HeldItem.None, Token = next, SalvoShots = remaining, SalvoReadyTick = ready }
                    : inventory with { SecondItem = remaining > 0 ? HeldItem.Salvo : HeldItem.None, SecondToken = next, SecondSalvoShots = remaining, SecondSalvoReadyTick = ready };
            }
            else { slots[pair.Key] = usedIndex == 0 ? inventory with { Item = HeldItem.None } : inventory with { SecondItem = HeldItem.None }; }
        }

        foreach (var request in requests)
        {
            if (!slots.TryGetValue(request.VehicleId, out var inventory)) { continue; }
            var slot = inventory.Active;
            bool engaged = ItemRegistry.Find(slot.Item)?.Sustained == true && inventory.EngagedToken == slot.Token &&
                world.GetVehicle(slot.Vehicle).CanInteract && !request.Reset.HasValue &&
                world.State.Match is not { Phase: not Matches.MatchPhase.Active } &&
                (request.Input.Held & InputButtons.UseItem) != 0;
            if (!engaged)
            {
                slots[request.VehicleId] = inventory with { EngagedToken = 0 };
                continue;
            }
            if (slot.Item == HeldItem.MachineGun)
            {
                // No native observation provider means no round was fired and no resource is spent.
                if (raycastWeapon is null) { continue; }
                var ammo = slot.Ammo!;
                double phase = ammo.Phase + Configuration.MachineGunFireRate / 60;
                int remainingRounds = ammo.Remaining;
                while (remainingRounds > 0 && phase >= 1 - 1e-12)
                {
                    phase = Math.Max(0, phase - 1);
                    var pose = request.Observation.Physics;
                    Vector3 direction = MachineGunShot.Direction(slot.Token, ammo.Capacity - remainingRounds, pose.Orientation, Configuration.MachineGunSpread);
                    Vector3 end = pose.Position + direction * Configuration.MachineGunRange;
                    var hit = raycastWeapon(slot.Vehicle, pose.Position, end);
                    if (hit is not null)
                    {
                        if (!float.IsFinite(hit.Fraction) || hit.Fraction is < 0 or > 1 || hit.Vehicle == slot.Vehicle ||
                            (hit.Vehicle != 0 && !effects.ContainsKey(hit.Vehicle)))
                        { throw new ArgumentException("Invalid host weapon ray observation."); }
                        end = Vector3.Lerp(pose.Position, end, hit.Fraction);
                        if (hit.Vehicle != 0 && world.GetVehicle(hit.Vehicle).CanInteract && !requests.Single(value => value.VehicleId == hit.Vehicle).Reset.HasValue)
                        {
                            float fade = MachineGunShot.Falloff(hit.Fraction * Configuration.MachineGunRange, Configuration);
                            if (fade > 0)
                            {
                                var effect = new DamageEffect(Configuration.MachineGunDamage * fade, direction * (Configuration.MachineGunKnockback * fade), Vector3.Zero);
                                effects[hit.Vehicle].Add(new VehicleEffectRequest(effect, new DamageContext("machine-gun", slot.Vehicle, "bullet")));
                            }
                        }
                    }
                    events.Add(new ItemEvent(slot.Token, slot.Vehicle, slot.Item, end, hit is not null)
                    { Origin = pose.Position, Tracer = (ammo.Capacity - remainingRounds) % Configuration.MachineGunTracerEvery == 0 });
                    remainingRounds--;
                }
                MachineGunAmmo? remainingAmmo = remainingRounds == 0 ? null : ammo with { Remaining = remainingRounds, Phase = phase };
                var remainingItem = remainingAmmo is null ? HeldItem.None : HeldItem.MachineGun;
                slots[request.VehicleId] = inventory.ActiveSlot == 0
                    ? inventory with { Ammo = remainingAmmo, Item = remainingItem, EngagedToken = remainingAmmo is null ? 0 : inventory.EngagedToken }
                    : inventory with { SecondAmmo = remainingAmmo, SecondItem = remainingItem, EngagedToken = remainingAmmo is null ? 0 : inventory.EngagedToken };
                if (remainingAmmo is null) { journal.Add(new RuntimeEvent { Category = EventCategory.Item, Kind = "Exhausted", Actor = slot.Vehicle, Cause = "MachineGun", Tick = input.Tick }); }
                continue;
            }
            ItemRegistry.Find(HeldItem.Nitro)!.Handler!.Stage(slot, request.Observation.Physics, Configuration, missiles, repair, patches, placeOil, boosts, mines, placeMine, NextToken, ground);
            double remaining = Math.Max(0, slot.NitroCharge - Configuration.NitroConsumptionPerSecond / 60);
            if (remaining < 1e-9) { remaining = 0; }
            slots[request.VehicleId] = inventory.ActiveSlot == 0
                ? inventory with { NitroCharge = remaining, Item = remaining == 0 ? HeldItem.None : HeldItem.Nitro, EngagedToken = remaining == 0 ? 0 : inventory.EngagedToken }
                : inventory with { SecondNitroCharge = remaining, SecondItem = remaining == 0 ? HeldItem.None : HeldItem.Nitro, EngagedToken = remaining == 0 ? 0 : inventory.EngagedToken };
            if (remaining == 0) { boosts.Remove(request.VehicleId); journal.Add(new RuntimeEvent { Category = EventCategory.Item, Kind = "Exhausted", Actor = slot.Vehicle, Cause = "Nitro", Tick = input.Tick }); }
        }

        var previousPatchIds = _patches.Select(patch => patch.Id).ToHashSet();
        for (int i = 0; i < patches.Count; i++)
        {
            if (!previousPatchIds.Contains(patches[i].Id)) { patches[i] = patches[i] with { ExpiresAtTick = checked(input.Tick + (ulong)MathF.Ceiling(Configuration.OilLifetimeSeconds * 60)) }; }
        }

        if (world.State.Match?.Phase != Matches.MatchPhase.Finished)
        {
            foreach (var request in requests.OrderBy(request => request.VehicleId))
            {
                var vehicle = world.GetVehicle(request.VehicleId);
                if (!vehicle.CanInteract || request.Reset.HasValue) { continue; }
                foreach (var patch in patches.OrderBy(patch => patch.Id))
                {
                    if (!patch.Contains(request.Observation)) { continue; }
                    if (contactCounts.GetValueOrDefault(patch.Id) >= patch.EnemyContacts) { continue; }
                    oilVehicles.Add(vehicle.VehicleId);
                    if (patch.Owner != vehicle.VehicleId && affected.Add((patch.Id, vehicle.VehicleId)))
                    {
                        contacts.Add(new OilContact(patch.Id, vehicle.VehicleId, vehicle.LifeId));
                        contactCounts[patch.Id] = contactCounts.GetValueOrDefault(patch.Id) + 1;
                        oilTriggers.Add(new(patch.Id, patch.Owner, vehicle.VehicleId, vehicle.LifeId));
                    }
                }
            }
        }

        var movingMines = new List<ProxyMineState>();
        foreach (var mine in mines.OrderBy(mine => mine.Id))
        {
            if (world.State.Match?.Phase == Matches.MatchPhase.Finished) { break; }
            var targets = requests.Where(request => world.GetVehicle(request.VehicleId).CanInteract && !request.Reset.HasValue).OrderBy(request => request.VehicleId).ToArray();
            var nearest = targets.OrderBy(request => Vector3.DistanceSquared(mine.Position, request.Observation.Physics.Position)).FirstOrDefault();
            Vector3? target = nearest?.Observation.Physics.Position;
            if (moveMine is null) { movingMines.Add(mine); continue; }
            var candidate = mine.Advance(target, Configuration);
            var motion = moveMine(mine, candidate);
            motion.State.Validate();
            if (motion.State.Id != mine.Id || motion.State.Owner != mine.Owner || motion.State.SeatingTicks != candidate.SeatingTicks ||
                Vector3.Distance(motion.State.Position, candidate.Position) > 2 ||
                (motion.ContactVehicle != 0 && !requests.Any(request => request.VehicleId == motion.ContactVehicle)))
            { throw new ArgumentException("Invalid host mine motion observation."); }
            var contact = targets.FirstOrDefault(request => request.VehicleId == motion.ContactVehicle);
            if (contact is not null)
            {
                Vector3 outward = contact.Observation.Physics.Position - motion.State.Position;
                outward.Y = Math.Max(0.7f, outward.Y);
                var effect = new DamageEffect(Configuration.MineDamage, Vector3.Normalize(outward) * Configuration.MineKnockback, Vector3.Zero);
                effects[contact.VehicleId].Add(new VehicleEffectRequest(effect, new DamageContext("proxy-mine", mine.Owner, "contact-detonation")));
                events.Add(new ItemEvent(mine.Id, mine.Owner, HeldItem.ProxyMine, motion.State.Position, true));
            }
            else { movingMines.Add(motion.State); }
        }
        var advanced = new List<MissileState>();
        foreach (MissileState scheduled in missiles)
        {
            var missile = scheduled;
            if (!world.State.Vehicles.Any(vehicle => vehicle.VehicleId == missile.Owner && vehicle.CanInteract))
            {
                continue;
            }

            if (missile.Arc is { } arc)
            {
                if (world.State.Match?.Phase == Matches.MatchPhase.Finished ||
                    world.GetVehicle(missile.Owner).LifeId != arc.Life || requests.Any(request => request.VehicleId == missile.Owner && request.Reset.HasValue)) { continue; }
                if (arc.ElapsedTicks == 0)
                {
                    events.Add(new(missile.Id, missile.Owner, missile.Item, arc.Origin, false));
                }
            }

            Vector3 end = missile.Arc is { } flight ? flight.At(flight.ElapsedTicks + 1) : missile.Position + (missile.Velocity / 60);
            float? hit = collide(missile, end);
            if (hit is null && missile.Arc is not null && missile.RemainingTicks == 1) { hit = 1; }
            if (hit is float fraction)
            {
                if (!float.IsFinite(fraction) || fraction < 0 || fraction > 1)
                {
                    throw new ArgumentException("Collision fraction must lie on the swept segment.");
                }

                Vector3 center = Vector3.Lerp(missile.Position, end, fraction);
                events.Add(new ItemEvent(missile.Id, missile.Owner, missile.Item, center, true));
                foreach (VehicleStepRequest request in requests)
                {
                    DamageEffect effect = Explosion(center, request.Observation.Physics.Position, missile.Item);
                    if (effect.Damage > 0 || effect.Impulse != Vector3.Zero)
                    {
                        effects[request.VehicleId].Add(new VehicleEffectRequest(effect, new DamageContext(missile.Arc is null ? "missile" : "salvo", missile.Owner, missile.Arc is null ? "radial-explosion" : $"radial-explosion:{missile.Id}")));
                    }
                }
            }
            else if (missile.RemainingTicks > 1)
            {
                advanced.Add(missile with { Position = end, RemainingTicks = missile.RemainingTicks - 1,
                    Velocity = missile.Arc is null ? missile.Velocity : (end - missile.Position) * 60,
                    Arc = missile.Arc is { } continuation ? continuation with { ElapsedTicks = continuation.ElapsedTicks + 1 } : null });
            }
        }

        world.Step(input, requests.Select(request => new VehicleStepRequest(request.VehicleId, request.Input, request.Observation, effects[request.VehicleId], request.Reset, request.Repair + repair.GetValueOrDefault(request.VehicleId), repair.ContainsKey(request.VehicleId) ? "Wrench" : request.RepairCause, oilVehicles.Contains(request.VehicleId), boosts.GetValueOrDefault(request.VehicleId), request.ClearNitro || !boosts.ContainsKey(request.VehicleId))).ToArray(), journal.Concat(events.Where(outcome => outcome.Item != HeldItem.MachineGun).Select(outcome => new RuntimeEvent { Category = EventCategory.Item, Kind = outcome.Impact ? "Impact" : "Used", Actor = outcome.Owner, Cause = outcome.Item.ToString(), Tick = input.Tick })).ToArray(), oilTriggers);
        foreach (var pair in slots.ToArray())
        {
            VehicleSnapshot state = world.GetVehicle(pair.Key);
            if (!state.CanInteract && (world.Respawn?.ClearHeldItemOnDeath ?? true))
            {
                slots.Remove(pair.Key);
            }
            else if (state.LifeId != pair.Value.Life)
            {
                slots[pair.Key] = pair.Value with { Life = state.LifeId, Token = pair.Value.Token == 0 ? 0 : NextToken(), SecondToken = pair.Value.SecondToken == 0 ? 0 : NextToken(), SelectionRevision = 0, EngagedToken = 0 };
            }
        }

        // Only successful new effects consume contacts. Prior history survives death, departure and new lives.
        var priorContacts = _contacts.ToHashSet();
        contacts.RemoveAll(contact => !priorContacts.Contains(contact) && !world.State.Vehicles.Any(vehicle => vehicle.VehicleId == contact.Vehicle && vehicle.LifeId == contact.Life && vehicle.CanInteract && vehicle.Movement.OilTicks > 0));
        var patchOwners = patches.ToDictionary(patch => patch.Id, patch => patch.Owner);
        foreach (var contact in contacts.Where(contact => !priorContacts.Contains(contact)))
        {
            world.Events.Record(EventCategory.Item, "Oil triggered", actor: patchOwners[contact.Patch], target: contact.Vehicle, cause: "Oil", tick: input.Tick);
        }
        contactCounts = contacts.GroupBy(contact => contact.Patch).ToDictionary(group => group.Key, group => group.Count());
        patches.RemoveAll(patch => contactCounts.GetValueOrDefault(patch.Id) >= patch.EnemyContacts);
        activePatchIds = patches.Select(patch => patch.Id).ToHashSet();
        contacts.RemoveAll(contact => !activePatchIds.Contains(contact.Patch));
        if (world.State.Match?.Phase == Matches.MatchPhase.Finished)
        {
            patches.Clear();
            movingMines.Clear();
            contacts.Clear();
        }

        advanced.RemoveAll(missile => !world.State.Vehicles.Any(vehicle => vehicle.VehicleId == missile.Owner && vehicle.CanInteract));
        if (world.State.Match?.Phase == Matches.MatchPhase.Finished) { advanced.RemoveAll(missile => missile.Arc is not null); }
        bool changed = !slots.OrderBy(pair => pair.Key).SequenceEqual(_slots.OrderBy(pair => pair.Key)) || missiles.Count > 0 || !patches.SequenceEqual(_patches) || !contacts.SequenceEqual(_contacts) || journal.Count > 0 || !movingMines.SequenceEqual(_mines);
        foreach (var removed in _slots.Values.Where(slot => !slots.ContainsKey(slot.Vehicle)).SelectMany(slot => new[] { slot, slot with { Token = slot.SecondToken, Item = slot.SecondItem } }).Where(slot => slot.Item != HeldItem.None))
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
            world.Events.Record(EventCategory.Item, "Projectile removed", actor: removed.Owner, cause: removed.Item.ToString(), context: removed.RemainingTicks <= 1 ? "lifetime expired" : "owner inactive", tick: input.Tick);
        }

        _missiles.Clear();
        _missiles.AddRange(advanced);
        _token = token;
        _mines.Clear();
        _mines.AddRange(movingMines);
        _patches.Clear();
        _patches.AddRange(patches);
        _contacts.Clear();
        _contacts.AddRange(contacts);
        _pending.Clear();
        foreach (var pending in waiting) { _pending.Add(pending.Key, pending.Value); }
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
    /// <param name="item">Authoritative exploding item identity.</param>
    public DamageEffect Explosion(Vector3 center, Vector3 target, HeldItem item = HeldItem.Missile)
    {
        if (item != HeldItem.Salvo) { return VehicleDamageMath.Explosion(center, target, Configuration.ExplosionRadius, Configuration.MaximumDamage, Configuration.MaximumImpulse, Vector3.Zero); }
        float linear = Math.Clamp(1 - Vector3.Distance(center, target) / Configuration.SalvoBlastRadius, 0, 1);
        float scale = linear > 0 ? MathF.Pow(linear, Configuration.SalvoFalloff) / linear : 0;
        var unit = VehicleDamageMath.Explosion(center, target, Configuration.SalvoBlastRadius, 1, 1, Vector3.Zero);
        return new(unit.Damage * scale * Configuration.SalvoDamage, unit.Impulse * (scale * Configuration.SalvoImpulse), Vector3.Zero);
    }
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
                if (missile.Arc is not null) { continue; }
                _missiles[i] = missile with { Velocity = Vector3.Normalize(missile.Velocity) * configuration.MissileSpeed };
            }

            Revision++;
        }

        Configuration = configuration;
    }

}
