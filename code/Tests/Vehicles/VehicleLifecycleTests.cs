using System.Numerics;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;
using World = Trackstorm.Core.Simulation.Simulation;

namespace Trackstorm.Core.Tests.Vehicles;

/// <summary>Deterministic lifecycle boundaries through the production aggregate, items and replication paths.</summary>
[TestFixture]
internal sealed class VehicleLifecycleTests
{
    /// <summary>Lethal damage occurs once, blocks all interactions, and resets exactly at the deadline.</summary>
    [Test]
    public void DeathWaitAndRespawnUseExactGlobalTicks()
    {
        World world = Create();
        var items = new ItemAuthority();
        Assert.That(items.Grant(world, 1, HeldItem.Missile), Is.True);
        var start = world.GetVehicle(1);
        var charged = new VehicleState(0, start.ObservedPhysics, true, true, 0.4f, 1, SurfaceType.Mud, 0.5f, 0.8f, 2, 3, 0.2f, new WheelSupport(new Vector4(0.1f)));
        world.Restore(new SimulationState(0, default, [new VehicleSnapshot(1, 1, charged, start.Damage, start.ObservedPhysics)]));
        Step(world, items, 120);
        VehicleSnapshot dead = world.GetVehicle(1);
        Assert.That(dead.Lifecycle, Is.EqualTo(VehicleLifecycle.Dead));
        Assert.That(dead.RespawnAtTick, Is.EqualTo(5));
        Assert.That(dead.Damage.LastDamage!.DestroyedTransition, Is.True);
        Assert.That(world.LifecycleChanges.Count, Is.EqualTo(1));
        Assert.That(items.Slots, Is.Empty, "Inventory clears in the lethal batch, not one tick later.");
        Assert.That(items.Grant(world, 1, HeldItem.Wrench), Is.False);
        Assert.That(items.RequestUse(world, 1, 1, 1), Is.False);
        for (int tick = 2; tick < 5; tick++)
        {
            Step(world, items, 500);
            VehicleSnapshot waiting = world.GetVehicle(1);
            Assert.That(waiting.Lifecycle, Is.EqualTo(VehicleLifecycle.Respawning));
            Assert.That(waiting.Damage.LastDamage, Is.EqualTo(dead.Damage.LastDamage));
            Assert.That(waiting.Movement.Physics.LinearVelocity, Is.EqualTo(Vector3.Zero));
            Assert.That(waiting.Movement.Physics.AngularVelocity, Is.EqualTo(Vector3.Zero));
            Assert.That(waiting.Effects, Is.Empty);
            Assert.That(items.Grant(world, 1, HeldItem.Wrench), Is.False);
            Assert.That(items.RequestUse(world, 1, 1, 1), Is.False);
        }

        Step(world, items, 500);
        VehicleSnapshot alive = world.GetVehicle(1);
        Assert.That(alive.Lifecycle, Is.EqualTo(VehicleLifecycle.Alive));
        Assert.That(alive.LifeId, Is.EqualTo(2));
        Assert.That(alive.RespawnAtTick, Is.Null);
        Assert.That(alive.Damage.CurrentHP, Is.EqualTo(120));
        Assert.That(alive.Damage.LastDamage, Is.Null);
        Assert.That(alive.Damage.LastCollisionTick, Is.Null);
        Assert.That(alive.Movement, Is.EqualTo(new VehicleState(5, world.Arena.Respawn(1, 2), false, false, 0, 0)));
        Assert.That(alive.ObservedPhysics, Is.EqualTo(alive.Movement.Physics));
        Assert.That(alive.Effects, Is.Empty);
    }

