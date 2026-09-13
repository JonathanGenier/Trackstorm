using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Simulation;

/// <summary>
/// Owns and advances authoritative state one caller-controlled fixed step at a time.
/// </summary>
public sealed class Simulation
{
    private readonly Dictionary<ulong, VehicleAuthority> _vehicles = new();
    /// <summary>
    /// Initializes a new instance of the <see cref="Simulation"/> class.
    /// </summary>
    /// <param name="configuration">The fixed-step simulation configuration.</param>
    public Simulation(SimulationConfiguration configuration)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// Gets the fixed-step configuration used by this simulation.
    /// </summary>
    public SimulationConfiguration Configuration { get; }

    /// <summary>
    /// Gets the current authoritative simulation state.
    /// </summary>
    public SimulationState State { get; private set; }

    /// <summary>
    /// Consumes one ordered logical input frame and advances the simulation by exactly one tick.
    /// </summary>
    /// <param name="input">The engine-independent logical input for this step.</param>
    /// <returns>The authoritative state after the step.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the input tick is not the next simulation tick.
    /// </exception>
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

        _vehicles.Add(vehicleId, new VehicleAuthority(vehicleId, movement, damage, initial));
        State = new SimulationState(State.Tick, State.LastInput, _vehicles.Values.Select(vehicle => vehicle.Snapshot));
    }

    /// <summary>Reads an immutable aggregate; callers cannot mutate the private authority owner.</summary>
    /// <param name="vehicleId">Registered vehicle.</param>
    /// <returns>Latest committed state.</returns>
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
        _vehicles.Add(vehicleId, authority);
        State = new SimulationState(State.Tick, State.LastInput, _vehicles.Values.Select(vehicle => vehicle.Snapshot));
    }

    /// <summary>Removes a departed vehicle at a fixed boundary.</summary>
    /// <param name="vehicleId">Departed identity.</param>
    public void LeaveVehicle(ulong vehicleId)
    {
        _vehicles.Remove(vehicleId);
        State = new SimulationState(State.Tick, State.LastInput, _vehicles.Values.Select(vehicle => vehicle.Snapshot));
    }

    /// <summary>Advances every vehicle and the global clock atomically from one ordered batch.</summary>
    /// <param name="input">Next global input tick.</param>
    /// <param name="requests">Exactly one observation/input request for every registered vehicle.</param>
    /// <returns>Accepted commands/events, in vehicle identity order.</returns>
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

        VehicleStepResult[] candidates = requests.OrderBy(request => request.VehicleId).Select(request => _vehicles[request.VehicleId].Prepare(request)).ToArray();
        var next = new SimulationState(nextTick, input, candidates.Select(result => result.Snapshot));
        foreach (VehicleStepResult result in candidates)
        {
            _vehicles[result.Snapshot.VehicleId].Commit(result.Snapshot);
        }

        State = next;
        return Array.AsReadOnly(candidates);
    }

    /// <summary>Atomically restores the global tick/input and all vehicle aggregates using the registered tuning.</summary>
    /// <param name="state">Complete synchronization boundary; all registered vehicles must be present.</param>
    public void Restore(SimulationState state)
    {
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
    }
}
