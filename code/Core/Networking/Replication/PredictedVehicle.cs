using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Networking.Replication;

/// <summary>Reuses the authoritative simulation for immediate prediction and ordered replay.</summary>
public sealed class PredictedVehicle
{
    /// <summary>At most 300 ms of speculative movement ahead of confirmed input; longer stalls retain controls for transport but hold prediction.</summary>
    public const int MaximumPredictionSteps = 18;
    private readonly Simulation.Simulation _world = new(new SimulationConfiguration(HostVehicleSession.TickRate));
    private readonly ulong _vehicle;
    private ulong _lastSnapshotTick;
    private Development.GameplayConfiguration _configuration;
    private readonly Dictionary<uint, (InputFrame Input, VehicleSnapshot State)> _predicted = new(MaximumPredictionSteps);

    /// <summary>Initializes from an authoritative spawn boundary.</summary>
    /// <param name="initial">Assigned local vehicle.</param>
    /// <param name="configuration">Validated effective gameplay tuning.</param>
    public PredictedVehicle(ReplicatedVehicle initial, Development.GameplayConfiguration? configuration = null)
    {
        _configuration = configuration ?? new() { Damage = new() { MaxHP = initial.State.Damage.MaxHP } };
        _vehicle = initial.State.VehicleId;
        _world.AddVehicle(_vehicle, _configuration.Vehicle, _configuration.Damage, initial.State.ObservedPhysics);
        History = new InputHistory(initial.AcknowledgedInput);
        Restore(initial.State);
        _lastSnapshotTick = initial.State.Movement.Tick;
    }

    /// <summary>Initializes from authority and adopts inputs retained while that first snapshot was in flight.</summary>
    /// <param name="initial">Assigned local vehicle.</param>
    /// <param name="history">Existing sequenced input history created after reliable assignment.</param>
    /// <param name="observe">Synchronous replay-capable external collision seam.</param>
    /// <param name="configuration">Validated effective gameplay tuning.</param>
    public PredictedVehicle(ReplicatedVehicle initial, InputHistory history, Func<VehicleSnapshot, VehicleObservation> observe, Development.GameplayConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(observe);
        if (!history.CanAcknowledge(initial.AcknowledgedInput))
        {
            throw new ArgumentException("The authoritative acknowledgement is outside the retained input history.", nameof(initial));
        }

        _vehicle = initial.State.VehicleId;
        _configuration = configuration ?? new() { Damage = new() { MaxHP = initial.State.Damage.MaxHP } };
        _world.AddVehicle(_vehicle, _configuration.Vehicle, _configuration.Damage, initial.State.ObservedPhysics);
        History = history;
        Restore(initial.State);
        History.Acknowledge(initial.AcknowledgedInput);
        foreach (SequencedInput input in History.Pending.Take(MaximumPredictionSteps))
        {
            Step(input, observe);
        }

        _lastSnapshotTick = initial.State.Movement.Tick;
    }

    /// <summary>Bounded outstanding commands.</summary>
    public InputHistory History { get; }
    /// <summary>Latest predicted simulation state; render smoothing never mutates this state.</summary>
    public VehicleSnapshot State => _world.GetVehicle(_vehicle);
    /// <summary>Distance between old and corrected present-time predictions after replay.</summary>
    public float PredictionError { get; private set; }
    /// <summary>Authority has fallen beyond the speculative movement horizon; acknowledgements are required to advance again.</summary>
    public bool IsPredictionLimited => History.Pending.Count > MaximumPredictionSteps;
    /// <summary>Exact historical movement confirmations that required no external collision replay.</summary>
    public long ConfirmedPredictions { get; private set; }

    /// <summary>Installs host tuning without discarding acknowledged input history or inventing health changes.</summary>
    /// <param name="configuration">Validated effective gameplay tuning.</param>
    public void ApplyConfiguration(Development.GameplayConfiguration configuration)
    {
        _world.ApplyConfiguration(configuration);
        _configuration = configuration;
        _predicted.Clear();
    }

    /// <summary>Predicts before any reply from the host is needed.</summary>
    /// <param name="input">Immediately captured logical input.</param>
    /// <param name="observe">Same collision adapter used by host simulation.</param>
    public void Predict(InputFrame input, Func<VehicleSnapshot, VehicleObservation> observe)
    {
        SequencedInput retained = History.Add(input);
        if (!IsPredictionLimited) { Step(retained, observe); }
    }

