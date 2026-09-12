using Trackstorm.Core.Input;

namespace Trackstorm.Core.Simulation;

/// <summary>
/// Owns and advances authoritative state one caller-controlled fixed step at a time.
/// </summary>
public sealed class Simulation
{
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
        ulong nextTick = checked(State.Tick + 1);
        if (input.Tick != nextTick)
        {
            throw new ArgumentException("The input frame must target the next simulation tick.", nameof(input));
        }

        State = new SimulationState(nextTick, input);
        return State;
    }
}
