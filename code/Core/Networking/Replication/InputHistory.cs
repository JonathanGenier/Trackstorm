using Trackstorm.Core.Input;

namespace Trackstorm.Core.Networking.Replication;

/// <summary>Bounded unacknowledged commands shared by retransmission and prediction replay.</summary>
public sealed class InputHistory
{
    /// <summary>Two seconds of inputs at 60 Hz; exhaustion requires session resynchronization.</summary>
    public const int Capacity = 120;
    /// <summary>Current command plus three previous commands per unreliable packet.</summary>
    public const int Redundancy = 4;
    private readonly List<SequencedInput> _pending = new();
    private uint _sequence;

    /// <summary>Initializes a sequence origin, including near-wrap origins for deterministic checks.</summary>
    /// <param name="initialSequence">Last identity before the first input.</param>
    public InputHistory(uint initialSequence = 0)
    {
        _sequence = initialSequence;
        LastAcknowledged = initialSequence;
    }

    /// <summary>Last host-confirmed command identity.</summary>
    public uint LastAcknowledged { get; private set; }
    /// <summary>Identity the next captured frame will receive, including wrap.</summary>
    public uint NextSequence => unchecked(_sequence + 1);
    /// <summary>Ordered immutable view of outstanding commands.</summary>
    public IReadOnlyList<SequencedInput> Pending => _pending.AsReadOnly();
    /// <summary>Whether another command can be retained safely.</summary>
    public bool IsFull => _pending.Count == Capacity;

    /// <summary>Retains one command before it is predicted or sent.</summary>
    /// <param name="frame">Current local input.</param>
    /// <returns>Assigned command identity.</returns>
    public SequencedInput Add(InputFrame frame)
    {
        if (IsFull)
        {
            throw new InvalidOperationException("Prediction history exhausted; reconnect to resynchronize.");
        }

        var input = new SequencedInput(unchecked(++_sequence), frame);
        _pending.Add(input);
        return input;
    }

    /// <summary>Copies the newest bounded redundancy window in replay order.</summary>
    /// <returns>At most four distinct retained inputs.</returns>
    public SequencedInput[] GetRedundancy() => _pending.TakeLast(Redundancy).ToArray();

    /// <summary>Retires old-life controls while preserving sequence acknowledgements and retransmission.</summary>
    public void NeutralizePending()
    {
        for (int i = 0; i < _pending.Count; i++)
        {
            _pending[i] = new SequencedInput(_pending[i].Sequence, new InputFrame(_pending[i].Frame.Tick, 0, 0, 0, 0, 0, 0));
        }
    }

    /// <summary>Checks confirmation ordering without removing any replay state.</summary>
    /// <param name="sequence">Proposed host confirmation.</param>
    /// <returns>Whether the acknowledgement belongs to this history's confirmed/pending range.</returns>
    public bool CanAcknowledge(uint sequence) => sequence == LastAcknowledged ||
        (NetworkSequence.IsNewer(sequence, LastAcknowledged) && (sequence == _sequence || NetworkSequence.IsNewer(_sequence, sequence)));

    /// <summary>Removes only confirmed inputs; stale and impossible future acknowledgements do nothing.</summary>
    /// <param name="sequence">Host-confirmed input.</param>
    /// <returns>Whether this was a valid acknowledgement, including an unchanged one.</returns>
    public bool Acknowledge(uint sequence)
    {
        if (sequence == LastAcknowledged)
        {
            return true;
        }

        if (!CanAcknowledge(sequence))
        {
            return false;
        }

        _pending.RemoveAll(input => input.Sequence == sequence || NetworkSequence.IsNewer(sequence, input.Sequence));
        LastAcknowledged = sequence;
        return true;
    }
}
