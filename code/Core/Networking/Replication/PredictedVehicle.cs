using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Networking.Replication;

/// <summary>Reuses the authoritative simulation for immediate prediction and ordered replay.</summary>
public sealed class PredictedVehicle
{
    private readonly Simulation.Simulation _world = new(new SimulationConfiguration(HostVehicleSession.TickRate));
    private readonly ulong _vehicle;
    private ulong _lastSnapshotTick;

    /// <summary>Initializes from an authoritative spawn boundary.</summary>
    /// <param name="initial">Assigned local vehicle.</param>
    public PredictedVehicle(ReplicatedVehicle initial)
    {
        _vehicle = initial.State.VehicleId;
        _world.AddVehicle(_vehicle, new(), new(), initial.State.ObservedPhysics);
        History = new InputHistory(initial.AcknowledgedInput);
        Restore(initial.State);
        _lastSnapshotTick = initial.State.Movement.Tick;
    }

    /// <summary>Initializes from authority and adopts inputs retained while that first snapshot was in flight.</summary>
    /// <param name="initial">Assigned local vehicle.</param>
    /// <param name="history">Existing sequenced input history created after reliable assignment.</param>
    /// <param name="observe">Synchronous replay-capable external collision seam.</param>
    public PredictedVehicle(ReplicatedVehicle initial, InputHistory history, Func<VehicleSnapshot, VehicleObservation> observe)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(observe);
        if (!history.CanAcknowledge(initial.AcknowledgedInput))
        {
            throw new ArgumentException("The authoritative acknowledgement is outside the retained input history.", nameof(initial));
        }

        _vehicle = initial.State.VehicleId;
        _world.AddVehicle(_vehicle, new(), new(), initial.State.ObservedPhysics);
        History = history;
        Restore(initial.State);
        History.Acknowledge(initial.AcknowledgedInput);
        foreach (SequencedInput input in History.Pending)
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

    /// <summary>Predicts before any reply from the host is needed.</summary>
    /// <param name="input">Immediately captured logical input.</param>
    /// <param name="observe">Same collision adapter used by host simulation.</param>
    public void Predict(InputFrame input, Func<VehicleSnapshot, VehicleObservation> observe) => Step(History.Add(input), observe);

    /// <summary>Applies authority, retires confirmed inputs, then replays remaining commands in sequence order.</summary>
    /// <param name="authoritative">New host boundary for this vehicle.</param>
    /// <param name="observe">Synchronous replay-capable external collision seam.</param>
    /// <returns>Whether the snapshot was accepted.</returns>
    public bool Reconcile(ReplicatedVehicle authoritative, Func<VehicleSnapshot, VehicleObservation> observe)
    {
        if (authoritative.State.VehicleId != _vehicle || authoritative.State.Movement.Tick <= _lastSnapshotTick ||
            !History.CanAcknowledge(authoritative.AcknowledgedInput))
        {
            return false;
        }

        Vector3 before = State.Movement.Physics.Position;
        Restore(authoritative.State);
        History.Acknowledge(authoritative.AcknowledgedInput);
        foreach (SequencedInput input in History.Pending)
        {
            Step(input, observe);
        }

        _lastSnapshotTick = authoritative.State.Movement.Tick;
        PredictionError = Vector3.Distance(before, State.Movement.Physics.Position);
        return true;
    }

    private void Restore(VehicleSnapshot state) => _world.Restore(new SimulationState(state.Movement.Tick, new InputFrame(state.Movement.Tick, 0, 0, 0, 0, 0, 0), [state]));

    private void Step(SequencedInput input, Func<VehicleSnapshot, VehicleObservation> observe)
    {
        InputFrame frame = input.AtTick(checked(_world.State.Tick + 1));
        _world.Step(frame, [new VehicleStepRequest(_vehicle, frame, observe(State))]);
    }
}
