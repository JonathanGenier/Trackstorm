namespace Trackstorm.Core.Simulation;

/// <summary>
/// Represents the authoritative state produced by the fixed-step simulation foundation.
/// </summary>
/// <param name="Tick">The number of completed simulation steps.</param>
/// <param name="LastInput">The logical input consumed by the most recent step.</param>
public readonly record struct SimulationState(long Tick, LogicalInput LastInput);
