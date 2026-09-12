using Trackstorm.Core.Input;
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

        SimulationState state = simulation.Step(CreateInput(1));

        Assert.That(state.Tick, Is.EqualTo(1));
    }

    /// <summary>
    /// Verifies that repeated explicit steps advance by their exact count.
    /// </summary>
    [Test]
    public void Step_WhenRepeated_AdvancesDeterministically()
    {
        var simulation = CreateSimulation();

        simulation.Step(CreateInput(1));
        simulation.Step(CreateInput(2, InputButtons.Drift));
        SimulationState state = simulation.Step(CreateInput(3));

        Assert.That(state.Tick, Is.EqualTo(3));
    }

    /// <summary>
    /// Verifies that Core records the logical input consumed by the step.
    /// </summary>
    [Test]
    public void Step_RecordsConsumedLogicalInput()
    {
        var simulation = CreateSimulation();
        InputFrame input = CreateInput(1, InputButtons.UseItem);

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
        InputFrame[] inputs =
        [
            CreateInput(1),
            CreateInput(2, InputButtons.Drift),
            CreateInput(3, InputButtons.Drift | InputButtons.UseItem),
            CreateInput(4),
        ];

        foreach (InputFrame input in inputs)
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

    /// <summary>
    /// Verifies that duplicate, skipped, and out-of-order input ticks cannot alter state.
    /// </summary>
    [Test]
    public void Step_WithNonSequentialInputTick_ThrowsWithoutChangingState()
    {
        var simulation = CreateSimulation();
        SimulationState firstState = simulation.Step(CreateInput(1));

        Assert.Multiple(() =>
        {
            Assert.That(() => simulation.Step(CreateInput(1)), Throws.ArgumentException);
            Assert.That(() => simulation.Step(CreateInput(3)), Throws.ArgumentException);
            Assert.That(simulation.State, Is.EqualTo(firstState));
        });
    }

    private static InputFrame CreateInput(ulong tick, InputButtons held = InputButtons.None)
    {
        return new InputFrame(tick, 0, 0, 0, held, InputButtons.None, InputButtons.None);
    }

    private static Trackstorm.Core.Simulation.Simulation CreateSimulation()
    {
        var configuration = new SimulationConfiguration(SimulationConfiguration.DefaultTicksPerSecond);
        return new Trackstorm.Core.Simulation.Simulation(configuration);
    }
}
