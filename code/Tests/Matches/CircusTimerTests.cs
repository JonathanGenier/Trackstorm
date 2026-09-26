using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Matches;

internal sealed class CircusTimerTests
{
    [Test]
    public void FullDefaultDurationStartsAfterCountdownAndFinishesExactlyOnce()
    {
        var host = Create();
        for (int i = 0; i < 181; i++) Step(host);
        var active = host.World.State.Match!;
        Assert.That(active.Phase, Is.EqualTo(MatchPhase.Active));
        Assert.That(active.Lifecycle.RemainingMatchTicks(host.World.State.Tick), Is.EqualTo(36000));
        for (int i = 0; i < 35999; i++) Step(host);
        Assert.That(host.World.State.Match!.Phase, Is.EqualTo(MatchPhase.Active));
        Assert.That(host.World.State.Match.Lifecycle.RemainingMatchTicks(host.World.State.Tick), Is.EqualTo(1));
        Step(host);
        var final = host.World.State.Match!;
        Assert.That(final.Tick, Is.EqualTo(36181));
        Assert.That(final.Winner, Is.EqualTo(1), "All-zero ties use the existing stable identity order.");
        Assert.That(final.Lifecycle.Outcome!.Reason, Is.EqualTo("time-limit"));
        for (int i = 0; i < 10; i++) Step(host, kill: true);
        Assert.That(host.World.State.Match, Is.SameAs(final));
        Assert.That(host.World.Events.Entries.Count(entry => entry.Kind == "Finished"), Is.EqualTo(1));
    }

