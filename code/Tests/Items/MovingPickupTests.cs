using System.Numerics;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Sessions;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class MovingPickupTests
{
    private static readonly Vector3 Marker = PrototypeArena.Configuration.Items[0].Position;

    [TestCase(-4f, 4f, 0f, true)]
    [TestCase(-0.6f, 0.6f, 2.95f, true)]
    [TestCase(-0.6f, 0.6f, 3.01f, false)]
    [TestCase(-4f, -3.1f, 0f, false)]
    [TestCase(0f, 0f, 0f, true)]
    [TestCase(0f, 0f, 3.01f, false)]
    public void CommittedSegmentUsesUnchangedThreeMetreVolume(float from, float to, float offset, bool collected)
    {
        var host = Create();
        Place(host, new(from, 0, offset));
        Move(host, new(to, 0, offset));
        host.CollectPickups();
        Assert.That(host.Items.Slots.Count, Is.EqualTo(collected ? 1 : 0));
        Assert.That(host.Spawns!.States[0].Available, Is.EqualTo(!collected));
    }

    [Test]
    public void FullInventoryRejectsWithoutDrawingAndLaterCrossingCanFillFreedSlot()
    {
        var host = Create();
        Place(host, new(-4, 0, 0));
        host.Items.Grant(host.World, 1, HeldItem.Wrench);
        host.Items.Grant(host.World, 1, HeldItem.ProxyMine);
        var full = host.Items.Slots.Single();
        ulong random = host.Spawns!.RandomState;
        Move(host, new(4, 0, 0));
        host.CollectPickups();
        Assert.That(host.Spawns.RandomState, Is.EqualTo(random));
        Assert.That(host.Spawns.Balances, Is.Empty);
        Assert.That(host.Spawns.States[0].Available, Is.True);
        Assert.That(host.Items.Slots.Single(), Is.EqualTo(full));
        host.UseItem(0, host.SessionId, full.Life, full.Token);
        Move(host, new(-4, 0, 0));
        host.CollectPickups();
        Assert.That(host.Items.Slots.Single().Full, Is.True);
        Assert.That(host.Items.Slots.Single().SecondToken, Is.EqualTo(full.SecondToken));
        Assert.That(host.Spawns.Balances.Single().Total, Is.EqualTo(1));
    }

    [Test]
    public void ContentionCooldownAndRepeatedCollectionKeepOneAwardPerBoundary()
    {
        var host = Create();
        host.Join(42);
        Place(host, new(-4, 0, 0));
        Move(host, new(4, 0, 0));
        host.CollectPickups();
        host.CollectPickups();
        Assert.That(host.Items.Slots.Single().Vehicle, Is.EqualTo(1));
        ulong token = host.Items.TokenHighWater;
        Move(host, new(-4, 0, 0));
        host.CollectPickups();
        Assert.That(host.Items.TokenHighWater, Is.EqualTo(token));
        Move(host, new(4, 0, 0));
        host.CollectPickups();
        Assert.That(host.Items.Slots.Single().Full, Is.True);
        Assert.That(host.Spawns!.Balances.Single().Total, Is.EqualTo(2));
    }

    [Test]
    public void RestoredWorldCannotReuseOldMovementWindow()
    {
        var host = Create();
        Place(host, new(-4, 0, 0));
        Move(host, new(4, 0, 0));
        Place(host, new(12, 0, 0));
        host.CollectPickups();
        Assert.That(host.Items.Slots, Is.Empty);
        Move(host, new(13, 0, 0));
        host.CollectPickups();
        Assert.That(host.Items.Slots, Is.Empty);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void LifeDiscontinuitiesAndDeadStartsDoNotSweep(bool lifeChanged)
    {
        var host = Create();
        Place(host, new(-4, 0, 0));
        var previous = host.World.State.Vehicles.ToDictionary(v => v.VehicleId, v =>
            new VehicleSnapshot(v.VehicleId, v.LifeId + (lifeChanged ? 1ul : 0), v.Movement,
                lifeChanged ? v.Damage : new VehicleDamageState(100, 0, null, null), v.ObservedPhysics));
        Move(host, new(4, 0, 0));
        host.Spawns!.Collect(host.World, previous);
        Assert.That(host.Items.Slots, Is.Empty);
    }

    [Test]
    public void CheckpointRestoreStartsFreshSweepWithoutReplayingOldCrossing()
    {
        var host = Create();
        Place(host, new(-4, 0, 0));
        Move(host, new(4, 0, 0));
        var items = new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], [], host.Spawns!.States);
        var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(items, host.World.State.Match!, null, host.Configuration)));
        var restored = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 1);
        restored.CollectPickups();
        Assert.That(restored.Items.Slots, Is.Empty);
        Move(restored, new(-4, 0, 0));
        restored.CollectPickups();
        Assert.That(restored.Items.Slots.Count, Is.EqualTo(1));
        Assert.That(restored.Spawns!.Balances.Single().Total, Is.EqualTo(1));
    }

    private static HostVehicleSession Create()
    {
        var host = new HostVehicleSession(99);
        host.RegisterSpawns(PrototypeArena.Configuration, new() { CooldownTicks = 2 });
        return host;
    }

    private static void Place(HostVehicleSession host, Vector3 offset)
    {
        var world = host.World.State;
        var pose = new VehiclePhysicsState(Marker + offset, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        host.World.Restore(new SimulationState(world.Tick, world.LastInput, world.Vehicles.Select(v =>
            new VehicleSnapshot(v.VehicleId, v.LifeId, new VehicleState(world.Tick, pose, false, false, 0, 0), v.Damage, pose)), world.Match));
    }

    private static void Move(HostVehicleSession host, Vector3 offset) => host.Step(default, v =>
        new VehicleObservation(new VehiclePhysicsState(Marker + offset, Quaternion.Identity, Vector3.Zero, Vector3.Zero), Vector3.UnitY));
}
