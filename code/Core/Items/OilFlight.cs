namespace Trackstorm.Core.Items;

/// <summary>Immutable value-compared flight parcels, bounded per activation.</summary>
public sealed class OilFlight : IEquatable<OilFlight>
{
    public static OilFlight Empty { get; } = new([]);
    public OilFlight(IEnumerable<OilDrop> drops)
    {
        var copy = drops.Take(49).ToArray();
        if (copy.Length > 48) { throw new ArgumentException("Oil flight budget exceeded."); }
        Drops = Array.AsReadOnly(copy);
    }
    public IReadOnlyList<OilDrop> Drops { get; }
    public int Length => Drops.Count;
    public OilDrop this[int index] => Drops[index];
    public bool Equals(OilFlight? other) => other is not null && Drops.SequenceEqual(other.Drops);
    public override bool Equals(object? obj) => obj is OilFlight other && Equals(other);
    public override int GetHashCode() { var hash = new HashCode(); foreach (var drop in Drops) { hash.Add(drop); } return hash.ToHashCode(); }
}
