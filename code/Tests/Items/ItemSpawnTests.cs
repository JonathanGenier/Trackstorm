using System.Numerics;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Sessions;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

/// <summary>Authoritative registration, contention, clock, pool and publication boundaries.</summary>
[TestFixture]
internal sealed class ItemSpawnTests
{
    [Test]
    public void TwoSuccessfulPickupsFillDistinctSlotsAndFullInventoryDoesNotDraw()
    {
        var host = Host();
        Place(host, 1, "item-01");
        Assert.That(host.Spawns!.TryPickup(host.World, "item-01", 1), Is.True);
        var first = host.Items.Slots.Single();
        Place(host, 1, "item-02");
        Assert.That(host.Spawns.TryPickup(host.World, "item-02", 1), Is.True);
        var full = host.Items.Slots.Single();
        Assert.That(full.Full, Is.True);
        Assert.That((full.Token, full.Item), Is.EqualTo((first.Token, first.Item)));
        Assert.That(full.SecondToken, Is.GreaterThan(first.Token));
        Assert.That(host.Spawns.States.Single(s => s.Id == "item-02").Token, Is.EqualTo(full.SecondToken));
        Assert.That(host.Spawns.Balances.Single().Total, Is.EqualTo(2));
        ulong random = host.ItemSelectionRandom.State;
        Place(host, 1, "item-03");
        Assert.That(host.Spawns.TryPickup(host.World, "item-03", 1), Is.False);
        Assert.That(host.ItemSelectionRandom.State, Is.EqualTo(random));
        Assert.That(host.Spawns.Balances.Single().Total, Is.EqualTo(2));
        Assert.That(host.Items.Slots.Single(), Is.EqualTo(full));
        Assert.That(host.Spawns.States.Single(s => s.Id == "item-03").Available, Is.True);
    }
    /// <summary>Fresh matches choose independent authoritative seeds and keep each in effective tuning.</summary>
    [Test]
    public void FreshMatchesStartWithIndependentItemSeeds()
    {
        var lobby = new LobbyAuthority(100, "Host");
        var seeds = new HashSet<int>();
        for (ulong match = 1; match <= 8; match++)
        {
            var host = new HostVehicleSession(match, configuration: lobby.Configuration.Configuration,
                configurationRevision: lobby.Configuration.Revision, randomizeItemSeed: true);
            host.RegisterSpawns(PrototypeArena.Configuration);
            int seed = host.Configuration.Configuration.Spawns.Seed;
            Assert.That(host.Configuration.Revision, Is.EqualTo(lobby.Configuration.Revision + 1));
            Assert.That(host.Spawns!.Configuration.Seed, Is.EqualTo(seed));
            Assert.That(host.Spawns.RandomState, Is.EqualTo(unchecked((ulong)seed)));
            lobby.RetainConfiguration(host.Configuration);
            seeds.Add(seed);
        }

        Assert.That(seeds.Count, Is.GreaterThan(1));
    }

    /// <summary>A future policy can draw first, then select an item from the same match stream.</summary>
    [Test]
    public void SequentialDecisionsShareOneMatchOwnedStream()
    {
        var tuning = new ItemSpawnConfiguration { Seed = 17 };
        var first = new HostVehicleSession(1, configuration: new() { Spawns = tuning });
        var replay = new HostVehicleSession(2, configuration: new() { Spawns = tuning });
        first.RegisterSpawns(PrototypeArena.Configuration);
        replay.RegisterSpawns(PrototypeArena.Configuration);

        int firstPreselection = first.ItemSelectionRandom.Next(3);
        int replayPreselection = replay.ItemSelectionRandom.Next(3);
        Assert.That(firstPreselection, Is.EqualTo(replayPreselection));
        Assert.That(first.Spawns!.RandomState, Is.EqualTo(first.ItemSelectionRandom.State));
        Assert.That(first.ItemSelectionRandom.State, Is.Not.EqualTo((ulong)tuning.Seed));

        Place(first, 1, "item-01");
        Place(replay, 1, "item-01");
        Assert.That(first.Spawns.TryPickup(first.World, "item-01", 1), Is.True);
        Assert.That(replay.Spawns!.TryPickup(replay.World, "item-01", 1), Is.True);
        Assert.That(first.Items.Slots.Single().Item, Is.EqualTo(replay.Items.Slots.Single().Item));
        Assert.That(first.ItemSelectionRandom.State, Is.EqualTo(replay.ItemSelectionRandom.State));
        Assert.That(first.Spawns.RandomState, Is.EqualTo(first.ItemSelectionRandom.State));
    }

