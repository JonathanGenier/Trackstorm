using System.Collections.Immutable;

namespace Trackstorm.Core.Items;

/// <summary>Immutable host pickup tuning and registry-keyed distribution.</summary>
public sealed record ItemSpawnConfiguration
{
    /// <summary>Ten seconds at the authoritative 60 Hz clock.</summary>
    public int CooldownTicks { get; init; } = 600;
    /// <summary>Maximum three-dimensional distance from vehicle center to marker.</summary>
    public float PickupRadius { get; init; } = 3;
    /// <summary>Nonnegative weights for every registered identity; zero excludes an item.</summary>
    public ImmutableDictionary<HeldItem, int> Weights { get; init; } = ItemRegistry.All.ToImmutableDictionary(item => item.Identity, item => item.DefaultWeight);
    /// <summary>Reproducible match selection seed.</summary>
    public int Seed { get; init; } = 1;

    /// <summary>Rejects invalid timing, range, unknown/missing identities and empty or overflowing pools.</summary>
    public void Validate()
    {
        if (CooldownTicks is < 1 or > 216000 || !float.IsFinite(PickupRadius) || PickupRadius is <= 0 or > 10 ||
            Weights is null || Weights.Count != ItemRegistry.All.Count ||
            ItemRegistry.All.Any(item => !Weights.ContainsKey(item.Identity)) ||
            Weights.Values.Any(weight => weight < 0) || Weights.Values.Sum(weight => (long)weight) is <= 0 or > int.MaxValue)
        {
            throw new ArgumentException("Pickup tuning requires bounded cooldown/radius and a nonempty registered weighted pool.");
        }
    }

    /// <summary>Value equality includes distribution contents, independent of dictionary allocation.</summary>
    /// <param name="other">Configuration to compare.</param>
    /// <returns>Whether every tuning value matches.</returns>
    public bool Equals(ItemSpawnConfiguration? other) => other is not null &&
        CooldownTicks == other.CooldownTicks && PickupRadius == other.PickupRadius && Seed == other.Seed &&
        (ReferenceEquals(Weights, other.Weights) || (Weights is not null && other.Weights is not null &&
        Weights.Count == other.Weights.Count && Weights.All(pair => other.Weights.TryGetValue(pair.Key, out int value) && value == pair.Value)));

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CooldownTicks);
        hash.Add(PickupRadius);
        hash.Add(Seed);
        if (Weights is not null)
        {
            foreach (var pair in Weights.OrderBy(pair => pair.Key))
            {
                hash.Add(pair.Key);
                hash.Add(pair.Value);
            }
        }

        return hash.ToHashCode();
    }

    /// <summary>Creates an independent seeded weighted selection stream.</summary>
    /// <returns>A host-only selector.</returns>
    public Func<HeldItem> CreateSelector()
    {
        Validate();
        var random = new ItemRandom(unchecked((ulong)Seed));
        return () => random.Next(this);
    }
}