    [Test]
    public void SixKillsDoNotFinishAndScoreLeaderBeatsKillLeaderAtExpiry()
    {
        var host = Create(1);
        Step(host); Step(host);
        for (int i = 0; i < 6; i++)
        {
            Step(host, kill: true);
            Step(host); Step(host); Step(host);
        }
        var match = host.World.State.Match!;
        Assert.That(match.Phase, Is.EqualTo(MatchPhase.Active));
        Assert.That(match.Players[0].Kills, Is.EqualTo(6));
        Assert.That(match.Players[0].CircusScore, Is.GreaterThan(0));
        Assert.That(match.Players[0].KdMultiplier, Is.EqualTo(6));
        var state = host.World.State;
        var leader = new MatchState(state.Tick, match.Revision + 1, match.KillTarget, match.Phase, null, null,
            match.Players.Select(row => row.Player == 2 ? row with { CircusScore = 100000 } : row),
            activeStartedAtTick: match.ActiveStartedAtTick, durationTicks: match.DurationTicks);
        host.World.Restore(new(state.Tick, state.LastInput, state.Vehicles, leader));
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["match.duration_ticks"] = state.Tick - match.ActiveStartedAtTick!.Value + 1 }, out _), Is.True);
        Step(host);
        var final = host.World.State.Match!;
        Assert.That(final.Winner, Is.EqualTo(2));
        Assert.That(final.FinalResults!.Standings[0].PlayerId, Is.EqualTo(2));
        Assert.That(final.Players[0].Kills, Is.EqualTo(6));
        Step(host, kill: true);
        Assert.That(host.World.State.Match, Is.SameAs(final));
    }

    [Test]
    public void AdmissionResumeCodecAndSuccessorKeepClockAndScores()
    {
        var host = Create(1);
        Step(host); Step(host); Step(host, kill: true);
        for (int i = 0; i < 123; i++) Step(host);
        var match = host.World.State.Match!;
        ulong remaining = match.Lifecycle.RemainingMatchTicks(host.World.State.Tick);
        var admission = host.PrepareJoin(3, 1)!;
        Assert.That(admission.Match.Lifecycle.RemainingMatchTicks(admission.Items.World.Tick), Is.EqualTo(remaining));
        host.Suspend(10);
        Assert.That(host.ResumePlayer(11, 2), Is.True);
        var clean = new MatchState(match.Tick, match.Revision, match.KillTarget, match.Phase, null, null, match.Players,
            activeStartedAtTick: match.ActiveStartedAtTick, durationTicks: match.DurationTicks);
        var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(new ItemPublication(1, host.Snapshot(), host.Items.Slots, host.Items.Missiles, []), clean, null, host.Configuration)));
        var successor = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 2);
        Assert.That(successor.World.State.Match!.Players, Is.EqualTo(match.Players));
        Assert.That(successor.World.State.Match.Lifecycle.RemainingMatchTicks(successor.World.State.Tick), Is.EqualTo(remaining));
        successor.World.AccountMatchRecoveryTime(180);
        Assert.That(successor.World.State.Match!.Lifecycle.RemainingMatchTicks(successor.World.State.Tick), Is.EqualTo(remaining - 180));
        var encoded = MatchCodec.Decode(MatchCodec.Encode(1, successor.World.State.Match)).State;
        Assert.That(encoded.RecoveryElapsedTicks, Is.EqualTo(180));
        Assert.That(encoded.Players, Is.EqualTo(match.Players));
        Step(successor);
        Assert.That(successor.World.State.Match!.Lifecycle.RemainingMatchTicks(successor.World.State.Tick), Is.EqualTo(remaining - 181));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void HostOnlyDurationEditsDoNotRestartAndExpiredBudgetRejectsLateScoring(bool recovery)
    {
        var host = Create(1);
        Step(host); Step(host);
        for (int i = 0; i < 60; i++) Step(host);
        ulong? started = host.World.State.Match!.ActiveStartedAtTick;
        Assert.That(host.TryConfigure(10, new Dictionary<string, double> { ["match.duration_ticks"] = 600 }, out _), Is.False);
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["match.duration_ticks"] = 600 }, out _), Is.True);
        Assert.That(host.World.State.Match!.ActiveStartedAtTick, Is.EqualTo(started));
        Assert.That(host.World.State.Match.Lifecycle.RemainingMatchTicks(host.World.State.Tick), Is.EqualTo(540));
        var config = GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(1, host.Configuration));
        Assert.That(config.State.Configuration.Match.DurationTicks, Is.EqualTo(600));
        Assert.That(DeveloperSettingsFile.Read(new DeveloperSettingsFile().Write(host.Configuration.Configuration)).Configuration.Match.DurationTicks, Is.EqualTo(600));
        foreach (double invalid in new[] { 0d, -1d, 216001d, 0.5, double.NaN })
            Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["match.duration_ticks"] = invalid }, out _), Is.False);
        if (recovery) host.World.AccountMatchRecoveryTime(600);
        else Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["match.duration_ticks"] = 1 }, out _), Is.True);
        Step(host, kill: true);
        Assert.That(host.World.State.Match!.Phase, Is.EqualTo(MatchPhase.Finished));
        Assert.That(host.World.State.Match.Players.Sum(row => row.Kills), Is.Zero);
        var final = host.World.State.Match;
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["match.duration_ticks"] = 36000 }, out _), Is.True);
        Assert.That(host.World.State.Match, Is.SameAs(final));
    }

    [Test]
    public void RecoveryAcrossCountdownConsumesOnlyTheActiveInterval()
    {
        var host = Create();
        Step(host);
        host.World.AccountMatchRecoveryTime(60);
        Assert.That(host.World.State.Match!.CountdownAtTick, Is.EqualTo(121));
        Assert.That(host.World.State.Match.Lifecycle.RemainingMatchTicks(1), Is.EqualTo(36000));
        host.World.AccountMatchRecoveryTime(180);
        Assert.That(host.World.State.Match!.Phase, Is.EqualTo(MatchPhase.Active));
        Assert.That(host.World.State.Match.Lifecycle.RemainingMatchTicks(1), Is.EqualTo(35940));
        Step(host);
        Assert.That(host.World.State.Match!.Lifecycle.RemainingMatchTicks(2), Is.EqualTo(35939));
    }

    private static HostVehicleSession Create(ulong countdown = 180)
    {
        var host = new HostVehicleSession(1, matchConfiguration: new() { CountdownTicks = countdown }, respawnConfiguration: new() { DelayTicks = 1 });
        host.JoinPlayer(10, 2);
        return host;
    }

    private static void Step(HostVehicleSession host, bool kill = false)
    {
        var frame = new InputFrame(host.World.State.Tick + 1, 0, 0, 0, 0, 0, 0);
        host.World.Step(frame, host.World.State.Vehicles.Select(vehicle => new VehicleStepRequest(vehicle.VehicleId, frame,
            new VehicleObservation(vehicle.ObservedPhysics, Vector3.UnitY), kill && vehicle.VehicleId == 2
                ? [new VehicleEffectRequest(new DamageEffect(10000, Vector3.Zero, Vector3.Zero), new DamageContext("missile", 1, "timer verification"))] : [])).ToArray());
    }
}
