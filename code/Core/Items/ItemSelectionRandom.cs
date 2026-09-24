namespace Trackstorm.Core.Items;

/// <summary>The host match's single portable stream for sequential item-selection decisions.</summary>
public sealed class ItemSelectionRandom
{
    /// <summary>Starts a stream from a match seed or a saved continuation word.</summary>
    public ItemSelectionRandom(ulong state) => State = state;

    /// <summary>Complete SplitMix64 continuation, captured with the match checkpoint.</summary>
    public ulong State { get; private set; }

    /// <summary>Draws once in [0, exclusiveUpperBound) for any authoritative weighted decision.</summary>
    public int Next(int exclusiveUpperBound)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveUpperBound);
        ulong value;
        unchecked
        {
            State += 0x9E3779B97F4A7C15UL;
            value = State;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            value ^= value >> 31;
        }

        return (int)(value % (ulong)exclusiveUpperBound);
    }

    /// <summary>Restores the exact next draw without advancing or deriving a new seed.</summary>
    internal void Restore(ulong state) => State = state;
}
