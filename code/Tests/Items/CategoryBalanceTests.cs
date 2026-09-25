using System.Collections.Immutable;
using System.Numerics;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Development;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Sessions;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

/// <summary>Per-player allocation, portable continuation and existing authority integration.</summary>
[TestFixture]
internal sealed class CategoryBalanceTests
{
    [Test]
    public void JiraExampleExcludesAndRecoversCategories()
    {
        var configuration = new ItemSpawnConfiguration();
        var expected = new[] { HeldItem.Wrench, HeldItem.Missile, HeldItem.Oil, HeldItem.Missile };
        ulong seed = Enumerable.Range(0, 10000).Select(i => (ulong)i).First(value =>
        {
            var random = new ItemSelectionRandom(value);
            var state = new PlayerItemBalance { Player = 1 };
            return expected.All(item => { state = state.Select(configuration, random); return state.SelectedItem == item; });
        });
        var rng = new ItemSelectionRandom(seed);
        var balance = new PlayerItemBalance { Player = 1 };
        for (int i = 0; i < expected.Length; i++)
        {
            var before = balance;
            balance = balance.Select(configuration, rng);
            Assert.That(balance.SelectedItem, Is.EqualTo(expected[i]));
            Assert.That(before.Credits[balance.SelectedCategory!.Value] + (balance.SelectedCategory == ItemCategory.Weapon ? 0.5m : 0.25m), Is.Positive);
            if (i == 0) { Assert.That(balance.Credits[ItemCategory.Consumable], Is.EqualTo(-0.75m)); }
            if (i == 2) { Assert.That(balance.Credits[ItemCategory.Consumable] + 0.25m, Is.Zero); Assert.That(balance.Credits[ItemCategory.Droppable] + 0.25m, Is.Zero); }
        }
        Assert.That(balance.Credits.Values, Is.All.Zero);
        Assert.That(balance.Counts[ItemCategory.Weapon], Is.EqualTo(2));
        Assert.That(balance.Total, Is.EqualTo(4));
        var fresh = new PlayerItemBalance { Player = 2 };
        var continuation = rng.State;
        Assert.That(balance.Select(configuration, rng).SelectedItem, Is.EqualTo(fresh.Select(configuration, new(continuation)).SelectedItem));
        TestContext.WriteLine($"Jira sequence seed {seed}: Wrench -> Missile -> Oil -> Missile; all credits zero.");
    }

    [TestCase(2, 1, 1)]
    [TestCase(3, 5, 2)]
    [TestCase(1, 1, 1)]
    public void IndependentPlayersConvergeAndOnlyPositiveCreditsWin(int weapons, int consumables, int droppables)
    {
        var configuration = new ItemSpawnConfiguration { CategoryWeights = new Dictionary<ItemCategory, int>
        { [ItemCategory.Weapon] = weapons, [ItemCategory.Consumable] = consumables, [ItemCategory.Droppable] = droppables }.ToImmutableDictionary() };
        var rng = new ItemSelectionRandom(17);
        var players = Enumerable.Range(1, 8).Select(i => new PlayerItemBalance { Player = (ulong)i }).ToArray();
        for (int i = 0; i < 40000; i++)
        {
            int index = i % players.Length;
            var before = players[index];
            players[index] = before.Select(configuration, rng);
            var category = players[index].SelectedCategory!.Value;
            Assert.That(before.Credits[category] + configuration.CategoryWeights[category] / (decimal)(weapons + consumables + droppables), Is.GreaterThan(0));
        }
        foreach (var player in players)
        {
            player.Validate();
            foreach (var category in ItemRegistry.Categories)
            {
                decimal target = (decimal)player.Total * configuration.CategoryWeights[category.Identity] / (decimal)(weapons + consumables + droppables);
                Assert.That(Math.Abs(player.Counts[category.Identity] - target), Is.LessThan(3));
            }
            TestContext.WriteLine($"Player {player.Player}: total {player.Total}; " + string.Join(", ", player.Counts.OrderBy(p => p.Key)));
        }
        var untouched = players[1];
        for (int i = 0; i < 100; i++) { players[0] = players[0].Select(configuration, rng); }
        Assert.That(players[1], Is.SameAs(untouched));
    }

