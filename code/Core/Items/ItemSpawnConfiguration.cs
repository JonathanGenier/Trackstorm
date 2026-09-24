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
    /// <summary>Nonnegative category targets; empty item categories are excluded and remaining targets normalized.</summary>
    public ImmutableDictionary<ItemCategory, int> CategoryWeights { get; init; } = ItemRegistry.Categories.ToImmutableDictionary(c => c.Identity, c => c.DefaultWeight);
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
        if (CategoryWeights is null || CategoryWeights.Count != ItemRegistry.Categories.Count ||
            ItemRegistry.Categories.Any(c => !CategoryWeights.ContainsKey(c.Identity)) ||
            CategoryWeights.Values.Any(w => w < 0) || CategoryWeights.Values.Sum(w => (long)w) > int.MaxValue ||
            !ItemRegistry.All.Any(i => Weights[i.Identity] > 0 && CategoryWeights[i.Category] > 0))
        {
            throw new ArgumentException("Category targets require a usable positive registered distribution.");
        }
    }

    /// <summary>Value equality includes distribution contents, independent of dictionary allocation.</summary>
    /// <param name="other">Configuration to compare.</param>
    /// <returns>Whether every tuning value matches.</returns>
    public bool Equals(ItemSpawnConfiguration? other) => other is not null &&
        CooldownTicks == other.CooldownTicks && PickupRadius == other.PickupRadius && Seed == other.Seed &&
        CategoryWeights.Count == other.CategoryWeights.Count && CategoryWeights.All(p => other.CategoryWeights.TryGetValue(p.Key, out int value) && value == p.Value) &&
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

        foreach (var pair in CategoryWeights.OrderBy(p => p.Key)) { hash.Add(pair.Key); hash.Add(pair.Value); }
        return hash.ToHashCode();
    }

    /// <summary>Selects an item with the match's existing authoritative stream.</summary>
    internal HeldItem SelectItem(ItemSelectionRandom random, ItemCategory category)
    {
        Validate();
        ArgumentNullException.ThrowIfNull(random);
        var pool = ItemRegistry.All.Where(i => i.Category == category).ToArray();
        int draw = random.Next((int)pool.Sum(i => (long)Weights[i.Identity]));
        foreach (var definition in pool)
        {
            int weight = Weights[definition.Identity];
            if (draw < weight)
            {
                return definition.Identity;
            }

            draw -= weight;
        }

        throw new InvalidOperationException("Validated item distribution has no selection.");
    }
}