    /// <summary>Registration preserves actual configuration IDs and is permitted once per match.</summary>
    [Test]
    public void RegistersExactlyActualMarkers()
    {
        var original = PrototypeArena.Configuration;
        var arena = new ArenaConfiguration(original.Minimum, original.Maximum, original.Players, original.Items.Select(marker => marker with { Id = "custom-" + marker.Id }), original.Surfaces);
        var host = new HostVehicleSession(1);
        host.RegisterSpawns(arena);
        Assert.That(host.Spawns!.States.Select(state => state.Id), Is.EquivalentTo(arena.Items.Select(marker => marker.Id)));
        Assert.That(host.Spawns.States.Count, Is.EqualTo(8));
        Assert.That(host.Spawns.States.All(state => state.Available), Is.True);
        Assert.That(host.Spawns.TryPickup(host.World, "item-01", 1), Is.False);
        Assert.Throws<InvalidOperationException>(() => host.RegisterSpawns(arena));
        var started = new HostVehicleSession(2);
        started.Step(default, Observe);
        Assert.Throws<InvalidOperationException>(() => started.RegisterSpawns(arena));
    }

    /// <summary>An invalid fixture override cannot reset the match stream before registration fails.</summary>
    [Test]
    public void InvalidSpawnOverrideLeavesMatchStreamUntouched()
    {
        var host = new HostVehicleSession(1);
        ulong initialState = host.ItemSelectionRandom.State;
        Assert.Throws<ArgumentException>(() => host.RegisterSpawns(PrototypeArena.Configuration,
            new ItemSpawnConfiguration { Seed = 99, CooldownTicks = 0 }));
        Assert.That(host.ItemSelectionRandom.State, Is.EqualTo(initialState));
        Assert.That(host.Spawns, Is.Null);
    }

    /// <summary>Two same-tick contacts and retries produce exactly one grant and one cooldown.</summary>
    [Test]
    public void ContestHasOneWinnerUntilExactCooldownBoundary()
    {
        var sequence = new Queue<HeldItem>(new[] { HeldItem.Wrench, HeldItem.Missile });
        var host = Host(() => sequence.Dequeue());
        host.Join(42);
        Place(host, 1, "item-01");
        Place(host, 2, "item-01");
        Assert.That(host.Spawns!.TryPickup(host.World, "item-01", 2), Is.True);
        Assert.That(host.Spawns.TryPickup(host.World, "item-01", 1), Is.False);
        Assert.That(host.Spawns.TryPickup(host.World, "item-01", 2), Is.False);
        Assert.That(host.Items.Slots.Single().Vehicle, Is.EqualTo(2));
        Assert.That(host.Spawns.States[0], Is.EqualTo(new ItemSpawnState("item-01", false, 3, 2, 1, HeldItem.Wrench)));
        for (int i = 0; i < 2; i++)
        {
            host.Step(default, Observe);
            Assert.That(host.Spawns.TryPickup(host.World, "item-01", 1), Is.False);
        }

        host.Step(default, Observe);
        Assert.That(host.Spawns.States[0].Available, Is.True);
        Assert.That(host.Spawns.TryPickup(host.World, "item-01", 1), Is.True);
        Assert.That(host.Items.Slots.Single(slot => slot.Vehicle == 1).Item, Is.EqualTo(HeldItem.Missile));
        Assert.That(sequence, Is.Empty);
        Assert.That(host.Spawns.States[0].NextActivationTick, Is.EqualTo(6));
        Assert.That(host.Items.Slots.Select(slot => slot.Token).Distinct().Count(), Is.EqualTo(2));
    }

