namespace Trackstorm.Core.Simulation;

/// <summary>
/// Represents the engine-independent logical input consumed by one simulation step.
/// </summary>
/// <param name="IsActive">Whether the demonstration input is active for the step.</param>
public readonly record struct LogicalInput(bool IsActive);
