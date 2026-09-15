using System.Numerics;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

/// <summary>Authoritative registration, contention, clock, pool and publication boundaries.</summary>
[TestFixture]
internal sealed class ItemSpawnTests
{
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

    /// <summary>Both items have positive weights, overflow is rejected, and a seed reproduces the weighted sequence.</summary>
    [Test]
    public void ValidatesPoolAndSeededDistribution()
    {
        foreach (var configuration in new[]
        {
            new ItemSpawnConfiguration { WrenchWeight = -1 }, new ItemSpawnConfiguration { MissileWeight = -1 },
            new ItemSpawnConfiguration { WrenchWeight = 0, MissileWeight = 0 }, new ItemSpawnConfiguration { MissileWeight = 0 },
            new ItemSpawnConfiguration { WrenchWeight = int.MaxValue }, new ItemSpawnConfiguration { CooldownTicks = 0 },
            new ItemSpawnConfiguration { PickupRadius = float.NaN },
        })
        {
            Assert.Throws<ArgumentException>(() => configuration.CreateSelector());
        }

        var tuning = new ItemSpawnConfiguration { WrenchWeight = 1, MissileWeight = 3, Seed = 17 };
        var first = tuning.CreateSelector();
        var second = tuning.CreateSelector();
        var sequence = Enumerable.Range(0, 1000).Select(_ => first()).ToArray();
        Assert.That(sequence, Is.EqualTo(Enumerable.Range(0, 1000).Select(_ => second()).ToArray()));
        Assert.That(sequence, Does.Contain(HeldItem.Wrench).And.Contain(HeldItem.Missile));
        Assert.That(sequence.Count(item => item == HeldItem.Missile), Is.InRange(650, 850));
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