    [Test]
    public void ItemWeightsRemainRandomWithinTheCategoryAndTargetsValidate()
    {
        var configuration = new ItemSpawnConfiguration();
        configuration = configuration with { CategoryWeights = configuration.CategoryWeights.SetItem(ItemCategory.Weapon, 0).SetItem(ItemCategory.Droppable, 0), Weights = configuration.Weights.SetItem(HeldItem.Nitro, 3) };
        var rng = new ItemSelectionRandom(42);
        var state = new PlayerItemBalance { Player = 1 };
        int nitro = 0;
        for (int i = 0; i < 4000; i++)
        {
            state = state.Select(configuration, rng);
            Assert.That(state.SelectedCategory, Is.EqualTo(ItemCategory.Consumable));
            if (state.SelectedItem == HeldItem.Nitro) { nitro++; }
        }
        Assert.That(nitro, Is.InRange(2850, 3150));
        foreach (var weights in new[] { configuration.CategoryWeights.Clear(), configuration.CategoryWeights.SetItem(ItemCategory.Consumable, -1), configuration.CategoryWeights.SetItem(ItemCategory.Consumable, 0), configuration.CategoryWeights.SetItem((ItemCategory)255, 1) })
        { Assert.Throws<ArgumentException>(() => (configuration with { CategoryWeights = weights }).Validate()); }
        configuration = configuration with { Weights = configuration.Weights.SetItem(HeldItem.Wrench, 0).SetItem(HeldItem.Nitro, 0) };
        Assert.Throws<ArgumentException>(() => configuration.Validate());
    }

