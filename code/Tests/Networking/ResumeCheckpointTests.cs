using System.Numerics;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Networking;

/// <summary>Authoritative state retention and complete reconnect serialization.</summary>
[TestFixture]
internal sealed class ResumeCheckpointTests
{
    /// <summary>Grace neutralizes queued driving/item actions and retains one live vehicle and slot.</summary>
    [Test]
    public void SuspensionRetainsStateAndRebindsWithoutReplayingPendingCommands()
    {
        var host = new HostVehicleSession(100);
        host.RegisterSpawns(PrototypeArena.Configuration);
        host.JoinPlayer(10, 2);
        host.Items.Grant(host.World, 2, HeldItem.Missile);
        var slot = host.Items.Slots.Single();
        host.Receive(10, 100, [new SequencedInput(1, new InputFrame(1, 0, ushort.MaxValue, 0, 0, InputButtons.UseItem, 0))]);
        host.UseItem(10, 100, 1, slot.Token);
        host.Suspend(10);
        Assert.That(host.Receive(10, 100, [new SequencedInput(2, default)]), Is.False);
        Assert.That(host.UseItem(10, 100, 1, slot.Token), Is.False);
        host.Step(default, Observe);
        Assert.That(host.World.State.Vehicles.Count, Is.EqualTo(2));
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.Missile));
        Assert.That(host.Items.Missiles, Is.Empty);
        var before = host.World.GetVehicle(2);
        Assert.That(host.ResumePlayer(20, 2), Is.True);
        Assert.That(host.ResumePlayer(21, 2), Is.False);
        Assert.That(host.World.GetVehicle(2), Is.SameAs(before));
        Assert.That(host.Snapshot().Vehicles.Single(v => v.State.VehicleId == 2).AcknowledgedInput, Is.Zero);
        Assert.That(host.Receive(20, 100, [new SequencedInput(1, default)]), Is.True);
        host.Step(default, Observe);
        Assert.That(host.Snapshot().Vehicles.Single(v => v.State.VehicleId == 2).AcknowledgedInput, Is.EqualTo(1));
        host.Suspend(20);
        host.ExpirePlayer(2);
        host.ExpirePlayer(2);
        Assert.That(host.World.State.Vehicles.Count, Is.EqualTo(1));
        Assert.That(host.World.State.Match!.Players.Any(p => p.Player == 2), Is.True);
    }

    /// <summary>The checkpoint preserves every current replicated component and does not replay historical outcomes.</summary>
    [Test]
    public void CheckpointRoundTripsAndReseedsPredictionAtAuthoritativeTick()
    {
        var host = new HostVehicleSession(100);
        host.RegisterSpawns(PrototypeArena.Configuration);
        host.JoinPlayer(10, 2);
        host.Items.Grant(host.World, 2, HeldItem.Wrench);
        for (uint sequence = 1; sequence <= 5; sequence++)
        {
            host.Receive(10, 100, [new SequencedInput(sequence, default)]);
            host.Step(default, Observe);
        }

        WorldSnapshot world = host.Snapshot();
        var items = new ItemPublication(10, world, host.Items.Slots, host.Items.Missiles, [], host.Spawns!.States);
        MatchState match = host.World.State.Match!;
        var props = new ArenaPropSnapshot(100, world.Tick, [new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero), new VehiclePhysicsState(Vector3.One, Quaternion.Identity, Vector3.Zero, Vector3.Zero), new VehiclePhysicsState(Vector3.UnitX, Quaternion.Identity, Vector3.Zero, Vector3.Zero)]);
        byte[] bytes = ResumeCheckpointCodec.Encode(new ResumeCheckpoint(items, match, props));
        var restored = ResumeCheckpointCodec.Decode(bytes);
        Assert.That(ItemCodec.EncodeState(restored.Items), Is.EqualTo(ItemCodec.EncodeState(items)));
        Assert.That(MatchCodec.Encode(100, restored.Match), Is.EqualTo(MatchCodec.Encode(100, match)));
        Assert.That(restored.Props!.Bodies, Is.EqualTo(props.Bodies));
        var predicted = new PredictedVehicle(restored.Items.World.Vehicles.Single(v => v.State.VehicleId == 2));
        Assert.That(predicted.History.Pending, Is.Empty);
        Assert.That(predicted.History.LastAcknowledged, Is.EqualTo(5));
        Assert.That(predicted.State.Movement.Tick, Is.EqualTo(5));
        predicted.Predict(default, Observe);
        Assert.That(predicted.History.Pending.Single().Sequence, Is.EqualTo(6));
        Assert.That(predicted.State.Movement.Tick, Is.EqualTo(6));
        Assert.That(predicted.Reconcile(world.Vehicles.Single(v => v.State.VehicleId == 2), Observe), Is.False);
        bytes[3] = 255;
        Assert.Throws<ArgumentException>(() => ResumeCheckpointCodec.Decode(bytes));
    }

    /// <summary>Dead-state resume preserves the absolute respawn deadline and consumed scoring life, without replaying the kill.</summary>
    [Test]
    public void DeadResumeRetainsDeadlineAndScoreWithoutReplayingDeath()
    {
        var host = new HostVehicleSession(100, respawnConfiguration: new RespawnConfiguration { DelayTicks = 4 }, matchConfiguration: new MatchConfiguration { CountdownTicks = 1 });
        host.JoinPlayer(10, 2);
        host.Step(default, Observe);
        host.Step(default, Observe);
        host.Suspend(10);
        ulong tick = host.World.State.Tick + 1;
        var frame = new InputFrame(tick, 0, 0, 0, 0, 0, 0);
        host.World.Step(frame, host.World.State.Vehicles.Select(vehicle => new VehicleStepRequest(vehicle.VehicleId, frame, Observe(vehicle), vehicle.VehicleId == 2 ? [new VehicleEffectRequest(new DamageEffect(100, Vector3.Zero, Vector3.Zero), new DamageContext("missile", 1, "resume-test"))] : [])).ToArray());
        VehicleSnapshot dead = host.World.GetVehicle(2);
        Assert.That(dead.Lifecycle, Is.EqualTo(VehicleLifecycle.Dead));
        MatchState state = host.World.State.Match!;
        var match = new MatchState(state.Tick, state.Revision, state.KillTarget, state.Phase, state.CountdownAtTick, state.Winner, state.Players);
        var items = new ItemPublication(1, host.Snapshot(), [], [], []);
        var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new ResumeCheckpoint(items, match, null)));
        Assert.That(checkpoint.Items.World.Vehicles.Single(v => v.State.VehicleId == 2).State.RespawnAtTick, Is.EqualTo(dead.RespawnAtTick));
        Assert.That(checkpoint.Match.Changes, Is.Empty);
        Assert.That(checkpoint.Match.Players.Single(p => p.Player == 1).Kills, Is.EqualTo(1));
        Assert.That(checkpoint.Match.Players.Single(p => p.Player == 1).CircusScore, Is.EqualTo(100));
        Assert.That(checkpoint.Match.Players.Single(p => p.Player == 1).KillStreak, Is.EqualTo(1));
        Assert.That(checkpoint.Match.Players.Single(p => p.Player == 2).ProcessedLife, Is.EqualTo(dead.LifeId));
        Assert.That(host.ResumePlayer(20, 2), Is.True);
        for (int i = 0; i < 6; i++)
        {
            host.Step(default, Observe);
        }

        Assert.That(host.World.GetVehicle(2).LifeId, Is.EqualTo(dead.LifeId + 1));
        Assert.That(host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(100));
        Assert.That(host.World.State.Match!.Players.Single(p => p.Player == 1).Kills, Is.EqualTo(1));
        Assert.That(host.World.State.Match.Players.Single(p => p.Player == 2).Deaths, Is.EqualTo(1));
        Assert.That(host.World.State.Match.Players, Is.EqualTo(checkpoint.Match.Players));
    }

    [Test]
    public void PendingStuntsSurviveAdmissionRebindAndAuthorityRestoreWithLiveTuning()
    {
        var host = new HostVehicleSession(103, matchConfiguration: new() { CountdownTicks = 1, KillTarget = 20 });
        host.JoinPlayer(10, 2);
        host.Step(default, Observe);
        host.Step(default, Observe);
        VehicleObservation Moving(VehicleSnapshot vehicle) => new(new VehiclePhysicsState(vehicle.ObservedPhysics.Position, Quaternion.Identity, new(0, 0, -(new VehicleConfiguration().ForwardSpeed * 0.97f)), Vector3.Zero), Vector3.UnitY);
        for (int i = 0; i < 60; i++) { host.Step(default, Moving); }
        Assert.That(host.World.State.Match!.Players.All(player => player.CircusScore == 0 && player.Stunts!.TopSpeed.Ticks == 60), Is.True);
        var preview = host.PrepareJoin(3, 1)!;
        Assert.That(preview.Match.Players.Take(2), Is.EqualTo(host.World.State.Match.Players));
        Assert.That(preview.Match.Players.Last().Stunts, Is.Null);
        host.Suspend(10);
        var state = host.World.State.Match!;
        var retained = new MatchState(state.Tick, state.Revision, state.KillTarget, state.Phase, state.CountdownAtTick, state.Winner, state.Players, mode: state.Mode);
        var checkpoint = new ResumeCheckpoint(new ItemPublication(1, host.Snapshot(), [], [], []), retained, null, host.Configuration);
        var restored = HostVehicleSession.Restore(ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(checkpoint)), host.CaptureAuthority(), 1);
        Assert.That(restored.ResumePlayer(20, 2), Is.True);
        Assert.That(restored.TryConfigure(0, new Dictionary<string, double> { ["match.top_speed_enter_ratio"] = 0.99, ["match.top_speed_exit_ratio"] = 0.98 }, out _), Is.True);
        restored.Step(default, Moving);
        Assert.That(restored.World.State.Match!.Players.All(player => Math.Abs(player.CircusScore - 5) < 1e-9 && player.Stunts is null), Is.True, "Live threshold edit completes each retained event once.");
        for (int i = 0; i < 60; i++) { restored.Step(default, Moving); }
        Assert.That(restored.World.State.Match.Players.All(player => Math.Abs(player.CircusScore - 5) < 1e-9), Is.True);
        Assert.That(restored.ResumePlayer(30, 2), Is.False, "Duplicate rebind cannot restart a completed award.");
        Assert.That(host.World.State.Match.Players.All(player => player.CircusScore == 0), Is.True, "Preview and restoration never mutate original authority.");
    }

    [Test]
    public void AbandoningVehicleCancelsPendingWithoutRemovingBankedScore()
    {
        var host = new HostVehicleSession(103, matchConfiguration: new() { CountdownTicks = 1 });
        host.JoinPlayer(10, 2);
        host.Step(default, Observe);
        host.Step(default, Observe);
        for (int i = 0; i < 60; i++)
        {
            host.Step(default, vehicle => new(new VehiclePhysicsState(vehicle.ObservedPhysics.Position, Quaternion.Identity, new(0, 0, -(new VehicleConfiguration().ForwardSpeed * 0.97f)), Vector3.Zero), Vector3.UnitY));
        }
        host.World.LeaveVehicle(2);
        Assert.That(host.World.State.Match!.Players.Single(player => player.Player == 2).Stunts, Is.Null);
        Assert.That(host.World.State.Match.Players.Single(player => player.Player == 1).Stunts!.TopSpeed.Ticks, Is.EqualTo(60));
    }
    private static VehicleObservation Observe(VehicleSnapshot state) => new(state.Movement.Physics, Vector3.UnitY);

    /// <summary>Nonlethal banked points, tuning and sequence memory survive actual checkpoint/authority reconstruction.</summary>
    [Test]
    public void CircusCollisionAndLiveTuningContinueAcrossAuthorityRestore()
    {
        var host = new HostVehicleSession(100, matchConfiguration: new MatchConfiguration { CountdownTicks = 1, KillTarget = 20 });
        host.JoinPlayer(10, 2);
        host.Step(default, Observe);
        host.Step(default, Observe);
        Assert.That(host.TryConfigure(0, new Dictionary<string, double>
        {
            ["match.base_kill_points"] = 80,
            ["match.kill_streak_bonus_step"] = 10,
            ["match.collision_points_per_damage"] = 2.5,
        }, out _), Is.True);

        void Hit(HostVehicleSession authority, float damage, ulong reset = 0)
        {
            var frame = new InputFrame(authority.World.State.Tick + 1, 0, 0, 0, 0, 0, 0);
            authority.World.Step(frame, authority.World.State.Vehicles.Select(vehicle => new VehicleStepRequest(vehicle.VehicleId, frame, Observe(vehicle),
                vehicle.VehicleId == 2 && reset == 0 ? [new VehicleEffectRequest(new DamageEffect(damage, Vector3.Zero, Vector3.Zero), new DamageContext("collision", 1, "restore"))] : [],
                reset: vehicle.VehicleId == reset ? vehicle.ObservedPhysics : null)).ToArray());
        }

        Hit(host, 10);
        Assert.That(host.World.State.Match!.Players[0].CircusScore, Is.EqualTo(25));
        host.Suspend(10);
        var state = host.World.State.Match!;
        var retained = new MatchState(state.Tick, state.Revision, state.KillTarget, state.Phase, state.CountdownAtTick, state.Winner, state.Players, mode: state.Mode);
        var checkpoint = new ResumeCheckpoint(new ItemPublication(1, host.Snapshot(), [], [], []), retained, null, host.Configuration);
        var decoded = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(checkpoint));
        var restored = HostVehicleSession.Restore(decoded, host.CaptureAuthority(), 1);
        Assert.That(restored.World.State.Match!.Players, Is.EqualTo(host.World.State.Match.Players));
        Assert.That(restored.ResumePlayer(20, 2), Is.True);
        Hit(restored, 1000);
        Assert.That(restored.World.State.Match!.Players[0].CircusScore, Is.EqualTo(330));
        Hit(restored, 0, 2);
        Hit(restored, 1000);
        Assert.That(restored.World.State.Match!.Players[0].CircusScore, Is.EqualTo(760), "New life earns 250 collision points at x1 then (80 + 10) kill points at x2.");
    }
}
