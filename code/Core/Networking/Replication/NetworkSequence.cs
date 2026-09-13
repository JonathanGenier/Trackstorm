namespace Trackstorm.Core.Networking.Replication;

/// <summary>Serial-number arithmetic for bounded windows smaller than half the uint range.</summary>
public static class NetworkSequence
{
    /// <summary>Returns false for equal values and the ambiguous half-range separation.</summary>
    /// <param name="candidate">Potentially newer sequence or wire tick.</param>
    /// <param name="previous">Reference sequence.</param>
    /// <returns>Whether the candidate follows the reference, including wrap to zero.</returns>
    public static bool IsNewer(uint candidate, uint previous) => unchecked(candidate - previous) is > 0 and < 0x80000000;
}
