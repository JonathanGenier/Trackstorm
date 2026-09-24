using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Networking;

/// <summary>Application participation follows the retained authoritative phase through recovery.</summary>
[TestFixture]
internal sealed class GameLoopParticipationTests
{
    /// <summary>Early commands are acknowledged as neutral and cannot survive the countdown boundary.</summary>
    [Test]
    public void CountdownSuppressesHostRemoteAndItemCommands()
    {
        var host = Create();
        host.GiveItem(0, HeldItem.Missile);
        var slot = host.Items.Slots.Single();
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.False);
        Assert.That(host.SwitchItem(0, 99, slot.Life, 1), Is.False);
        Assert.That(host.Receive(10, 99, [new(1, Drive()), new(2, Drive()), new(3, Drive())]), Is.True);
        for (int tick = 0; tick < 4; tick++)
        {
            host.Step(Drive(), Observe);
            Assert.That(host.World.State.LastInput.Accelerate, Is.Zero);
            Assert.That(host.World.State.Vehicles.All(vehicle => vehicle.Movement.Physics.LinearVelocity.Z == 0), Is.True);
        }

        Assert.That(host.World.State.Match!.Phase, Is.EqualTo(MatchPhase.Active));
        Assert.That(host.Snapshot().Vehicles.Single(vehicle => vehicle.State.VehicleId == 2).AcknowledgedInput, Is.EqualTo(3));
        host.Step(default, Observe);
        Assert.That(host.World.GetVehicle(2).Movement.Physics.LinearVelocity.Z, Is.Zero, "Early held controls must not spill into Active.");
        host.Receive(10, 99, [new(4, Drive())]);
        host.Step(Drive(), Observe);
        Assert.That(host.World.State.LastInput.Accelerate, Is.EqualTo(ushort.MaxValue));
        Assert.That(host.World.GetVehicle(2).Movement.Physics.LinearVelocity.Length(), Is.GreaterThan(0));
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.True);
    }

    /// <summary>Finishing discards already queued driving and item intent without consuming the retained slot.</summary>
    [Test]
    public void FinishedNeutralizesPreviouslyAcceptedCommands()
    {
        var host = Create();
        for (int tick = 0; tick < 4; tick++)
        {
            host.Step(default, Observe);
        }

        host.GiveItem(0, HeldItem.Missile);
        var slot = host.Items.Slots.Single();
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.True);
        Assert.That(host.Receive(10, 99, [new(1, Drive()), new(2, Drive())]), Is.True);
        var world = host.World.State;
        var finished = new MatchState(world.Tick, world.Match!.Revision + 1, 1, MatchPhase.Finished, null, 1, [new(1, 1, 0, 1, 0), new(2, 0, 1, 0, 1)]);
        host.World.Restore(new SimulationState(world.Tick, world.LastInput, world.Vehicles, finished));
        host.Step(Drive(), Observe);
        Assert.That(host.Items.Slots.Single(), Is.EqualTo(slot));
        Assert.That(host.Items.Missiles, Is.Empty);
        Assert.That(host.World.State.Vehicles.All(vehicle => vehicle.Movement.Physics.LinearVelocity.Z == 0), Is.True);
        Assert.That(host.World.State.LastInput.Accelerate, Is.Zero);
        Assert.That(host.Snapshot().Vehicles.Single(vehicle => vehicle.State.VehicleId == 2).AcknowledgedInput, Is.EqualTo(1));
        Assert.That(host.World.State.Match, Is.SameAs(finished));
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.False);
    }

    /// <summary>Restore continues exact countdown/active/finished boundaries with disconnected reservations.</summary>
    /// <param name="phase">Phase at the checkpoint.</param>
    [TestCase(MatchPhase.Countdown)]
    [TestCase(MatchPhase.Active)]
    [TestCase(MatchPhase.Finished)]
    public void MigrationAndResumeRetainPhaseAndTiming(MatchPhase phase)
    {
        var host = Create();
        host.Step(default, Observe);
        if (phase != MatchPhase.Countdown)
        {
            for (int tick = 0; tick < 3; tick++)
            {
                host.Step(default, Observe);
            }
        }

        if (phase == MatchPhase.Finished)
        {
            var state = host.World.State;
            var result = new MatchState(state.Tick, state.Match!.Revision + 1, 1, MatchPhase.Finished, null, 1, [new(1, 1, 0, 1, 0), new(2, 0, 1, 0, 1)]);
            host.World.Restore(new SimulationState(state.Tick, state.LastInput, state.Vehicles, result));
        }

        var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new ResumeCheckpoint(
            new ItemPublication(1, host.Snapshot(), host.Items.Slots, host.Items.Missiles, []), host.World.State.Match!, null, host.Configuration)));
        var replacement = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 2, requireActiveMatch: true);
        Assert.That(replacement.World.State.Match!.Phase, Is.EqualTo(phase));
        Assert.That(replacement.World.State.Match.CountdownAtTick, Is.EqualTo(checkpoint.Match.CountdownAtTick));
        Assert.That(replacement.AllowsParticipation, Is.EqualTo(phase == MatchPhase.Active));
        Assert.That(replacement.World.State.Tick, Is.EqualTo(checkpoint.Items.World.Tick));
        replacement.Step(Drive(), Observe);
        Assert.That(replacement.World.State.Tick, Is.EqualTo(checkpoint.Items.World.Tick + 1));
        Assert.That(replacement.World.State.Match.Phase, Is.EqualTo(phase));
        Assert.That(replacement.ResumePlayer(20, 1), Is.True);
        Assert.That(replacement.World.State.Match.Players, Is.EqualTo(checkpoint.Match.Players));
        if (phase == MatchPhase.Countdown)
        {
            while (replacement.World.State.Tick < checkpoint.Match.CountdownAtTick)
            {
                replacement.Step(Drive(), Observe);
            }

            Assert.That(replacement.World.State.Match.Phase, Is.EqualTo(MatchPhase.Active));
        }

        if (phase == MatchPhase.Finished)
        {
            Assert.That(replacement.World.State.Match, Is.SameAs(checkpoint.Match));
            Assert.That(replacement.CanJoin, Is.False);
            Assert.That(replacement.World.State.LastInput.Accelerate, Is.Zero);
        }
    }

    private static HostVehicleSession Create()
    {
        var host = new HostVehicleSession(99, matchConfiguration: new() { CountdownTicks = 3, KillTarget = 1 }, requireActiveMatch: true);
        host.JoinPlayer(10, 2);
        return host;
    }

    private static InputFrame Drive() => new(0, 0, ushort.MaxValue, 0, 0, 0, 0);

    private static VehicleObservation Observe(VehicleSnapshot state) => new(state.Movement.Physics, Vector3.UnitY);
}
