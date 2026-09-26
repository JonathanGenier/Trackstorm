using Trackstorm.Core.Input;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Matches;

internal sealed class SynchronizedStartTests
{
    [Test]
    public void FullHandoffStartsOneFixedCountdownEvenBelowConfiguredMinimum()
    {
        var host = new HostVehicleSession(173, matchConfiguration: new() { MinimumPlayers = 8, CountdownTicks = 1 }, requireActiveMatch: true);
        host.JoinPlayer(2, 2);
        Assert.That(host.World.InitializeMatch(new(173, 0, [1])), Is.False);
        Assert.That(host.World.State.Match!.Phase, Is.EqualTo(MatchPhase.Waiting));
        var context = new SynchronizedMatchContext(173, 0, [1, 2]);
        Assert.That(host.World.InitializeMatch(context), Is.True);
        Assert.That(host.World.InitializeMatch(context), Is.False);
        Assert.That(host.World.State.Match!.CountdownAtTick, Is.EqualTo(300));
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["match.countdown_ticks"] = 120 }, out _), Is.True);
        Assert.That(host.World.State.Match!.CountdownAtTick, Is.EqualTo(300), "Live development tuning cannot move an application GO deadline.");
        Assert.That(host.World.Events.Entries.Count(entry => entry.Category == Core.Events.EventCategory.Match && entry.Kind == "Countdown"), Is.EqualTo(1));
        host.Leave(2);
        for (int tick = 1; tick <= 300; tick++)
        {
            Assert.That(host.AllowsParticipation, Is.False);
            host.Step(new InputFrame(0, 0, ushort.MaxValue, 0, 0, 0, 0), Observe);
            Assert.That(host.World.State.LastInput.Accelerate, Is.Zero);
            Assert.That(host.World.State.Match!.Lifecycle.RemainingMatchTicks((ulong)tick), Is.EqualTo(36000));
            Assert.That(host.World.State.Match.Phase, Is.EqualTo(tick < 300 ? MatchPhase.Countdown : MatchPhase.Active));
        }
        Assert.That(host.World.State.Match!.ActiveStartedAtTick, Is.EqualTo(300));
        host.Step(default, Observe);
        Assert.That(host.World.State.Match!.Lifecycle.RemainingMatchTicks(301), Is.EqualTo(35999));
        Assert.That(host.World.InitializeMatch(context), Is.False);
    }

    [Test]
    public void VehicleCountAndForceStartCannotBypassApplicationSynchronization()
    {
        var host = new HostVehicleSession(173, matchConfiguration: new() { MinimumPlayers = 1, CountdownTicks = 1 }, requireActiveMatch: true);
        host.ForceStart(0);
        for (int tick = 0; tick < 5; tick++) host.Step(default, Observe);
        Assert.That(host.World.State.Match!.Phase, Is.EqualTo(MatchPhase.Waiting));
        Assert.That(host.AllowsParticipation, Is.False);
    }

    private static VehicleObservation Observe(VehicleSnapshot state) => new(state.Movement.Physics, System.Numerics.Vector3.UnitY);
}
