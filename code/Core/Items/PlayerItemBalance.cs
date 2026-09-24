using System.Collections.Immutable;

namespace Trackstorm.Core.Items;

/// <summary>Detached current-match pickup history for a stable player, independent of vehicle life.</summary>
public sealed record PlayerItemBalance
{
    /// <summary>Stable player/vehicle identity, never a transport peer.</summary>
    public ulong Player { get; init; }
    /// <summary>Normalized credit; one selection spends one unit.</summary>
    public ImmutableDictionary<ItemCategory, decimal> Credits { get; init; } = ItemRegistry.Categories.ToImmutableDictionary(c => c.Identity, _ => 0m);
    /// <summary>Successful pickup counts by category.</summary>
    public ImmutableDictionary<ItemCategory, ulong> Counts { get; init; } = ItemRegistry.Categories.ToImmutableDictionary(c => c.Identity, _ => 0ul);
    /// <summary>Total successful pickups, excluding developer grants.</summary>
    public ulong Total { get; init; }
    /// <summary>Most recently selected item, or None before the first pickup.</summary>
    public HeldItem SelectedItem { get; init; }
    /// <summary>Most recently selected category, absent before the first pickup.</summary>
    public ItemCategory? SelectedCategory => ItemRegistry.Find(SelectedItem)?.Category;

    internal void Validate()
    {
        var keys = ItemRegistry.Categories.Select(c => c.Identity).ToHashSet();
        if (Player == 0 || Credits is null || Counts is null || !keys.SetEquals(Credits.Keys) || !keys.SetEquals(Counts.Keys) ||
            Credits.Values.Any(c => c < -keys.Count || c > keys.Count) || Math.Abs(Credits.Values.Sum()) > 0.00000000000000000001m ||
            Counts.Values.Sum(c => (decimal)c) != Total ||
            (Total == 0 ? SelectedItem != HeldItem.None || Credits.Values.Any(c => c != 0) : SelectedCategory is null || Counts[SelectedCategory.Value] == 0))
        {
            throw new ArgumentException("Invalid per-player item balance.");
        }
    }

    internal PlayerItemBalance Select(ItemSpawnConfiguration configuration, ItemSelectionRandom random)
    {
        var active = ItemRegistry.Categories.Where(c => configuration.CategoryWeights[c.Identity] > 0 &&
            ItemRegistry.All.Any(i => i.Category == c.Identity && configuration.Weights[i.Identity] > 0)).ToArray();
        decimal totalWeight = active.Sum(c => (decimal)configuration.CategoryWeights[c.Identity]);
        var credits = Credits.ToBuilder();
        decimal inactiveCredit = ItemRegistry.Categories.Where(c => !active.Contains(c)).Sum(c => credits[c.Identity]);
        foreach (var category in ItemRegistry.Categories.Where(c => !active.Contains(c))) { credits[category.Identity] = 0; }
        foreach (var category in active)
        {
            credits[category.Identity] += (1 + inactiveCredit) * configuration.CategoryWeights[category.Identity] / totalWeight;
            // Decimal division can leave sub-precision residue after a complete allocation cycle.
            if (Math.Abs(credits[category.Identity]) < 0.000000000000000000000001m) { credits[category.Identity] = 0; }
        }
        var eligible = active.Where(c => credits[c.Identity] > 0).ToArray();
        decimal draw = random.Next(int.MaxValue) / (decimal)int.MaxValue * eligible.Sum(c => credits[c.Identity]);
        var selected = eligible[^1].Identity;
        foreach (var category in eligible)
        {
            if (draw < credits[category.Identity]) { selected = category.Identity; break; }
            draw -= credits[category.Identity];
        }
        credits[selected] -= 1;
        return this with
        {
            Credits = credits.ToImmutable(),
            Counts = Counts.SetItem(selected, checked(Counts[selected] + 1)),
            Total = checked(Total + 1),
            SelectedItem = configuration.SelectItem(random, selected),
        };
    }
}
