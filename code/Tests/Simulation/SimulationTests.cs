using Trackstorm.Core.Simulation;

namespace Trackstorm.Core.Tests.Simulation;

/// <summary>
/// Verifies the deterministic fixed-step simulation foundation.
/// </summary>
[TestFixture]
internal sealed class SimulationTests
{
    /// <summary>
    /// Verifies that one explicit step advances exactly one tick.
    /// </summary>
    [Test]
    public void Step_AdvancesExactlyOneTick()
    {
        var simulation = CreateSimulation();

        SimulationState state = simulation.Step(new LogicalInput(false));

        Assert.That(state.Tick, Is.EqualTo(1));
    }

    /// <summary>
    /// Verifies that repeated explicit steps advance by their exact count.
    /// </summary>
    [Test]
    public void Step_WhenRepeated_AdvancesDeterministically()
    {
        var simulation = CreateSimulation();

        simulation.Step(new LogicalInput(false));
        simulation.Step(new LogicalInput(true));
        SimulationState state = simulation.Step(new LogicalInput(false));

        Assert.That(state.Tick, Is.EqualTo(3));
    }

    /// <summary>
    /// Verifies that Core records the logical input consumed by the step.
    /// </summary>
    [Test]
    public void Step_RecordsConsumedLogicalInput()
    {
        var simulation = CreateSimulation();
        var input = new LogicalInput(true);

        SimulationState state = simulation.Step(input);

        Assert.That(state.LastInput, Is.EqualTo(input));
    }

    /// <summary>
    /// Verifies equivalent simulations produce equivalent state for the same input sequence.
    /// </summary>
    [Test]
    public void Step_WithEquivalentInitialStateAndInputs_ProducesEquivalentResults()
    {
        var first = CreateSimulation();
        var second = CreateSimulation();
        LogicalInput[] inputs =
        [
            new LogicalInput(false),
            new LogicalInput(true),
            new LogicalInput(true),
            new LogicalInput(false),
        ];

        foreach (LogicalInput input in inputs)
        {
            first.Step(input);
            second.Step(input);
        }

        Assert.That(first.State, Is.EqualTo(second.State));
    }

    /// <summary>
    /// Verifies that a simulation requires a configuration.
    /// </summary>
    [Test]
    public void Constructor_WithNullConfiguration_Throws()
    {
        Assert.That(
            () => new Trackstorm.Core.Simulation.Simulation(null!),
            Throws.ArgumentNullException);
    }

    private static Trackstorm.Core.Simulation.Simulation CreateSimulation()
    {
        var configuration = new SimulationConfiguration(SimulationConfiguration.DefaultTicksPerSecond);
        return new Trackstorm.Core.Simulation.Simulation(configuration);
    }
}