    /// <summary>Opting into retention preserves the item but retires its old-life capability.</summary>
    /// <param name="clear">Host policy for held inventory.</param>
    [TestCase(true)]
    [TestCase(false)]
    public void ItemClearingPolicyAndRepeatedLivesRemainClean(bool clear)
    {
        World world = Create(clear);
        var items = new ItemAuthority();
        var selected = new HashSet<Vector3>();
        for (int cycle = 0; cycle < 32; cycle++)
        {
            if (items.Slots.Count == 0)
            {
                Assert.That(items.Grant(world, 1, HeldItem.Missile), Is.True);
            }

            ItemSlot old = items.Slots.Single();
            Step(world, items, 120);
            Assert.That(items.Slots.Count, Is.EqualTo(clear ? 0 : 1));
            Assert.That(items.RequestUse(world, 1, old.Life, old.Token), Is.False);
            for (int wait = 0; wait < 4; wait++)
            {
                Step(world, items);
            }

            VehicleSnapshot alive = world.GetVehicle(1);
            selected.Add(alive.Movement.Physics.Position);
            Assert.That(alive.LifeId, Is.EqualTo(cycle + 2));
            Assert.That(alive.Damage.CurrentHP, Is.EqualTo(120));
            Assert.That(alive.Damage.LastDamage, Is.Null);
            Assert.That(alive.Movement, Is.EqualTo(new VehicleState(alive.Movement.Tick, alive.Movement.Physics, false, false, 0, 0)));
            Assert.That(items.Missiles, Is.Empty);
            Assert.That(items.RequestUse(world, 1, old.Life, old.Token), Is.False);
            if (!clear)
            {
                Assert.That(items.Slots.Single().Item, Is.EqualTo(HeldItem.Missile));
                Assert.That(items.Slots.Single().Token, Is.GreaterThan(old.Token));
                Assert.That(items.Slots.Single().Life, Is.EqualTo(alive.LifeId));
            }
        }

        Assert.That(selected, Is.EquivalentTo(world.Arena.Players.Select(spawn => spawn.Position)));
    }

    /// <summary>Inactive sources cannot inflict collision/effect damage, and their flying projectiles are retired.</summary>
    [Test]
    public void DeadSourcesCannotDamageAnotherVehicle()
    {
        World world = Create();
        world.AddVehicle(2, new(), new(), PrototypeArena.Configuration.Spawn(1));
        var items = new ItemAuthority();
        items.Grant(world, 1, HeldItem.Missile);
        ItemSlot slot = items.Slots.Single();
        items.RequestUse(world, 1, slot.Life, slot.Token);
        Step(world, items);
        Assert.That(items.Missiles.Count, Is.EqualTo(1));
        Step(world, items, 120);
        Assert.That(items.Missiles, Is.Empty);
        VehicleSnapshot target = world.GetVehicle(2);
        InputFrame frame = Drive(world.State.Tick + 1);
        world.Step(frame, [new VehicleStepRequest(1, frame, new VehicleObservation(world.GetVehicle(1).ObservedPhysics, Vector3.UnitY)), new VehicleStepRequest(2, frame, new VehicleObservation(target.ObservedPhysics, Vector3.UnitY, [new VehicleContact(-Vector3.UnitX * 60, Vector3.UnitX, 0, 1)]), [Effect(200, 1)])]);
        Assert.That(world.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(target.Damage.CurrentHP));
        Assert.That(world.GetVehicle(2).Effects, Is.Empty);
    }

