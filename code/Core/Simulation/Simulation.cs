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
        State = new SimulationState(State.Tick, State.LastInput, _vehicles.Values.Select(vehicle => vehicle.Snapshot), match);
    }

    /// <summary>Removes a departed vehicle at a fixed boundary.</summary>
    /// <param name="vehicleId">Departed identity.</param>
    public void LeaveVehicle(ulong vehicleId)
    {
        _vehicles.Remove(vehicleId);
        State = new SimulationState(State.Tick, State.LastInput, _vehicles.Values.Select(vehicle => vehicle.Snapshot), State.Match);
    }

    /// <summary>Advances every vehicle and the global clock atomically from one ordered batch.</summary>
    /// <returns>Accepted commands/events, in vehicle identity order.</returns>
    /// <param name="input">Next global input tick.</param>
    /// <param name="requests">Exactly one observation/input request for every registered vehicle.</param>
    public IReadOnlyList<VehicleStepResult> Step(InputFrame input, IReadOnlyList<VehicleStepRequest> requests)
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
            var filtered = new VehicleStepRequest(request.VehicleId, request.Input, observation, request.Effects.Where(effect => Participates(effect.Attribution.InstigatorId)), request.Reset, request.Repair);
            VehicleStepResult candidate = _vehicles[request.VehicleId].Prepare(filtered, Respawn, Arena, reserved.Values.ToArray());
            reserved[request.VehicleId] = candidate.Snapshot;
            return candidate;
        }).ToArray();
        VehicleSnapshot[] transitions = candidates.Select(result => result.Snapshot)
            .Where(state => state.Lifecycle != _vehicles[state.VehicleId].Snapshot.Lifecycle || state.LifeId != _vehicles[state.VehicleId].Snapshot.LifeId).ToArray();
        Matches.MatchState? match = State.Match is null ? null : Matches.MatchAuthority.Advance(State.Match, _developmentStart ? MatchRules! with { MinimumPlayers = 1 } : MatchRules!, nextTick, candidates.Select(result => result.Snapshot).ToArray());
        if (match?.Phase is Matches.MatchPhase.Active or Matches.MatchPhase.Finished)
        {
            _developmentStart = false;
        }

        var next = new SimulationState(nextTick, input, candidates.Select(result => result.Snapshot), match);
        foreach (VehicleStepResult result in candidates)
        {
            _vehicles[result.Snapshot.VehicleId].Commit(result.Snapshot);
        }

        State = next;
        LifecycleChanges = Array.AsReadOnly(transitions);

        return Array.AsReadOnly(candidates);
    }

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

}
