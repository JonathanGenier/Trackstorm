using Trackstorm.Core.Input;

namespace Trackstorm.Core.Networking.Replication;

/// <summary>A local input identity independent of the host's simulation clock.</summary>
/// <param name="Sequence">Wrapping command identity.</param>
/// <param name="Frame">Logical input; its tick is rebased onto the consuming simulation.</param>
public readonly record struct SequencedInput(uint Sequence, InputFrame Frame)
{
    /// <summary>Preserves input values and edges while targeting the next simulation tick.</summary>
    /// <param name="tick">Authoritative or predicted tick.</param>
    /// <returns>The same command at the specified tick.</returns>
    public InputFrame AtTick(ulong tick) => new(tick, Frame.Steering, Frame.Accelerate, Frame.Brake, Frame.Held, Frame.Pressed, Frame.Released);
}
