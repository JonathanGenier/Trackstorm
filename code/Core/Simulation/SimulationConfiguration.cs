namespace Trackstorm.Core.Simulation;

/// <summary>
/// Defines the authoritative timing configuration for a fixed-step simulation.
/// </summary>
public sealed class SimulationConfiguration
{
    /// <summary>
    /// The default number of authoritative simulation steps per second.
    /// </summary>
    public const int DefaultTicksPerSecond = 60;

    /// <summary>
    /// Initializes a new instance of the <see cref="SimulationConfiguration"/> class.
    /// </summary>
    /// <param name="ticksPerSecond">The number of fixed simulation steps per second.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="ticksPerSecond"/> is not positive.
    /// </exception>
    public SimulationConfiguration(int ticksPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerSecond);
        TicksPerSecond = ticksPerSecond;
    }

    /// <summary>
    /// Gets the number of fixed simulation steps per second.
    /// </summary>
    public int TicksPerSecond { get; }
}
