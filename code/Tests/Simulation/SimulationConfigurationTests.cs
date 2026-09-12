using Trackstorm.Core.Simulation;

namespace Trackstorm.Core.Tests.Simulation;

/// <summary>
/// Verifies fixed-step simulation configuration validation.
/// </summary>
[TestFixture]
internal sealed class SimulationConfigurationTests
{
    /// <summary>
    /// Verifies that positive fixed-step rates are accepted.
    /// </summary>
    [Test]
    public void Constructor_WithPositiveTickRate_SetsTickRate()
    {
        const int ticksPerSecond = 30;

        var configuration = new SimulationConfiguration(ticksPerSecond);

        Assert.That(configuration.TicksPerSecond, Is.EqualTo(ticksPerSecond));
    }

    /// <summary>
    /// Verifies that non-positive fixed-step rates are rejected.
    /// </summary>
    /// <param name="ticksPerSecond">The invalid tick rate.</param>
    [TestCase(0)]
    [TestCase(-1)]
    public void Constructor_WithNonPositiveTickRate_Throws(int ticksPerSecond)
    {
        Assert.That(
            () => new SimulationConfiguration(ticksPerSecond),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