    /// <summary>Applies authority, retires confirmed inputs, then replays remaining commands in sequence order.</summary>
    /// <returns>Whether the snapshot was accepted.</returns>
    /// <param name="authoritative">New host boundary for this vehicle.</param>
    /// <param name="observe">Synchronous replay-capable external collision seam.</param>
    public bool Reconcile(ReplicatedVehicle authoritative, Func<VehicleSnapshot, VehicleObservation> observe)
    {
        if (authoritative.State.VehicleId != _vehicle || authoritative.State.Movement.Tick <= _lastSnapshotTick ||
            !History.CanAcknowledge(authoritative.AcknowledgedInput))
        {
            return false;
        }

        Vector3 before = State.Movement.Physics.Position;
        VehicleSnapshot continuation = State;
        bool confirmed = !IsPredictionLimited &&
            _predicted.TryGetValue(authoritative.AcknowledgedInput, out var predicted) &&
            predicted.State.Movement == authoritative.State.Movement &&
            predicted.State.LifeId == authoritative.State.LifeId && predicted.State.Lifecycle == authoritative.State.Lifecycle &&
            authoritative.State.Effects.Count == 0;
        if (State.LifeId != authoritative.State.LifeId || State.Lifecycle != authoritative.State.Lifecycle)
        {
            History.NeutralizePending();
        }

        Restore(authoritative.State);
        History.Acknowledge(authoritative.AcknowledgedInput);
        if (confirmed && History.Pending.Count > 0 && History.Pending.All(input =>
                _predicted.TryGetValue(input.Sequence, out var cached) && cached.Input == input.Frame))
        {
            // Exact confirmation includes tick and every movement field. Preserve the already
            // predicted suffix, but install host-owned health/lifecycle/landing immediately.
            Restore(new VehicleSnapshot(_vehicle, authoritative.State.LifeId, continuation.Movement,
                authoritative.State.Damage, continuation.ObservedPhysics, lifecycle: authoritative.State.Lifecycle,
                respawnAtTick: authoritative.State.RespawnAtTick, landing: authoritative.State.Landing));
            foreach (uint sequence in _predicted.Keys.Where(sequence => !NetworkSequence.IsNewer(sequence, authoritative.AcknowledgedInput)).ToArray())
            {
                _predicted.Remove(sequence);
            }
            ConfirmedPredictions++;
        }
        else
        {
            _predicted.Clear();
            foreach (SequencedInput input in History.Pending.Take(MaximumPredictionSteps)) { Step(input, observe); }
        }

        _lastSnapshotTick = authoritative.State.Movement.Tick;
        PredictionError = Vector3.Distance(before, State.Movement.Physics.Position);
        return true;
    }

    private void Restore(VehicleSnapshot state) => _world.Restore(new SimulationState(state.Movement.Tick, new InputFrame(state.Movement.Tick, 0, 0, 0, 0, 0, 0), [state]));

    private void Step(SequencedInput input, Func<VehicleSnapshot, VehicleObservation> observe)
    {
        InputFrame frame = input.AtTick(checked(_world.State.Tick + 1));
        VehicleSnapshot previous = State;
        VehiclePhysicsState physics;
        VehicleState movement;
        if (previous.CanInteract)
        {
            VehicleObservation observation = observe(previous);
            physics = observation.Physics;
            var predictor = new VehicleMovement(_configuration.Vehicle, physics);
            predictor.Restore(previous.Movement);
            movement = predictor.Step(frame, physics, observation.Support, true, observation.Surface, observation.Wheels, waterDepth: observation.WaterDepth);
        }
        else
        {
            physics = new VehiclePhysicsState(previous.Movement.Physics.Position, previous.Movement.Physics.Orientation, Vector3.Zero, Vector3.Zero);
            movement = new VehicleState(frame.Tick, physics, false, false, 0, 0);
        }

        // Prediction owns movement only: collision observations cannot kill, heal or respawn a player.
        Restore(new VehicleSnapshot(_vehicle, previous.LifeId, movement, previous.Damage, physics, lifecycle: previous.Lifecycle, respawnAtTick: previous.RespawnAtTick, landing: previous.Landing));
        _predicted[input.Sequence] = (input.Frame, State);
    }
}
