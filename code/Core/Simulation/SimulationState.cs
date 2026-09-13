using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Simulation;

/// <summary>
/// Represents the authoritative state produced by the fixed-step simulation foundation.
/// </summary>
public readonly record struct SimulationState
{
    private readonly IReadOnlyList<VehicleSnapshot>? _vehicles;
    /// <summary>
    /// Initializes a new instance of the <see cref="SimulationState"/> struct.
    /// </summary>
    /// <param name="tick">The number of completed simulation steps.</param>
    /// <param name="lastInput">The logical input consumed by the most recent step.</param>
    /// <param name="vehicles">Complete immutable vehicle aggregates at this global tick.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when the state and logical-input ticks do not match.
    /// </exception>
    public SimulationState(ulong tick, InputFrame lastInput, IEnumerable<VehicleSnapshot>? vehicles = null)
    {
        if (tick != lastInput.Tick)
        {
            throw new ArgumentException("Authoritative state and logical input must identify the same tick.", nameof(lastInput));
        }

        Tick = tick;
        LastInput = lastInput;
        VehicleSnapshot[] copy = vehicles?.ToArray() ?? [];
        if (copy.Any(vehicle => vehicle is null || vehicle.Movement.Tick != tick) || copy.Select(vehicle => vehicle.VehicleId).Distinct().Count() != copy.Length)
        {
            throw new ArgumentException("Vehicle snapshots must have unique identities and share the simulation tick.", nameof(vehicles));
        }

        _vehicles = copy.Length == 0 ? null : Array.AsReadOnly(copy);
    }

    /// <summary>
    /// Gets the number of completed simulation steps.
    /// </summary>
    public ulong Tick { get; }

    /// <summary>
    /// Gets the logical input consumed by the most recent step.
    /// </summary>
    public InputFrame LastInput { get; }

    /// <summary>Complete vehicle gameplay aggregates; no Client-owned health or movement progression.</summary>
    public IReadOnlyList<VehicleSnapshot> Vehicles => _vehicles ?? Array.Empty<VehicleSnapshot>();
}
