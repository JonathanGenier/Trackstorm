using System.Numerics;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Networking;

/// <summary>Fresh admission reuses complete checkpoints without modifying existing gameplay.</summary>
[TestFixture]
internal sealed class ActiveJoinCheckpointTests
{
    /// <summary>Current death, scoring, cooldowns, inventory and native props survive a fresh bootstrap.</summary>
    [Test]
    public void BootstrapPreservesCurrentStateAndOmitsHistoricalOutcomes()
    {
        var host = new HostVehicleSession(100, matchConfiguration: new MatchConfiguration { CountdownTicks = 1 });
        host.RegisterSpawns(PrototypeArena.Configuration, selector: () => HeldItem.Wrench);
        host.JoinPlayer(10, 2);
        host.Step(default, Observe);
        host.Step(default, Observe);
        var pose = new VehiclePhysicsState(PrototypeArena.Configuration.Items[0].Position, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        var states = host.World.State.Vehicles.Select(state => state.VehicleId != 1 ? state : new VehicleSnapshot(1, 1, new VehicleState(host.World.State.Tick, pose, false, false, 0, 0), state.Damage, pose));
        host.World.Restore(new SimulationState(host.World.State.Tick, host.World.State.LastInput, states, host.World.State.Match));
        Assert.That(host.Spawns!.TryPickup(host.World, PrototypeArena.Configuration.Items[0].Id, 1), Is.True);
        ulong tick = host.World.State.Tick + 1;
        var input = new InputFrame(tick, 0, 0, 0, 0, 0, 0);
        host.World.Step(input, host.World.State.Vehicles.Select(vehicle => new VehicleStepRequest(vehicle.VehicleId, input, Observe(vehicle), vehicle.VehicleId == 2 ? [new VehicleEffectRequest(new DamageEffect(100, Vector3.Zero, Vector3.Zero), new DamageContext("missile", 1, "test"))] : [])).ToArray());
        var before = host.World.State;
        var journal = host.World.Events.LastSequence;
        var props = new ArenaPropSnapshot(100, tick, [pose, pose, pose]);
        var candidate = host.PrepareJoin(3, 10, props)!;
        var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(candidate));
        Assert.That(host.World.State, Is.EqualTo(before));
        Assert.That(host.World.Events.LastSequence, Is.EqualTo(journal));
        Assert.That(checkpoint.Items.World.Vehicles.Take(2).Select(vehicle => vehicle.State.RespawnAtTick), Is.EqualTo(before.Vehicles.Select(vehicle => vehicle.RespawnAtTick)));
        Assert.That(checkpoint.Items.World.Vehicles.Single(vehicle => vehicle.State.VehicleId == 2).State.Lifecycle, Is.EqualTo(VehicleLifecycle.Dead));
        Assert.That(checkpoint.Items.Spawns, Is.EqualTo(host.Spawns.States));
        Assert.That(checkpoint.Items.Spawns[0].Available, Is.False);
        Assert.That(checkpoint.Items.Slots, Is.EqualTo(host.Items.Slots));
        Assert.That(checkpoint.Match.Players.Take(2), Is.EqualTo(before.Match!.Players));
        Assert.That(checkpoint.Match.Players[0].Kills, Is.EqualTo(1));
        Assert.That(checkpoint.Match.Changes, Is.Empty);
        Assert.That(checkpoint.Items.Events, Is.Empty);
        Assert.That(checkpoint.Props!.Bodies, Is.EqualTo(props.Bodies));
        Assert.That(checkpoint.Configuration, Is.EqualTo(host.Configuration));
        Assert.That(host.ActivateJoin(20, 3), Is.True);
        Assert.That(host.ActivateJoin(20, 3), Is.False);
        Assert.That(host.World.State.Vehicles.Take(2), Is.EqualTo(before.Vehicles));
        Assert.That(host.World.State.Match!.Players.Take(2), Is.EqualTo(before.Match.Players));
        Assert.That(host.World.State.Tick, Is.EqualTo(tick));
    }

    /// <summary>Spawn placement rechecks current clearance at acknowledgement; finished games cannot admit.</summary>
    [Test]
    public void ActivationRechecksSpawnAndTerminalState()
    {
        var host = new HostVehicleSession(100);
        var candidate = host.PrepareJoin(2, 1)!;
        var proposed = candidate.Items.World.Vehicles.Single(vehicle => vehicle.State.VehicleId == 2).State.Movement.Physics;
        var original = host.World.GetVehicle(1);
        var blocking = new VehicleSnapshot(1, 1, new VehicleState(0, proposed, false, false, 0, 0), original.Damage, proposed);
        host.World.Restore(new SimulationState(0, default, [blocking], host.World.State.Match));
        Assert.That(host.ActivateJoin(20, 2), Is.True);
        Assert.That(host.World.GetVehicle(2).Movement.Physics.Position, Is.Not.EqualTo(proposed.Position));
        Assert.That(host.World.GetVehicle(1), Is.SameAs(blocking));
        var finished = new MatchState(0, 10, 5, MatchPhase.Finished, null, 1, [new PlayerScore(1, 5, 0, 1, 0), new PlayerScore(2, 0, 5, 0, 5)]);
        host.World.Restore(new SimulationState(0, default, host.World.State.Vehicles, finished));
        Assert.That(host.CanJoin, Is.False);
        Assert.That(host.PrepareJoin(3, 2), Is.Null);
        Assert.That(host.ActivateJoin(30, 3), Is.False);
    }

    private static VehicleObservation Observe(VehicleSnapshot state) => new(state.Movement.Physics, Vector3.UnitY);
}
