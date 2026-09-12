using Trackstorm.Core.Input;

namespace Trackstorm.Core.Simulation;

/// <summary>
/// Represents the authoritative state produced by the fixed-step simulation foundation.
/// </summary>
public readonly record struct SimulationState
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SimulationState"/> struct.
    /// </summary>
    /// <param name="tick">The number of completed simulation steps.</param>
    /// <param name="lastInput">The logical input consumed by the most recent step.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when the state and logical-input ticks do not match.
    /// </exception>
    public SimulationState(ulong tick, InputFrame lastInput)
    {
        if (tick != lastInput.Tick)
        {
            throw new ArgumentException("Authoritative state and logical input must identify the same tick.", nameof(lastInput));
        }

        Tick = tick;
        LastInput = lastInput;
    }

    /// <summary>
    /// Gets the number of completed simulation steps.
    /// </summary>
    public ulong Tick { get; }

    /// <summary>
    /// Gets the logical input consumed by the most recent step.
    /// </summary>
    public InputFrame LastInput { get; }
}
