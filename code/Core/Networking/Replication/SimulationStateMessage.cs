using Trackstorm.Core.Input;
using Trackstorm.Core.Simulation;

namespace Trackstorm.Core.Networking.Replication;

/// <summary>
/// Represents the versioned game-replication message for the current authoritative simulation state.
/// </summary>
public readonly record struct SimulationStateMessage
{
    /// <summary>
    /// A message version followed by one versioned logical input frame.
    /// </summary>
    public const int SerializedSize = 1 + InputFrame.SerializedSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="SimulationStateMessage"/> struct.
    /// </summary>
    /// <param name="state">The authoritative state to replicate.</param>
    public SimulationStateMessage(SimulationState state)
    {
        if (state.Vehicles.Count != 0)
        {
            throw new ArgumentException("The legacy version-one envelope carries tick/input only; encode vehicle aggregates explicitly.", nameof(state));
        }

        State = state;
    }

    /// <summary>
    /// Gets the authoritative state carried by this game-specific replication message.
    /// </summary>
    public SimulationState State { get; }

    /// <summary>
    /// Writes the exact version-one representation independently of any native transport.
    /// </summary>
    /// <param name="destination">Exactly <see cref="SerializedSize"/> bytes.</param>
    public void Write(Span<byte> destination)
    {
        if (destination.Length != SerializedSize)
        {
            throw new ArgumentException("A simulation-state message requires exactly 22 bytes.", nameof(destination));
        }

        destination[0] = 1;
        State.LastInput.Write(destination[1..]);
    }

    /// <summary>
    /// Reads and validates one version-one game-replication message.
    /// </summary>
    /// <param name="source">Exactly one encoded simulation-state message.</param>
    /// <returns>The decoded Core-owned replication message.</returns>
    public static SimulationStateMessage Read(ReadOnlySpan<byte> source)
    {
        if (source.Length != SerializedSize || source[0] != 1)
        {
            throw new ArgumentException("Expected one version-one simulation-state message.", nameof(source));
        }

        InputFrame input = InputFrame.Read(source[1..]);
        return new SimulationStateMessage(new SimulationState(input.Tick, input));
    }
}
