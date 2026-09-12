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
    /// Consumes one logical input and advances the simulation by exactly one tick.
    /// </summary>
    /// <param name="input">The engine-independent logical input for this step.</param>
    /// <returns>The authoritative state after the step.</returns>
    public SimulationState Step(LogicalInput input)
    {
        State = new SimulationState(checked(State.Tick + 1), input);
        return State;
    }
}
