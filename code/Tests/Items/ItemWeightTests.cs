using Trackstorm.Core.Development;
using Trackstorm.Core.Items;

namespace Trackstorm.Core.Tests.Items;

/// <summary>Relative item odds remain subordinate to independent category allocation.</summary>
[TestFixture]
internal sealed class ItemWeightTests
{
    [TestCase(1)]
    [TestCase(2)]
    public void EveryCategoryUsesProportionalItemWeightsWithoutChangingCategoryTargets(int firstWeight)
    {
        foreach (var category in ItemRegistry.Categories)
        {
            var pool = ItemRegistry.All.Where(item => item.Category == category.Identity).ToArray();
            var configuration = new ItemSpawnConfiguration();
            configuration = configuration with { Weights = configuration.Weights.SetItem(pool[0].Identity, firstWeight) };
            var counts = pool.ToDictionary(item => item.Identity, _ => 0);
            var random = new ItemSelectionRandom(169);
            var balance = new PlayerItemBalance { Player = 1 };
            for (int i = 0; i < 40000; i++)
            {
                balance = balance.Select(configuration, random);
                if (balance.SelectedCategory == category.Identity) { counts[balance.SelectedItem]++; }
            }

            Assert.That(balance.Counts[ItemCategory.Weapon], Is.EqualTo(20000));
            Assert.That(balance.Counts[ItemCategory.Consumable], Is.EqualTo(10000));
            Assert.That(balance.Counts[ItemCategory.Droppable], Is.EqualTo(10000));
            foreach (var item in pool)
            {
                double expected = configuration.Weights[item.Identity] / (double)(pool.Length - 1 + firstWeight);
                Assert.That(counts[item.Identity] / (double)balance.Counts[category.Identity], Is.EqualTo(expected).Within(0.02), item.Key);
            }
        }
    }

    [Test]
    public void EveryRegisteredWeightHasOneSharedConfigurationControlAndDefaultsToOne()
    {
        var defaults = GameplayConfiguration.HostedDefaults;
        foreach (var item in ItemRegistry.All)
        {
            var option = GameplayOptions.All.Single(option => option.Key == $"spawns.{item.Key}_weight");
            Assert.That(option.Read(defaults), Is.EqualTo(1));
            Assert.That(GameplayOptions.TryApply(defaults, new Dictionary<string, double> { [option.Key] = 7 }, out var tuned, out _), Is.True);
            Assert.That(tuned.Spawns.Weights[item.Identity], Is.EqualTo(7));
            Assert.That(tuned.Spawns.CategoryWeights, Is.EqualTo(defaults.Spawns.CategoryWeights));
            var decoded = GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(169, new(1, tuned)));
            Assert.That(decoded.State.Configuration, Is.EqualTo(tuned));
        }
    }
}
