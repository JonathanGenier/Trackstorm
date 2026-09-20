using Trackstorm.Core.Events;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Simulation;

/// <summary>
/// Owns and advances authoritative state one caller-controlled fixed step at a time.
/// </summary>
public sealed class Simulation
{
    private Dictionary<ulong, VehicleAuthority> _vehicles = new();
    private bool _developmentStart;
    /// <summary>
    /// Initializes a new instance of the <see cref="Simulation"/> class.
    /// </summary>
    /// <param name="configuration">The fixed-step simulation configuration.</param>
    /// <param name="respawn">Optional authoritative respawning; absent for isolated movement/replay fixtures.</param>
    /// <param name="arena">Validated spawn contract, defaulting to the production arena.</param>
    /// <param name="match">Optional authoritative match rules; enabled in multiplayer arenas.</param>
    public Simulation(SimulationConfiguration configuration, RespawnConfiguration? respawn = null, Arenas.ArenaConfiguration? arena = null, Matches.MatchConfiguration? match = null)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        respawn?.Validate();
        Respawn = respawn;
        Arena = arena ?? Arenas.PrototypeArena.Configuration;
        match?.Validate();
        MatchRules = match;
        if (match is not null)
        {
            State = new SimulationState(0, default, match: new Matches.MatchState(0, 0, match.KillTarget, Matches.MatchPhase.Waiting, null, null, []));
        }

    }

    /// <summary>
    /// Gets the fixed-step configuration used by this simulation.
    /// </summary>
    public SimulationConfiguration Configuration { get; }
    /// <summary>Committed diagnostic outcomes.</summary>
    public EventStream Events { get; set; } = new();

    /// <summary>Host lifecycle tuning; null disables automatic respawn in isolated fixtures.</summary>
    public RespawnConfiguration? Respawn { get; private set; }
    /// <summary>Validated configured arena markers.</summary>
    public Arenas.ArenaConfiguration Arena { get; }
    /// <summary>Optional Core-owned match rules.</summary>
    public Matches.MatchConfiguration? MatchRules { get; private set; }
    /// <summary>Committed lifecycle boundaries for scoring and other observers; never emitted by rejected batches.</summary>
    public IReadOnlyList<VehicleSnapshot> LifecycleChanges { get; private set; } = Array.Empty<VehicleSnapshot>();

    /// <summary>
    /// Gets the current authoritative simulation state.
    /// </summary>
    public SimulationState State { get; private set; }

    /// <summary>Completed application handoff for a newly initialized match.</summary>
    public Matches.SynchronizedMatchContext? MatchEntry { get; private set; }

    /// <summary>
    /// Consumes one ordered logical input frame and advances the simulation by exactly one tick.
    /// </summary>
    /// <returns>The authoritative state after the step.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the input tick is not the next simulation tick.
    /// </exception>
    /// <param name="input">The engine-independent logical input for this step.</param>
    public SimulationState Step(InputFrame input)
    {
        Step(input, Array.Empty<VehicleStepRequest>());
        return State;
    }

    /// <summary>Hands completed application loading into the existing Game Loop initialization contract.</summary>
    /// <param name="context">Trusted completed loading/synchronization boundary.</param>
    /// <returns>Whether initialization was accepted without replacing live match rules.</returns>
    public bool InitializeMatch(Matches.SynchronizedMatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (MatchEntry is not null || State.Tick != 0 || context.Tick != State.Tick || State.Match?.Phase != Matches.MatchPhase.Waiting ||
            !context.Participants.ToHashSet().SetEquals(State.Vehicles.Select(vehicle => vehicle.VehicleId)))
        {
            return false;
        }

        // Initialize once, then transfer the immutable boundary to the existing atomic match adapter.
        // The adapter remains the sole phase owner; no parallel GameLoop is advanced.
        var entry = new Matches.GameLoop(true);
        if (!entry.Initialize(context))
        {
            return false;
        }

        MatchEntry = entry.Context;
        return true;
    }

    /// <summary>Registers vehicle gameplay ownership before the first simulation tick.</summary>
    /// <param name="vehicleId">Unique stable identity.</param>
    /// <param name="movement">Movement tuning with the simulation's fixed rate.</param>
    /// <param name="damage">Damage tuning.</param>
    /// <param name="initial">Initial native-independent pose and velocities.</param>
    public void AddVehicle(ulong vehicleId, VehicleConfiguration movement, DamageConfiguration damage, VehiclePhysicsState initial)
    {
        ArgumentNullException.ThrowIfNull(movement);
        ArgumentNullException.ThrowIfNull(damage);
        if (State.Tick != 0 || _vehicles.ContainsKey(vehicleId) || movement.TicksPerSecond != Configuration.TicksPerSecond)
        {
            throw new ArgumentException("Vehicles require unique identities and matching rates before simulation starts.");
        }

        var authority = new VehicleAuthority(vehicleId, movement, damage, initial);
        Matches.MatchState? match = State.Match is null ? null : Matches.MatchAuthority.Join(State.Match, State.Tick, vehicleId);
        _vehicles.Add(vehicleId, authority);
        Events.Record(EventCategory.Lifecycle, "Spawned", target: vehicleId, life: 1, tick: State.Tick);
        State = new SimulationState(State.Tick, State.LastInput, _vehicles.Values.Select(vehicle => vehicle.Snapshot), match);
    }

    /// <summary>Reads an immutable aggregate; callers cannot mutate the private authority owner.</summary>
    /// <returns>Latest committed state.</returns>
    /// <param name="vehicleId">Registered vehicle.</param>
    public VehicleSnapshot GetVehicle(ulong vehicleId) => _vehicles[vehicleId].Snapshot;

    /// <summary>Registers a joining vehicle at the current fixed boundary without rewinding the world.</summary>
    /// <param name="vehicleId">New identity; the session must never reuse departed identities.</param>
    /// <param name="movement">Movement tuning matching the world rate.</param>
    /// <param name="damage">Health tuning.</param>
    /// <param name="initial">Spawn pose and velocities.</param>
    public void JoinVehicle(ulong vehicleId, VehicleConfiguration movement, DamageConfiguration damage, VehiclePhysicsState initial)
    {
        if (_vehicles.ContainsKey(vehicleId) || movement.TicksPerSecond != Configuration.TicksPerSecond)
        {
            throw new ArgumentException("Joining vehicles require unique identities and matching rates.");
        }

        var authority = new VehicleAuthority(vehicleId, movement, damage, initial);
        authority.Commit(new VehicleSnapshot(vehicleId, 1, new VehicleState(State.Tick, initial, false, false, 0, 0), authority.Snapshot.Damage, initial));
        Matches.MatchState? match = State.Match is null ? null : Matches.MatchAuthority.Join(State.Match, State.Tick, vehicleId);
        _vehicles.Add(vehicleId, authority);
        Events.Record(EventCategory.Lifecycle, "Spawned", target: vehicleId, life: 1, tick: State.Tick);
        State = new SimulationState(State.Tick, State.LastInput, _vehicles.Values.Select(vehicle => vehicle.Snapshot), match);
    }

    /// <summary>Removes a departed vehicle at a fixed boundary.</summary>
    /// <param name="vehicleId">Departed identity.</param>
    public void LeaveVehicle(ulong vehicleId)
    {
        if (_vehicles.Remove(vehicleId))
        {
            Events.Record(EventCategory.Lifecycle, "Despawned", target: vehicleId, tick: State.Tick);
        }

        State = new SimulationState(State.Tick, State.LastInput, _vehicles.Values.Select(vehicle => vehicle.Snapshot), State.Match);
    }

    /// <summary>Advances every vehicle and the global clock atomically from one ordered batch.</summary>
    /// <returns>Accepted commands/events, in vehicle identity order.</returns>
    /// <param name="input">Next global input tick.</param>
    /// <param name="requests">Exactly one observation/input request for every registered vehicle.</param>
    public IReadOnlyList<VehicleStepResult> Step(InputFrame input, IReadOnlyList<VehicleStepRequest> requests) => Step(input, requests, null);

    /// <summary>Atomically restores the global tick/input and all vehicle aggregates using the registered tuning.</summary>
    /// <param name="state">Complete synchronization boundary; all registered vehicles must be present.</param>
    public void Restore(SimulationState state)
    {
        if ((state.Match is null) != (MatchRules is null) || (state.Match is not null &&
            (state.Match.KillTarget != MatchRules!.KillTarget || (state.Match.Phase != Matches.MatchPhase.Finished && state.Vehicles.Any(vehicle => !state.Match.Players.Any(player => player.Player == vehicle.VehicleId))))))
        {
            throw new ArgumentException("Restoration requires the complete configured match state.", nameof(state));
        }

        if (state.Vehicles.Count != _vehicles.Count || state.Vehicles.Any(vehicle => !_vehicles.ContainsKey(vehicle.VehicleId)))
        {
            throw new ArgumentException("Restoration requires exactly the registered vehicle set.", nameof(state));
        }

        foreach (VehicleSnapshot vehicle in state.Vehicles)
        {
            _vehicles[vehicle.VehicleId].ValidateRestore(vehicle, state.Tick);
        }

        foreach (VehicleSnapshot vehicle in state.Vehicles)
        {
            _vehicles[vehicle.VehicleId].Commit(vehicle);
        }

        State = state;
        LifecycleChanges = Array.Empty<VehicleSnapshot>();
    }

    /// <summary>Commits staged item outcomes in causal order only after the whole world batch succeeds.</summary>
    /// <param name="input">Next tick.</param>
    /// <param name="requests">Complete vehicle request batch.</param>
    /// <param name="precedingEvents">Authority-staged item outcomes.</param>
    /// <returns>Committed vehicle results.</returns>
    internal IReadOnlyList<VehicleStepResult> Step(InputFrame input, IReadOnlyList<VehicleStepRequest> requests, IReadOnlyList<RuntimeEvent>? precedingEvents)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ulong nextTick = checked(State.Tick + 1);
        if (input.Tick != nextTick || requests.Count != _vehicles.Count ||
            requests.Any(request => request is null || request.Input.Tick != nextTick || !_vehicles.ContainsKey(request.VehicleId)) ||
            requests.Select(request => request.VehicleId).Distinct().Count() != requests.Count)
        {
            throw new ArgumentException("The input frame must target the next simulation tick.", nameof(input));
        }

        bool Participates(ulong id) => !_vehicles.TryGetValue(id, out var vehicle) || vehicle.Snapshot.CanInteract;
        var reserved = State.Vehicles.ToDictionary(vehicle => vehicle.VehicleId);
        VehicleStepResult[] candidates = requests.OrderBy(request => request.VehicleId).Select(request =>
        {
            var observation = new VehicleObservation(request.Observation.Physics, request.Observation.Support, request.Observation.Contacts.Where(contact => Participates(contact.OtherVehicleId)), request.Observation.Surface, request.Observation.Wheels);
            var filtered = new VehicleStepRequest(request.VehicleId, request.Input, observation, request.Effects.Where(effect => Participates(effect.Attribution.InstigatorId)), request.Reset, request.Repair, request.RepairCause);
            VehicleStepResult candidate = _vehicles[request.VehicleId].Prepare(filtered, Respawn, Arena, reserved.Values.ToArray());
            reserved[request.VehicleId] = candidate.Snapshot;
            return candidate;
        }).ToArray();
        VehicleSnapshot[] transitions = candidates.Select(result => result.Snapshot)
            .Where(state => state.Lifecycle != _vehicles[state.VehicleId].Snapshot.Lifecycle || state.LifeId != _vehicles[state.VehicleId].Snapshot.LifeId).ToArray();
        Matches.MatchState? match = State.Match is null ? null : Matches.MatchAuthority.Advance(State.Match, _developmentStart ? MatchRules! with { MinimumPlayers = 1 } : MatchRules!, nextTick, candidates);
        if (match?.Phase is Matches.MatchPhase.Active or Matches.MatchPhase.Finished)
        {
            _developmentStart = false;
        }

        var next = new SimulationState(nextTick, input, candidates.Select(result => result.Snapshot), match);
        foreach (VehicleStepResult result in candidates)
        {
            _vehicles[result.Snapshot.VehicleId].Commit(result.Snapshot);
        }

        Events.AdvanceTime(nextTick * 1000 / (ulong)Configuration.TicksPerSecond);
        foreach (var outcome in precedingEvents ?? [])
        {
            Events.Record(outcome.Category, outcome.Kind, outcome.Actor, outcome.Target, outcome.Cause, tick: nextTick);
        }

        foreach (var result in candidates)
        {
            var vehicle = result.Snapshot;
            float hp = requests.Single(request => request.VehicleId == vehicle.VehicleId).Reset.HasValue ? vehicle.Damage.MaxHP : State.Vehicles.Single(value => value.VehicleId == vehicle.VehicleId).Damage.CurrentHP;
            foreach (var damage in result.DamageEvents)
            {
                hp = Math.Max(0, hp - damage.Amount);
                Events.Record(EventCategory.Damage, "Applied", damage.Attribution.InstigatorId, vehicle.VehicleId, SafeCause(damage.Attribution), amount: damage.Amount, hp: hp, maxHP: vehicle.Damage.MaxHP, life: vehicle.LifeId, tick: nextTick);
            }

            if (!result.Reset && vehicle.CanInteract && vehicle.Damage.CurrentHP > hp)
            {
                Events.Record(EventCategory.Healing, "Applied", target: vehicle.VehicleId, cause: requests.Single(request => request.VehicleId == vehicle.VehicleId).RepairCause, amount: vehicle.Damage.CurrentHP - hp, hp: vehicle.Damage.CurrentHP, maxHP: vehicle.Damage.MaxHP, life: vehicle.LifeId, tick: nextTick);
            }
        }

        foreach (var vehicle in transitions)
        {
            var lethal = vehicle.Damage.LastDamage;
            bool scoredKill = match is not null && State.Match?.Revision != match.Revision &&
                match.Changes.Any(death => death.Victim == vehicle.VehicleId && death.Life == vehicle.LifeId && death.Killer != 0);
            Events.Record(EventCategory.Lifecycle, vehicle.CanInteract ? "Respawned" : vehicle.Lifecycle.ToString(), lethal?.Attribution.InstigatorId ?? 0, vehicle.VehicleId, lethal is null ? string.Empty : SafeCause(lethal.Attribution), context: scoredKill ? "Scored kill" : string.Empty, life: vehicle.LifeId, tick: nextTick);
        }

        if (match is not null && State.Match?.Revision != match.Revision)
        {
            foreach (var death in match.Changes.Where(death => death.Killer != 0))
            {
                var victim = candidates.Single(result => result.Snapshot.VehicleId == death.Victim).Snapshot;
                Events.Record(EventCategory.Lifecycle, "Kill", death.Killer, death.Victim, SafeCause(victim.Damage.LastDamage!.Attribution), life: death.Life, tick: nextTick);
            }
        }

        if (match is not null && State.Match?.Phase != match.Phase)
        {
            Events.Record(EventCategory.Match, match.Phase.ToString(), actor: match.Winner ?? 0, tick: nextTick);
        }

        State = next;
        LifecycleChanges = Array.AsReadOnly(transitions);

        return Array.AsReadOnly(candidates);
    }

    /// <summary>Arms a one-match minimum-player override through the ordinary countdown lifecycle.</summary>
    /// <returns>Whether the operation was accepted.</returns>
    internal bool ForceStart()
    {
        if (State.Match?.Phase != Matches.MatchPhase.Waiting || State.Vehicles.Count == 0)
        {
            return false;
        }

        _developmentStart = true;
        return true;
    }

    /// <summary>Replaces tuning atomically while preserving lives, damage history, physics and score state.</summary>
    /// <param name="configuration">Validated effective gameplay tuning.</param>
    internal void ApplyConfiguration(Development.GameplayConfiguration configuration)
    {
        configuration.Validate();
        var match = State.Match;
        if (match is not null && configuration.Match != MatchRules)
        {
            if (configuration.Match.KillTarget != match.KillTarget &&
                (match.Phase == Matches.MatchPhase.Finished || match.Players.Any(player => player.Kills >= configuration.Match.KillTarget)))
            {
                throw new ArgumentException("Kill target must exceed existing scores and cannot change a finished result.");
            }

            ulong? deadline = match.CountdownAtTick;
            if (deadline.HasValue && configuration.Match.CountdownTicks != MatchRules!.CountdownTicks)
            {
                deadline = checked(State.Tick + configuration.Match.CountdownTicks);
            }

            match = new Matches.MatchState(State.Tick, checked(match.Revision + 1), configuration.Match.KillTarget, match.Phase, deadline, match.Winner, match.Players);
        }

        var vehicles = _vehicles.ToDictionary(pair => pair.Key, pair => pair.Value.Retune(configuration.Vehicle, configuration.Damage));
        var state = new SimulationState(State.Tick, State.LastInput, vehicles.Values.Select(vehicle => vehicle.Snapshot), match);
        _vehicles = vehicles;
        Respawn = Respawn is null ? null : configuration.Respawn;
        MatchRules = MatchRules is null ? null : configuration.Match;
        State = state;
    }

    private static string SafeCause(DamageContext attribution) => attribution.Source switch
    {
        "missile" => "Missile",
        "collision" => attribution.InstigatorId == 0 ? "map collision" : "vehicle collision",
        "explosion" => "explosion",
        _ => "gameplay effect",
    };
}