    [Test]
    public void ConfigEditsRetainCountsAndCreditContinuityWithoutReseeding()
    {
        var host = Host();
        Pickup(host, 1, "item-01");
        var before = host.Spawns!.Balances.Single();
        ulong rng = host.ItemSelectionRandom.State;
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["spawns.category_weapon_weight"] = 7 }, out _), Is.True);
        Assert.That(host.Spawns.Balances.Single(), Is.SameAs(before));
        Assert.That(host.ItemSelectionRandom.State, Is.EqualTo(rng));
        var state = before;
        var tuning = host.Configuration.Configuration.Spawns;
        for (int i = 0; i < 100; i++)
        {
            var category = ItemRegistry.Categories[i % 3].Identity;
            var changed = tuning with { CategoryWeights = tuning.CategoryWeights.SetItem(category, 0) };
            state = state.Select(changed, host.ItemSelectionRandom);
            state.Validate();
        }
        Assert.That(state.Total, Is.EqualTo(101));
        var decoded = GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(99, host.Configuration));
        Assert.That(decoded.State, Is.EqualTo(host.Configuration));
    }

    [Test]
    public void LiveItemWeightsPreserveCommittedItemsAndIndependentHistoryForEveryPlayer()
    {
        var host = Host();
        host.JoinPlayer(20, 2);
        Pickup(host, 1, "item-01");
        Pickup(host, 2, "item-02");
        var balances = host.Spawns!.Balances.ToArray();
        var slots = host.Items.Slots.ToArray();
        var spawns = host.Spawns.States.ToArray();
        ulong random = host.ItemSelectionRandom.State;
        var edits = ItemRegistry.All.ToDictionary(item => $"spawns.{item.Key}_weight", item => item.Identity == HeldItem.Nitro ? 7d : 0d);
        var before = host.Configuration;
        Assert.That(host.TryConfigure(20, edits, out _), Is.False);
        Assert.That(host.Configuration, Is.EqualTo(before));
        Assert.That(host.TryConfigure(0, edits, out _), Is.True);
        EqualBalances(balances, host.Spawns.Balances);
        Assert.That(host.Items.Slots, Is.EqualTo(slots));
        Assert.That(host.Spawns.States, Is.EqualTo(spawns));
        Assert.That(host.ItemSelectionRandom.State, Is.EqualTo(random));
        Assert.That(host.Configuration.Configuration.Spawns.CategoryWeights, Is.EqualTo(before.Configuration.Spawns.CategoryWeights));

        host.Suspend(20);
        Assert.That(host.ResumePlayer(30, 2), Is.True);
        var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(Checkpoint(host)));
        var replacement = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 2);
        Assert.That(replacement.Configuration, Is.EqualTo(host.Configuration));
        foreach (var authority in new[] { host, replacement })
        {
            Pickup(authority, 1, "item-03");
            Assert.That(authority.Spawns!.Balances.Single(b => b.Player == 2).Total, Is.EqualTo(1));
            Pickup(authority, 2, "item-04");
            Assert.That(authority.Items.Slots.Select(slot => slot.SecondItem), Is.All.EqualTo(HeldItem.Nitro));
            Assert.That(authority.Spawns.Balances.Select(b => b.Total), Is.All.EqualTo(2));
        }
        EqualBalances(host.Spawns.Balances, replacement.Spawns!.Balances);
        Assert.That(replacement.ItemSelectionRandom.State, Is.EqualTo(host.ItemSelectionRandom.State));
    }

    [Test]
    public void PickupsAreIndependentAndRejectedOrDeveloperGrantsDoNotCount()
    {
        var host = Host();
        host.JoinPlayer(20, 2);
        Pickup(host, 1, "item-01");
        var first = host.Spawns!.Balances.Single();
        var rng = host.ItemSelectionRandom.State;
        Assert.That(host.Spawns.TryPickup(host.World, "item-01", 1), Is.False);
        Assert.That(host.ItemSelectionRandom.State, Is.EqualTo(rng));
        Assert.That(host.GiveItem(0, HeldItem.Wrench), Is.True);
        Assert.That(host.GiveItem(0, HeldItem.Oil), Is.False);
        Assert.That(host.Spawns.Balances.Single(), Is.SameAs(first));
        Pickup(host, 2, "item-02");
        Assert.That(host.Spawns.Balances.Single(b => b.Player == 1), Is.SameAs(first));
        Assert.That(host.Spawns.Balances.Single(b => b.Player == 2).Total, Is.EqualTo(1));
        host.Suspend(20);
        Assert.That(host.ResumePlayer(30, 2), Is.True);
        Assert.That(host.Spawns.Balances.Single(b => b.Player == 2).Total, Is.EqualTo(1));
        host.JoinPlayer(40, 3);
        Assert.That(host.Spawns.Balances.Any(b => b.Player == 3), Is.False);
        Pickup(host, 3, "item-03");
        Assert.That(host.Spawns.Balances.Single(b => b.Player == 3).Total, Is.EqualTo(1));
        host.Suspend(30);
        host.ExpirePlayer(2);
        Assert.That(host.Spawns.Balances.Any(b => b.Player == 2), Is.False);
    }

    [Test]
    public void ResumeAndMigrationRestoreExactNextCategoryAndItemAndNewMatchResets()
    {
        var lobby = new LobbyAuthority(100, "Host");
        lobby.Join(20, GameVersion.Current.ToString(), "Second", "second");
        lobby.SetReady(0, true); lobby.SetReady(20, true); lobby.Start(0);
        var host = Host(lobby.State.Match);
        host.JoinPlayer(20, 2);
        Pickup(host, 1, "item-01"); Pickup(host, 2, "item-02");
        host.Items.RemovePlayer(1); host.Items.RemovePlayer(2);
        lobby.RetainConfiguration(host.Configuration);
        var resume = Checkpoint(host);
        var restoredResume = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(resume));
        EqualBalances(resume.Items.Balances, restoredResume.Items.Balances);
        var migration = MigrationCheckpointCodec.Decode(MigrationCheckpointCodec.Encode(new(1, lobby.Capture("host"), resume, host.CaptureAuthority())));
        var restored = HostVehicleSession.Restore(migration.Arena!, migration.Host!, 2);
        EqualBalances(host.Spawns!.Balances, restored.Spawns!.Balances);
        for (int i = 0; i < 40; i++)
        {
            host.Items.RemovePlayer(1); restored.Items.RemovePlayer(1);
            host.Step(default, Observe); restored.Step(default, Observe);
            Pickup(host, 1, "item-03"); Pickup(restored, 1, "item-03");
            EqualBalances(host.Spawns.Balances, restored.Spawns.Balances);
            Assert.That(host.ItemSelectionRandom.State, Is.EqualTo(restored.ItemSelectionRandom.State));
        }
        var fresh = Host(999);
        Assert.That(fresh.Spawns!.Balances, Is.Empty);
        Assert.That(fresh.Spawns.States.All(s => s.Available), Is.True);
    }

    [Test]
    public void DeathAndAutomaticRespawnRetainCurrentMatchHistory()
    {
        var host = Host();
        Pickup(host, 1, "item-01");
        var balance = host.Spawns!.Balances.Single();
        host.Items.RemovePlayer(1);
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["items.maximum_damage"] = 10000 }, out _), Is.True);
        host.Items.Grant(host.World, 1, HeldItem.Missile);
        var slot = host.Items.Slots.Single();
        host.UseItem(0, host.SessionId, slot.Life, slot.Token);
        host.Step(default, Observe, (_, _) => 0);
        Assert.That(host.World.GetVehicle(1).CanInteract, Is.False);
        for (int i = 0; i < 3; i++) { host.Step(default, Observe); }
        Assert.That(host.World.GetVehicle(1).LifeId, Is.GreaterThan(1));
        Assert.That(host.Spawns.Balances.Single(), Is.SameAs(balance));
    }

    [Test]
    public void CodecRejectsInvalidHistoryAndTruncation()
    {
        var host = Host(); Pickup(host, 1, "item-01");
        var resume = Checkpoint(host);
        var bytes = ItemCodec.EncodeState(resume.Items);
        for (int i = 0; i < bytes.Length; i++) { int length = i; Assert.Throws<ArgumentException>(() => ItemCodec.DecodeState(bytes.AsSpan(0, length))); }
        var valid = host.Spawns!.Balances.Single();
        foreach (var invalid in new[] { valid with { Player = 0 }, valid with { Player = 999 }, valid with { Total = 99 }, valid with { Credits = valid.Credits.Clear() }, valid with { Credits = valid.Credits.SetItem(ItemCategory.Weapon, 100) }, valid with { SelectedItem = HeldItem.None } })
        { Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), [], [], [], balances: [invalid])); }
        Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), [], [], [], balances: [valid, valid]));
    }

    private static ResumeCheckpoint Checkpoint(HostVehicleSession host) => new(new ItemPublication(1, host.Snapshot(), host.Items.Slots, host.Items.Missiles, [], host.Spawns?.States, host.Items.Patches, host.Items.OilContacts, host.Spawns?.Balances), host.World.State.Match!, null, host.Configuration);

    private static void EqualBalances(IReadOnlyList<PlayerItemBalance> expected, IReadOnlyList<PlayerItemBalance> actual)
    {
        Assert.That(actual.Select(b => (b.Player, b.Total, b.SelectedItem)), Is.EqualTo(expected.Select(b => (b.Player, b.Total, b.SelectedItem))));
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.That(actual[i].Credits, Is.EquivalentTo(expected[i].Credits));
            Assert.That(actual[i].Counts, Is.EquivalentTo(expected[i].Counts));
        }
    }

    private static HostVehicleSession Host(ulong session = 99)
    {
        var host = new HostVehicleSession(session, configurationRevision: 1, configuration: new() { Spawns = new() { CooldownTicks = 1 }, Respawn = new() { DelayTicks = 2 } });
        host.RegisterSpawns(PrototypeArena.Configuration);
        return host;
    }
    private static VehicleObservation Observe(VehicleSnapshot state) => new(state.Movement.Physics, Vector3.UnitY);
    private static void Pickup(HostVehicleSession host, ulong player, string marker)
    {
        var state = host.World.State;
        var pose = new VehiclePhysicsState(PrototypeArena.Configuration.Items.Single(m => m.Id == marker).Position, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        host.World.Restore(new(state.Tick, state.LastInput, state.Vehicles.Select(v => v.VehicleId != player ? v : new VehicleSnapshot(player, v.LifeId, new VehicleState(state.Tick, pose, false, false, 0, 0), v.Damage, pose)), state.Match));
        Assert.That(host.Spawns!.TryPickup(host.World, marker, player), Is.True);
    }
}
