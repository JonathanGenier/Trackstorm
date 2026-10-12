namespace Trackstorm.Core.Items;

/// <summary>Immutable, value-compared additional pieces of a single firing activation.</summary>
public sealed class OilFootprint : IEquatable<OilFootprint>
{
    /// <summary>Bounded footprint complexity, excluding the primary patch.</summary>
    public const int MaximumSegments = 63;
    /// <summary>No additional deposited geometry yet.</summary>
    public static OilFootprint Empty { get; } = new([]);

    /// <summary>Copies and validates geometry so a published hazard cannot mutate externally.</summary>
    public OilFootprint(IEnumerable<OilSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        OilSegment[] copy = segments.Take(MaximumSegments + 1).ToArray();
        if (copy.Length > MaximumSegments) { throw new ArgumentException("Oil footprint exceeds its geometry bound."); }
        foreach (var segment in copy) { segment.Validate(); }
        Segments = Array.AsReadOnly(copy);
    }

    /// <summary>Pieces sharing the owning patch's identity, quota, owner and deadline.</summary>
    public IReadOnlyList<OilSegment> Segments { get; }
    public bool Equals(OilFootprint? other) => other is not null && Segments.SequenceEqual(other.Segments);
    public override bool Equals(object? obj) => obj is OilFootprint other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var segment in Segments) { hash.Add(segment); }
        return hash.ToHashCode();
    }
}
