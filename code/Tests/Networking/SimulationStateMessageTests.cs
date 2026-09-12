using Trackstorm.Core.Input;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Simulation;

namespace Trackstorm.Core.Tests.Networking;

/// <summary>
/// Verifies deterministic serialization at the game-specific replication boundary.
/// </summary>
[TestFixture]
internal sealed class SimulationStateMessageTests
{
    /// <summary>
    /// Verifies that a state message has stable bytes and round-trips without a transport runtime.
    /// </summary>
    [Test]
    public void Serialization_RoundTripsKnownRepresentation()
    {
        var input = new InputFrame(
            0x0807060504030201,
            -32767,
            65535,
            32768,
            InputButtons.Leaderboard,
            InputButtons.UseItem,
            InputButtons.Drift);
        var message = new SimulationStateMessage(new SimulationState(input.Tick, input));
        byte[] bytes = new byte[SimulationStateMessage.SerializedSize];
        byte[] expected = [1, 1, 1, 2, 3, 4, 5, 6, 7, 8, 1, 128, 255, 255, 0, 128, 4, 0, 2, 0, 1, 0];

        message.Write(bytes);
        SimulationStateMessage decoded = SimulationStateMessage.Read(bytes);

        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.EqualTo(expected));
            Assert.That(decoded, Is.EqualTo(message));
        });
    }

    /// <summary>
    /// Verifies that wrong lengths and versions are rejected at the replication boundary.
    /// </summary>
    [Test]
    public void Serialization_RejectsMalformedEnvelope()
    {
        byte[] bytes = new byte[SimulationStateMessage.SerializedSize];

        Assert.Multiple(() =>
        {
            Assert.That(() => SimulationStateMessage.Read([]), Throws.ArgumentException);
            Assert.That(() => SimulationStateMessage.Read(bytes), Throws.ArgumentException);
            Assert.That(
                () => default(SimulationStateMessage).Write(new byte[SimulationStateMessage.SerializedSize - 1]),
                Throws.ArgumentException);
        });
    }

    /// <summary>
    /// Verifies authoritative state cannot pair different simulation and input ticks.
    /// </summary>
    [Test]
    public void SimulationState_WithMismatchedInputTick_Throws()
    {
        var input = new InputFrame(2, 0, 0, 0, InputButtons.None, InputButtons.None, InputButtons.None);

        Assert.That(() => new SimulationState(1, input), Throws.ArgumentException);
    }
}