    /// <summary>Both aggregate codecs preserve waiting state and reject inconsistent lifecycle data.</summary>
    [Test]
    public void LifecycleRoundTripsAndRestoresDeadline()
    {
        var host = new HostVehicleSession(99, respawnConfiguration: new RespawnConfiguration { DelayTicks = 4 });
        var items = host.Items;
        Step(host.World, items, 100);
        foreach (int phase in new[] { 0, 1, 2 })
        {
            VehicleSnapshot state = host.World.GetVehicle(1);
            VehicleSnapshot aggregate = VehicleSnapshotCodec.Decode(VehicleSnapshotCodec.Encode(state));
            VehicleSnapshot network = VehicleNetworkCodec.DecodeSnapshot(VehicleNetworkCodec.EncodeSnapshot(host.Snapshot())).Vehicles.Single().State;
            Assert.That(VehicleSnapshotCodec.Encode(aggregate), Is.EqualTo(VehicleSnapshotCodec.Encode(state)));
            Assert.That(VehicleSnapshotCodec.Encode(network), Is.EqualTo(VehicleSnapshotCodec.Encode(state)));
            host.World.Restore(new SimulationState(state.Movement.Tick, Drive(state.Movement.Tick), [aggregate]));
            if (phase == 0)
            {
                Step(host.World, items);
            }
            else if (phase == 1)
            {
                for (int i = 0; i < 3; i++)
                {
                    Step(host.World, items);
                }
            }
        }

        VehicleSnapshot alive = host.World.GetVehicle(1);
        Assert.That(alive.Lifecycle, Is.EqualTo(VehicleLifecycle.Alive));
        Assert.Throws<ArgumentException>(() => new VehicleSnapshot(1, 2, alive.Movement, alive.Damage, alive.ObservedPhysics, lifecycle: VehicleLifecycle.Dead));
        Assert.Throws<ArgumentException>(() => new VehicleSnapshot(1, 2, alive.Movement, alive.Damage, alive.ObservedPhysics, lifecycle: (VehicleLifecycle)99));
        Assert.Throws<ArgumentException>(() => new VehicleSnapshot(1, 2, alive.Movement, alive.Damage, alive.ObservedPhysics, respawnAtTick: 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(delay: 0));
    }

    /// <summary>Client prediction neither kills from local contact observations nor respawns past the host deadline.</summary>
    [Test]
    public void PredictionCannotDecideHealthOrLifecycle()
    {
        var host = new HostVehicleSession(9, respawnConfiguration: new RespawnConfiguration { DelayTicks = 2 });
        var prediction = new PredictedVehicle(host.Snapshot().Vehicles.Single());
        prediction.Predict(Drive(1), state => new VehicleObservation(state.Movement.Physics, Vector3.UnitY, [new VehicleContact(-Vector3.UnitX * 100, Vector3.UnitX, 0, 0)]));
        Assert.That(prediction.State.Damage.CurrentHP, Is.EqualTo(100));
        Step(host.World, host.Items, 100);
        prediction = new PredictedVehicle(host.Snapshot().Vehicles.Single());
        for (int i = 0; i < 10; i++)
        {
            prediction.Predict(Drive((ulong)i), _ => throw new InvalidOperationException("Dead prediction must not run native movement."));
        }

        Assert.That(prediction.State.Lifecycle, Is.EqualTo(VehicleLifecycle.Dead));
        Assert.That(prediction.State.LifeId, Is.EqualTo(1));
        Assert.That(prediction.State.Movement.Physics.LinearVelocity, Is.EqualTo(Vector3.Zero));
    }

    /// <summary>Selection searches configured markers, avoids occupied footprints and has a defined full-arena wait.</summary>
    [Test]
    public void SpawnSelectionUsesCustomMarkersAndRejectsOccupiedFootprints()
    {
        ArenaConfiguration original = PrototypeArena.Configuration;
        var arena = new ArenaConfiguration(original.Minimum, original.Maximum, original.Players.Select(spawn => spawn with { Position = spawn.Position + Vector3.UnitX }), original.Items, original.Surfaces);
        var occupied = Enumerable.Range(0, 8).Select(slot =>
        {
            VehiclePhysicsState pose = arena.Spawn(slot);
            return new VehicleSnapshot((ulong)slot + 2, 1, new VehicleState(0, pose, false, false, 0, 0), new VehicleHealth(new()).State, pose);
        }).ToArray();
        Assert.That(arena.SelectRespawn(1, 2, occupied), Is.Null);
        for (int free = 0; free < 8; free++)
        {
            Assert.That(arena.SelectRespawn(1, 2, occupied.Where((_, slot) => slot != free).ToArray()), Is.EqualTo(arena.Spawn(free)));
        }

        var empty = Array.Empty<VehicleSnapshot>();
        Assert.That(Enumerable.Range(1, 8).Select(life => arena.SelectRespawn(1, (ulong)life, empty)!.Value.Position), Is.EquivalentTo(arena.Players.Select(spawn => spawn.Position)));
    }

    /// <summary>Simultaneous respawns reserve distinct markers within the atomic batch.</summary>
    [Test]
    public void SimultaneousRespawnsCannotSelectTheSameMarker()
    {
        var world = new World(new SimulationConfiguration(60), new RespawnConfiguration { DelayTicks = 1 });
        // These identities have the same cyclic starting marker.
        world.AddVehicle(1, new(), new(), PrototypeArena.Configuration.Spawn(0));
        world.AddVehicle(9, new(), new(), PrototypeArena.Configuration.Spawn(1));
        InputFrame frame = Drive(1);
        world.Step(frame, world.State.Vehicles.Select(state => new VehicleStepRequest(state.VehicleId, frame, new VehicleObservation(state.ObservedPhysics, Vector3.UnitY), [Effect(100)])).ToArray());
        frame = Drive(2);
        world.Step(frame, world.State.Vehicles.Select(state => new VehicleStepRequest(state.VehicleId, frame, new VehicleObservation(state.ObservedPhysics, Vector3.UnitY))).ToArray());
        Assert.That(world.State.Vehicles.All(state => state.CanInteract && state.LifeId == 2), Is.True);
        Assert.That(world.GetVehicle(1).ObservedPhysics.Position, Is.Not.EqualTo(world.GetVehicle(9).ObservedPhysics.Position));
    }

    /// <summary>Delayed controls from an old life remain acknowledgeable but cannot move the new life.</summary>
    [Test]
    public void OldLifeInputsAreNeutralizedWithoutStallingAcknowledgements()
    {
        var host = new HostVehicleSession(7, respawnConfiguration: new RespawnConfiguration { DelayTicks = 1 });
        host.Join(42);
        host.Step(default, state => new VehicleObservation(state.Movement.Physics, Vector3.UnitY, state.VehicleId == 2 ? [new VehicleContact(-Vector3.UnitX * 60, Vector3.UnitX, 0, 0)] : null));
        host.Step(default, state => new VehicleObservation(state.Movement.Physics, Vector3.UnitY));
        Assert.That(host.World.GetVehicle(2).LifeId, Is.EqualTo(2));
        Assert.That(host.Receive(42, 7, [new SequencedInput(1, Drive(1))], 1), Is.True);
        host.Step(default, state => new VehicleObservation(state.Movement.Physics, Vector3.UnitY));
        Assert.That(host.World.GetVehicle(2).Movement.Physics.LinearVelocity.Z, Is.Zero);
        Assert.That(host.Snapshot().Vehicles.Single(vehicle => vehicle.State.VehicleId == 2).AcknowledgedInput, Is.EqualTo(1));
        Assert.That(host.Receive(42, 7, [new SequencedInput(2, Drive(2))], 2), Is.True);
        host.Step(default, state => new VehicleObservation(state.Movement.Physics, Vector3.UnitY));
        Assert.That(host.World.GetVehicle(2).Movement.Physics.LinearVelocity.Length(), Is.GreaterThan(0));
    }

    /// <summary>The live pickup authority rejects both inactive phases before selecting an item or consuming a marker.</summary>
    [Test]
    public void DeadAndRespawningPlayersCannotClaimLivePickups()
    {
        World world = Create();
        var items = new ItemAuthority();
        int selections = 0;
        var spawns = new ItemSpawnAuthority(world.Arena, items, selector: () =>
        {
            selections++;
            return HeldItem.Wrench;
        });
        ArenaSpawn marker = world.Arena.Items[0];
        var pose = new VehiclePhysicsState(marker.Position, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        VehicleSnapshot initial = world.GetVehicle(1);
        world.Restore(new SimulationState(0, default, [new VehicleSnapshot(1, 1, new VehicleState(0, pose, false, false, 0, 0), initial.Damage, pose)]));
        Step(world, items, 120);
        for (int i = 0; i < 4; i++)
        {
            spawns.Advance(world);
            Assert.That(world.GetVehicle(1).Movement.Physics.Position, Is.EqualTo(marker.Position));
            Assert.That(spawns.TryPickup(world, marker.Id, 1), Is.False);
            Assert.That(spawns.States.Single(state => state.Id == marker.Id).Available, Is.True);
            Assert.That(spawns.Revision, Is.Zero);
            Assert.That(items.Slots, Is.Empty);
            Assert.That(selections, Is.Zero);
            Step(world, items);
        }

        Assert.That(world.GetVehicle(1).CanInteract, Is.True);
    }

    private static World Create(bool clear = true, ulong delay = 4)
    {
        var world = new World(new SimulationConfiguration(60), new RespawnConfiguration { DelayTicks = delay, ClearHeldItemOnDeath = clear });
        world.AddVehicle(1, new(), new DamageConfiguration { MaxHP = 120 }, PrototypeArena.Configuration.Spawn(0));
        return world;
    }

    private static InputFrame Drive(ulong tick) => new(tick, 32767, 65535, 0, InputButtons.Drift | InputButtons.UseItem, InputButtons.UseItem, 0);
    private static VehicleEffectRequest Effect(float damage, ulong source = 0) => new(new DamageEffect(damage, Vector3.One * 1000, Vector3.One), new DamageContext("missile", source, "lifecycle-test"));

    private static void Step(World world, ItemAuthority items, float damage = 0)
    {
        InputFrame frame = Drive(world.State.Tick + 1);
        items.Step(world, frame, world.State.Vehicles.Select(state => new VehicleStepRequest(state.VehicleId, frame, new VehicleObservation(state.Movement.Physics, Vector3.UnitY), state.VehicleId == 1 && damage > 0 ? [Effect(damage)] : null)).ToArray(), (_, _) => null);
    }
}