    /// <summary>Invalid contacts do not consume selection, claim availability or overwrite a slot.</summary>
    [Test]
    public void RejectsUnknownDeadDepartedOutOfRangeAndOccupiedPlayers()
    {
        int selections = 0;
        var host = Host(() =>
        {
            selections++;
            return HeldItem.Wrench;
        });
        Assert.That(host.Spawns!.TryPickup(host.World, "missing", 1), Is.False);
        Assert.That(host.Spawns.TryPickup(host.World, "item-01", 99), Is.False);
        Assert.That(host.Spawns.TryPickup(host.World, "item-01", 1), Is.False);
        Place(host, 1, "item-01", 0);
        Assert.That(host.Spawns.TryPickup(host.World, "item-01", 1), Is.False);
        Place(host, 1, "item-01");
        host.Items.Grant(host.World, 1, HeldItem.Missile);
        host.Items.Grant(host.World, 1, HeldItem.Oil);
        var original = host.Items.Slots.Single();
        Assert.That(host.Spawns.TryPickup(host.World, "item-01", 1), Is.False);
        Assert.That(host.Items.Slots.Single(), Is.EqualTo(original));
        host.Join(42);
        Place(host, 2, "item-01");
        host.Leave(42);
        Assert.That(host.Spawns.TryPickup(host.World, "item-01", 2), Is.False);
        Assert.That(selections, Is.Zero);
        Assert.That(host.Spawns.Revision, Is.Zero);
        Assert.That(host.Spawns.States.All(state => state.Available), Is.True);
    }

    /// <summary>Three-dimensional range includes the exact boundary but excludes airborne/outside contacts.</summary>
    /// <param name="distance">Vertical distance from the marker.</param>
    /// <param name="accepted">Expected decision.</param>
    [TestCase(3f, true)]
    [TestCase(3.01f, false)]
    public void RangeBoundary(float distance, bool accepted)
    {
        var host = Host();
        Place(host, 1, "item-01", offset: new Vector3(0, distance, 0));
        Assert.That(host.Spawns!.TryPickup(host.World, "item-01", 1), Is.EqualTo(accepted));
    }

    /// <summary>Invalid selectors fail without mutating inventory or spawn state.</summary>
    [Test]
    public void InvalidSelectorDoesNotPartiallyClaim()
    {
        var host = Host(() => HeldItem.None);
        Place(host, 1, "item-01");
        Assert.Throws<InvalidOperationException>(() => host.Spawns!.TryPickup(host.World, "item-01", 1));
        Assert.That(host.Items.Slots, Is.Empty);
        Assert.That(host.Spawns!.States.All(state => state.Available), Is.True);
    }

