using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Matches;

/// <summary>Mode selection, completion and reconstruction through the existing authoritative owners.</summary>
internal sealed class CircusGameLoopTests
{
    [TestCase(MatchMode.Circus)]
    [TestCase(MatchMode.FirstToTarget)]
    public void ConfiguredModeCompletesOnceAndNewGenerationsStartClean(MatchMode mode)
    {
        for (ulong generation = 1; generation <= 3; generation++)
        {
            var host = Create(generation, mode, 1);
            Assert.That(host.World.State.Match!.Players.All(row => row == new PlayerScore(row.Player, 0, 0, 0, 0)), Is.True);
            Assert.That(host.World.InitializeMatch(new(generation, 0, [1, 2])), Is.True);
            Hit(host, 10); // Countdown consumes damage but awards nothing.
            Assert.That(host.World.State.Match!.Lifecycle.Phase, Is.EqualTo(GameLoopPhase.Countdown));
            Assert.That(host.World.State.Match.Players.Sum(row => row.CircusScore), Is.Zero);
            Step(host);
            Assert.That(host.World.State.Match.Lifecycle.AllowsGameplay, Is.True);
            for (int i = 0; i < 60; i++) Step(host, moving: true);
            Assert.That(host.World.State.Match.Players[0].Stunts is not null, Is.EqualTo(mode == MatchMode.Circus));
            Hit(host, 1000);
            var final = host.World.State.Match;
            Assert.That(final!.Phase, Is.EqualTo(MatchPhase.Finished));
            Assert.That(final.Lifecycle.Outcome, Is.EqualTo(new MatchOutcome("kill-target", 1)));
            Assert.That(final.Players[0].CircusScore, Is.EqualTo(mode == MatchMode.Circus ? 190 : 0));
            Assert.That(final.Players[0].KillStreak, Is.EqualTo(mode == MatchMode.Circus ? 1 : 0));
            Assert.That(final.Players.All(row => row.Stunts is null), Is.True, "Completion discards outstanding stunts, without banking them.");
            for (int i = 0; i < 10; i++) Step(host, moving: true);
            Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["match.base_kill_points"] = 50 }, out _), Is.True);
            Assert.That(host.World.State.Match, Is.SameAs(final), "Tuning and continuing physics cannot replace the frozen completion boundary.");
            var recovered = HostVehicleSession.Restore(Checkpoint(host), host.CaptureAuthority(), 2);
            Step(recovered);
            Assert.That(recovered.World.State.Match!.Mode, Is.EqualTo(mode));
            Assert.That(recovered.World.State.Match.FinalResults!.Tick, Is.EqualTo(final.FinalResults!.Tick));
            Assert.That(recovered.World.State.Match.FinalResults.Standings, Is.EqualTo(final.FinalResults.Standings));
        }
    }

    [Test]
    public void AdmissionRebindAndReplacementContinuePendingAndBankedStateExactlyOnce()
    {
        var host = Create(20, MatchMode.Circus, 10);
        Step(host);
        Step(host);
        Hit(host, 1000);
        for (int i = 0; i < 3; i++) Step(host); // Respawn victim.
        for (int i = 0; i < 60; i++) Step(host, moving: true);
        var scores = host.World.State.Match!.Players.ToArray();
        Assert.That(scores[0].CircusScore, Is.EqualTo(200));
        Assert.That(scores[0].KillStreak, Is.EqualTo(1));
        Assert.That(scores.All(row => row.Stunts!.TopSpeed.Ticks == 60), Is.True);
        var preview = host.PrepareJoin(3, 1)!;
        Assert.That(preview.Match.Mode, Is.EqualTo(MatchMode.Circus));
        Assert.That(preview.Match.Players.Take(2), Is.EqualTo(scores));
        Assert.That(preview.Match.Players[2], Is.EqualTo(new PlayerScore(3, 0, 0, 0, 0)));
        host.Suspend(10);
        Assert.That(host.ResumePlayer(11, 2), Is.True);
        Assert.That(host.World.State.Match.Players, Is.EqualTo(scores));
        var replacement = HostVehicleSession.Restore(Checkpoint(host), host.CaptureAuthority(), 2);
        Assert.That(replacement.World.State.Match!.Players, Is.EqualTo(scores));
        Assert.That(replacement.World.State.Match.Awards, Is.Empty);
        Assert.That(replacement.ResumePlayer(40, 1), Is.True);
        Step(host);
        Step(replacement);
        Assert.That(replacement.World.State.Match!.Players, Is.EqualTo(host.World.State.Match!.Players));
        Assert.That(replacement.World.State.Match.Players[0].CircusScore, Is.EqualTo(205).Within(1e-9));
        Assert.That(replacement.World.State.Match.Players[1].CircusScore, Is.EqualTo(5).Within(1e-9));
        for (int i = 0; i < 30; i++) Step(replacement);
        Assert.That(replacement.World.State.Match.Players[0].CircusScore, Is.EqualTo(205).Within(1e-9));
        Assert.That(replacement.World.State.Match.Players.All(row => row.Stunts is null), Is.True);
    }

    [Test]
    public void ModeConfigurationIsValidatedReplicatedAndCannotChangeRunningRules()
    {
        var host = Create(30, MatchMode.Circus, 5);
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["match.mode"] = 0 }, out _), Is.True);
        Assert.That(host.World.State.Match!.Mode, Is.EqualTo(MatchMode.FirstToTarget));
        var decoded = GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(30, host.Configuration));
        Assert.That(decoded.State.Configuration.Match.Mode, Is.EqualTo(MatchMode.FirstToTarget));
        Assert.That(MatchCodec.Decode(MatchCodec.Encode(30, host.World.State.Match)).State.Mode, Is.EqualTo(MatchMode.FirstToTarget));
        Step(host);
        var boundary = host.World.State;
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["match.mode"] = 1, ["vehicle.mass"] = 500 }, out _), Is.False);
        Assert.That(host.World.State.Match, Is.SameAs(boundary.Match));
        Assert.That(host.World.State.Vehicles, Is.EqualTo(boundary.Vehicles));
        Assert.That(host.Configuration.Configuration.Vehicle.Mass, Is.EqualTo(new VehicleConfiguration().Mass));
        foreach (double invalid in new[] { -1d, 2d, 256d, 0.5, double.NaN })
            Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["match.mode"] = invalid }, out _), Is.False);
        Assert.Throws<ArgumentException>(() => new MatchState(1, 1, 5, MatchPhase.Active, null, null,
            [new PlayerScore(1, 0, 0, 0, 0) { CircusScore = 1 }], mode: MatchMode.FirstToTarget));
        var checkpoint = Checkpoint(host);
        Assert.Throws<ArgumentException>(() => new ResumeCheckpoint(checkpoint.Items, checkpoint.Match, null,
            new(host.Configuration.Revision, host.Configuration.Configuration with { Match = host.Configuration.Configuration.Match with { Mode = MatchMode.Circus } })));
    }

    private static HostVehicleSession Create(ulong generation, MatchMode mode, int target)
    {
        var host = new HostVehicleSession(generation, matchConfiguration: new() { Mode = mode, CountdownTicks = 1, KillTarget = target }, respawnConfiguration: new() { DelayTicks = 1 });
        host.JoinPlayer(10, 2);
        return host;
    }

    private static ResumeCheckpoint Checkpoint(HostVehicleSession host)
    {
        var state = host.World.State.Match!;
        var match = new MatchState(state.Tick, state.Revision, state.KillTarget, state.Phase, state.CountdownAtTick, state.Winner, state.Players, mode: state.Mode);
        return ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(new ItemPublication(1, host.Snapshot(), host.Items.Slots, host.Items.Missiles, []), match, null, host.Configuration)));
    }

    private static void Step(HostVehicleSession host, bool moving = false) => host.Step(default, vehicle => new(
        new VehiclePhysicsState(vehicle.ObservedPhysics.Position, Quaternion.Identity, moving ? new Vector3(0, 0, -27) : Vector3.Zero, Vector3.Zero), Vector3.UnitY));

    private static void Hit(HostVehicleSession host, float amount)
    {
        var frame = new InputFrame(host.World.State.Tick + 1, 0, 0, 0, 0, 0, 0);
        host.World.Step(frame, host.World.State.Vehicles.Select(vehicle => new VehicleStepRequest(vehicle.VehicleId, frame,
            new VehicleObservation(vehicle.ObservedPhysics, Vector3.UnitY), vehicle.VehicleId == 2
                ? [new VehicleEffectRequest(new DamageEffect(amount, Vector3.Zero, Vector3.Zero), new DamageContext("collision", 1, "mode integration"))] : [])).ToArray());
    }
}
