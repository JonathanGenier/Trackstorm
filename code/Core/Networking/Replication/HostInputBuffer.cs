using Trackstorm.Core.Input;

namespace Trackstorm.Core.Networking.Replication;

/// <summary>Validates redundant input windows and consumes at most one command per host tick.</summary>
public sealed class HostInputBuffer
{
    private readonly Dictionary<uint, SequencedInput> _pending = new();
    private uint _newestPacket;
    private int _missingTicks;
    private InputFrame _held;

    /// <summary>Initializes the command origin, including deterministic wrap-boundary scenarios.</summary>
    /// <param name="initialSequence">Last identity before this input stream begins.</param>
    public HostInputBuffer(uint initialSequence = 0)
    {
        _newestPacket = initialSequence;
        LastAcknowledged = initialSequence;
    }

    /// <summary>Last command consumed or explicitly retired after a loss gap.</summary>
    public uint LastAcknowledged { get; private set; }

    /// <summary>Accepts only ordered, bounded windows newer than the last accepted packet.</summary>
    /// <param name="inputs">Current and recent commands in ascending sequence order.</param>
    /// <returns>False for malformed, stale, duplicate or excessive-future input.</returns>
    public bool Receive(IReadOnlyList<SequencedInput> inputs)
    {
        if (inputs.Count is < 1 or > InputHistory.Redundancy || !NetworkSequence.IsNewer(inputs[^1].Sequence, _newestPacket))
        {
            return false;
        }

        for (int i = 0; i < inputs.Count; i++)
        {
            uint sequence = inputs[i].Sequence;
            if ((i > 0 && sequence != unchecked(inputs[i - 1].Sequence + 1)) ||
                (NetworkSequence.IsNewer(sequence, LastAcknowledged) && unchecked(sequence - LastAcknowledged) > InputHistory.Capacity))
            {
                return false;
            }
        }

        _newestPacket = inputs[^1].Sequence;
        foreach (SequencedInput input in inputs)
        {
            if (NetworkSequence.IsNewer(input.Sequence, LastAcknowledged))
            {
                _pending.TryAdd(input.Sequence, input);
            }
        }

        return true;
    }

    /// <summary>Consumes the next command, briefly waits for redundancy, then retires a lost gap.</summary>
    /// <param name="tick">Next host simulation tick.</param>
    /// <returns>One command, briefly held controls without repeated edges, or neutral input after 250 ms silence.</returns>
    public InputFrame Consume(ulong tick)
    {
        uint next = unchecked(LastAcknowledged + 1);
        if (!_pending.ContainsKey(next))
        {
            _missingTicks = Math.Min(16, _missingTicks + 1);
        }

        if (!_pending.ContainsKey(next) && _missingTicks >= 3 && _pending.Count > 0)
        {
            next = _pending.Keys.MinBy(sequence => unchecked(sequence - LastAcknowledged));
        }

        if (_pending.Remove(next, out SequencedInput input))
        {
            LastAcknowledged = next;
            _missingTicks = 0;
            _held = input.AtTick(tick);
            return _held;
        }

        return _missingTicks > 15 ? new InputFrame(tick, 0, 0, 0, 0, 0, 0)
            : new InputFrame(tick, _held.Steering, _held.Accelerate, _held.Brake, _held.Held, 0, 0);
    }
}