    /// <summary>Use then pickup at one boundary retires the previous capability and publishes the replacement.</summary>
    [Test]
    public void ConsumptionAndReacquisitionUseDistinctTokens()
    {
        var sequence = new Queue<HeldItem>(new[] { HeldItem.Wrench, HeldItem.Missile });
        var host = Host(() => sequence.Dequeue());
        Place(host, 1, "item-01");
        host.Spawns!.TryPickup(host.World, "item-01", 1);
        var previous = host.Items.Slots.Single();
        for (int i = 0; i < 3; i++)
        {
            host.Step(default, Observe);
        }

        Assert.That(host.UseItem(0, 99, previous.Life, previous.Token), Is.True);
        host.Step(default, Observe);
        Assert.That(host.Spawns.TryPickup(host.World, "item-01", 1), Is.True);
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.Missile));
        Assert.That(host.Items.Slots.Single().Token, Is.GreaterThan(previous.Token));
        Assert.That(host.UseItem(0, 99, previous.Life, previous.Token), Is.False);
        var publication = new ItemPublication(1, host.Snapshot(), host.Items.Slots, host.Items.Missiles, host.Items.Events, host.Spawns.States);
        Assert.That(ItemCodec.DecodeState(ItemCodec.EncodeState(publication)).Slots.Single().Item, Is.EqualTo(HeldItem.Missile));
    }

    /// <summary>The complete registered pool is bounded and a seed reproduces the four-item weighted sequence.</summary>
    [Test]
    public void ValidatesPoolAndSeededDistribution()
    {
        foreach (var configuration in new[]
        {
            new ItemSpawnConfiguration { Weights = new ItemSpawnConfiguration().Weights.SetItem(HeldItem.Wrench, -1) },
            new ItemSpawnConfiguration { Weights = new ItemSpawnConfiguration().Weights.Clear() },
            new ItemSpawnConfiguration { Weights = new ItemSpawnConfiguration().Weights.SetItem(HeldItem.Wrench, int.MaxValue) }, new ItemSpawnConfiguration { CooldownTicks = 0 },
            new ItemSpawnConfiguration { PickupRadius = float.NaN },
            new ItemSpawnConfiguration { Weights = new ItemSpawnConfiguration().Weights.SetItem((HeldItem)255, 1) },
            new ItemSpawnConfiguration { Weights = new ItemSpawnConfiguration().Weights.SetItems(ItemRegistry.All.Select(item => new KeyValuePair<HeldItem, int>(item.Identity, 0))) },
        })
        {
            Assert.Throws<ArgumentException>(() => configuration.Validate());
        }

        var tuning = new ItemSpawnConfiguration { Weights = new ItemSpawnConfiguration().Weights.SetItem(HeldItem.Missile, 3), Seed = 17 };
        var first = new ItemSelectionRandom(17);
        var second = new ItemSelectionRandom(17);
        var firstBalance = new PlayerItemBalance { Player = 1 };
        var secondBalance = new PlayerItemBalance { Player = 1 };
        var sequence = Enumerable.Range(0, 1000).Select(_ => (firstBalance = firstBalance.Select(tuning, first)).SelectedItem).ToArray();
        Assert.That(sequence, Is.EqualTo(Enumerable.Range(0, 1000).Select(_ => (secondBalance = secondBalance.Select(tuning, second)).SelectedItem).ToArray()));
        Assert.That(sequence, Is.SupersetOf(ItemRegistry.All.Select(item => item.Identity)));
        Assert.That(sequence.Count(item => item == HeldItem.Missile), Is.InRange(325, 425));
    }

    /// <summary>Replicated claims preserve cooldown and grant identity; malformed and old envelopes fail closed.</summary>
    [Test]
    public void PublicationRoundTripsAndRejectsCorruption()
    {
        var host = Host();
        Place(host, 1, "item-01");
        host.Spawns!.TryPickup(host.World, "item-01", 1);
        var publication = new ItemPublication(1, host.Snapshot(), host.Items.Slots, host.Items.Missiles, host.Items.Events, host.Spawns.States);
        byte[] bytes = ItemCodec.EncodeState(publication);
        var decoded = ItemCodec.DecodeState(bytes);
        Assert.That(decoded.Spawns, Is.EqualTo(publication.Spawns));
        Assert.That(decoded.Slots, Is.EqualTo(publication.Slots));
        for (int length = 0; length < bytes.Length; length++)
        {
            int prefix = length;
            Assert.Throws<ArgumentException>(() => ItemCodec.DecodeState(bytes.AsSpan(0, prefix)));
        }

        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeState(bytes.Concat(new byte[] { 0 }).ToArray()));
        bytes[2] = 1;
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeState(bytes));
        var invalid = host.Spawns.States.ToArray();
        invalid[0] = invalid[0] with { Available = true };
        Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), host.Items.Slots, Array.Empty<MissileState>(), Array.Empty<ItemEvent>(), invalid));
    }

    private static HostVehicleSession Host(Func<HeldItem>? selector = null)
    {
        var host = new HostVehicleSession(99);
        host.RegisterSpawns(PrototypeArena.Configuration, new ItemSpawnConfiguration { CooldownTicks = 3 }, selector);
        return host;
    }

    private static VehicleObservation Observe(VehicleSnapshot state) => new(state.Movement.Physics, Vector3.UnitY);

    private static void Place(HostVehicleSession host, ulong id, string marker, float hp = 100, Vector3 offset = default)
    {
        var world = host.World;
        Vector3 position = PrototypeArena.Configuration.Items.Single(spawn => spawn.Id == marker).Position + offset;
        var pose = new VehiclePhysicsState(position, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        var states = world.State.Vehicles.Select(state => state.VehicleId != id ? state : new VehicleSnapshot(id, state.LifeId, new VehicleState(world.State.Tick, pose, false, false, 0, 0), new VehicleDamageState(100, hp, null, null), pose));
        world.Restore(new SimulationState(world.State.Tick, world.State.LastInput, states, world.State.Match));
    }
}
