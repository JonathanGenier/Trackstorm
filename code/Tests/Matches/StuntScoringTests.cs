using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Matches;

/// <summary>Actual atomic simulation decisions with controlled fixed-step native observations.</summary>
internal sealed class StuntScoringTests
{
    [Test]
    public void DriftUsesPhysicalSlipEscalatesAndBanksOnlyOnSupportedCompletion()
    {
        var world = Create();
        for (int i = 0; i < 120; i++) { Step(world, new(0, 0, -20), buttons: InputButtons.Drift); }
        Assert.That(Score(world).Stunts, Is.Null, "A handbrake request without physical sliding is not a stunt.");
        for (int i = 0; i < 120; i++) { Step(world, new(8, 0, -20)); }
        Assert.That(Score(world).Stunts!.Drift.BasePoints, Is.EqualTo(10).Within(1e-9));
        for (int i = 0; i < 120; i++) { Step(world, new(8, 0, -20)); }
        Assert.That(Score(world).Stunts!.Drift.BasePoints, Is.EqualTo(30).Within(1e-9));
        Assert.That(Score(world).CircusScore, Is.Zero);
        Step(world, Vector3.Zero);
        Assert.That(Score(world).CircusScore, Is.EqualTo(30).Within(1e-9));
        Assert.That(world.State.Match!.Awards.Single().Player, Is.EqualTo(1));
        Assert.That(world.State.Match.Awards.Single().Category, Is.EqualTo(CircusScoreCategory.Drift));
        Assert.That(world.State.Match.Awards.Single().Points, Is.EqualTo(30).Within(1e-9));
        for (int i = 0; i < 60; i++) { Step(world, Vector3.Zero); }
        Assert.That(Score(world).CircusScore, Is.EqualTo(30).Within(1e-9));
        for (int i = 0; i < 60; i++) { Step(world, new(8, 0, -20)); }
        Assert.That(Score(world).Stunts!.Drift.BasePoints, Is.EqualTo(5).Within(1e-9), "New drift starts at the initial tier.");
        Step(world, Vector3.Zero);
        Assert.That(Score(world).CircusScore, Is.EqualTo(35).Within(1e-9));
    }

    [TestCase(10, 20)]
    [TestCase(30, 60)]
    public void AirEscalatesWhileJumpUsesDisplacementAndBothWaitForLanding(float distance, double jumpAward)
    {
        var world = Create();
        for (int i = 0; i < 240; i++) { Step(world, new(0, 0, -5), false, new(0, 5, -distance * (i + 1) / 240)); }
        Assert.That(Score(world).Stunts!.Airtime.BasePoints, Is.EqualTo(30).Within(1e-9));
        Assert.That(Score(world).Stunts!.LongJumpBasePoints, Is.EqualTo(jumpAward).Within(1e-6));
        Assert.That(Score(world).CircusScore, Is.Zero);
        Step(world, Vector3.Zero, position: new(0, 1, -distance));
        Assert.That(Score(world).CircusScore, Is.EqualTo(30 + jumpAward).Within(1e-6));
        Assert.That(world.State.Match!.Awards.Select(award => award.Category), Is.EqualTo(new[] { CircusScoreCategory.Airtime, CircusScoreCategory.LongJump }));
        Assert.That(Score(world).Stunts, Is.Null);
        Step(world, Vector3.Zero);
        Assert.That(Score(world).CircusScore, Is.EqualTo(30 + jumpAward).Within(1e-6));
    }

    [Test]
    public void ShortAndUnarmedFlightsCannotBankAndJumpDoesNotCountCircularPathLength()
    {
        var world = Create(active: false);
        for (int i = 0; i < 120; i++) { Step(world, new(0, 0, -5), false); }
        Assert.That(Score(world).Stunts, Is.Null);
        Step(world, Vector3.Zero);
        for (int i = 0; i < 14; i++) { Step(world, new(0, 0, -5), false, new(0, 5, -10)); }
        Step(world, Vector3.Zero);
        Assert.That(Score(world).CircusScore, Is.Zero);
        for (int i = 0; i < 60; i++) { Step(world, new(0, 0, -5), false, new(i < 30 ? 20 : 0, 5, 0)); }
        Step(world, Vector3.Zero);
        Assert.That(Score(world).CircusScore, Is.EqualTo(5).Within(1e-9), "Returning to takeoff scores airtime, zero distance.");
    }

    [Test]
    public void TopSpeedUsesFivePointsPerSecondAndHysteresisWithIndependentConcurrentFlight()
    {
        var world = Create();
        Step(world, new(0, 0, -26));
        Assert.That(Score(world).Stunts, Is.Null);
        for (int i = 0; i < 60; i++) { Step(world, new(0, 0, -27)); }
        for (int i = 0; i < 60; i++) { Step(world, new(0, 0, -26), false, new(0, 5, -10)); }
        Assert.That(Score(world).Stunts!.TopSpeed.BasePoints, Is.EqualTo(10).Within(1e-9));
        Assert.That(Score(world).CircusScore, Is.Zero);
        Step(world, new(0, 0, -25), false, new(0, 5, -10));
        Assert.That(Score(world).CircusScore, Is.EqualTo(10).Within(1e-9));
        Assert.That(world.State.Match!.Awards.Single().Player, Is.EqualTo(1));
        Assert.That(world.State.Match.Awards.Single().Category, Is.EqualTo(CircusScoreCategory.TopSpeed));
        Assert.That(world.State.Match.Awards.Single().Points, Is.EqualTo(10).Within(1e-9));
        Assert.That(Score(world).Stunts!.TopSpeed.Ticks, Is.Zero);
        Assert.That(Score(world).Stunts!.Airtime.Ticks, Is.EqualTo(61));
        Step(world, Vector3.Zero, position: new(0, 1, -10));
        Assert.That(Score(world).CircusScore, Is.EqualTo(10 + (61 * 5d / 60) + 20).Within(1e-9));
    }

    [Test]
    public void CompletingDriftDoesNotBankTopSpeedAndLossOfSupportCancelsOnlyDrift()
    {
        var world = Create();
        for (int i = 0; i < 60; i++) { Step(world, new(8, 0, -27)); }
        Step(world, new(0, 0, -27));
        Assert.That(Score(world).CircusScore, Is.EqualTo(5).Within(1e-9));
        Assert.That(Score(world).Stunts!.TopSpeed.Ticks, Is.EqualTo(61));
        for (int i = 0; i < 60; i++) { Step(world, new(8, 0, -27)); }
        Step(world, new(0, 0, -27), false);
        Assert.That(Score(world).CircusScore, Is.EqualTo(5).Within(1e-9));
        Assert.That(Score(world).Stunts!.Drift.Ticks, Is.Zero);
        Assert.That(Score(world).Stunts!.Airtime.Ticks, Is.EqualTo(1));
        Assert.That(Score(world).Stunts!.TopSpeed.Ticks, Is.EqualTo(122));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DeathOrResetAtCompletionDiscardsAllPendingAndKeepsBankedPoints(bool reset)
    {
        var world = Create();
        for (int i = 0; i < 60; i++) { Step(world, new(0, 0, -27)); }
        Step(world, Vector3.Zero);
        for (int i = 0; i < 60; i++) { Step(world, new(0, 0, -27), false, new(0, 5, -20)); }
        Assert.That(Score(world).PendingStuntScore, Is.GreaterThan(40));
        Step(world, Vector3.Zero, lethal: !reset, reset: reset);
        Assert.That(Score(world).Stunts, Is.Null);
        Assert.That(Score(world).CircusScore, Is.EqualTo(5).Within(1e-9));
        for (int i = 0; i < 60; i++) { Step(world, Vector3.Zero); }
        Assert.That(Score(world).CircusScore, Is.EqualTo(5).Within(1e-9));
    }

    [Test]
    public void AllCategoriesBankWithCurrentSharedFractionalMultiplier()
    {
        var world = Create();
        for (int i = 0; i < 60; i++) { Step(world, new(8, 0, -27)); }
        SetMultiplier(world);
        Step(world, new(0, 0, -27));
        Assert.That(Score(world).CircusScore, Is.EqualTo(12.5).Within(1e-9));
        for (int i = 0; i < 60; i++) { Step(world, new(0, 0, -27), false, new(0, 5, -10)); }
        Step(world, Vector3.Zero, position: new(0, 1, -10));
        Assert.That(Score(world).CircusScore, Is.EqualTo((5 + 5 + 20 + (121 * 5d / 60)) * 2.5).Within(1e-8));
    }

    [Test]
    public void CompleteRestoreContinuesPendingThenNeverReawardsCompletedEvents()
    {
        var world = Create();
        for (int i = 0; i < 150; i++) { Step(world, new(0, 0, -27), false, new(0, 5, -10)); }
        var restored = Create();
        restored.Restore(new SimulationState(world.State.Tick, world.State.LastInput, world.State.Vehicles,
            MatchCodec.Decode(MatchCodec.Encode(1, world.State.Match!)).State));
        for (int i = 0; i < 30; i++)
        {
            Step(world, new(0, 0, -27), false, new(0, 5, -15));
            Step(restored, new(0, 0, -27), false, new(0, 5, -15));
        }
        Step(world, Vector3.Zero, position: new(0, 1, -20));
        Step(restored, Vector3.Zero, position: new(0, 1, -20));
        Assert.That(MatchCodec.Encode(1, restored.State.Match!), Is.EqualTo(MatchCodec.Encode(1, world.State.Match!)));
        var completed = restored.State;
        restored.Restore(completed);
        for (int i = 0; i < 60; i++) { Step(restored, Vector3.Zero); }
        Assert.That(Score(restored).CircusScore, Is.EqualTo(Score(world).CircusScore));
        var invalidLife = completed.Match!.Players.Select(player => player.Player == 1 ? player with { Stunts = new StuntState { Life = 2, Tick = completed.Tick, TopSpeed = new(1, 1) } } : player);
        Assert.Throws<ArgumentException>(() => new SimulationState(completed.Tick, completed.LastInput, completed.Vehicles,
            new MatchState(completed.Tick, 1000, 20, MatchPhase.Active, null, null, invalidLife)));
    }

    [Test]
    public void TuningRoundTripsAndChangesRatesThresholdsAndDistanceAwards()
    {
        var edits = new Dictionary<string, double>
        {
            ["match.drift_rate"] = 7, ["match.drift_tier_step"] = 3, ["match.drift_tier_seconds"] = 0.5,
            ["match.airtime_rate"] = 9, ["match.airtime_tier_step"] = 4, ["match.airtime_tier_seconds"] = 0.5,
            ["match.stunt_maximum_tier"] = 1, ["match.jump_points_per_metre"] = 3,
            ["match.top_speed_enter_ratio"] = 0.8, ["match.top_speed_exit_ratio"] = 0.7,
            ["match.drift_minimum_speed"] = 6, ["match.drift_minimum_seconds"] = 0.5, ["match.airtime_minimum_seconds"] = 0.5,
        };
        Assert.That(GameplayOptions.TryApply(new(), edits, out var config, out _), Is.True);
        Assert.That(GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(1, new(1, config))).State.Configuration, Is.EqualTo(config));
        Assert.That(DeveloperSettingsFile.Read(DeveloperSettingsFile.Read("{\"schema\":2}").Write(config)).Configuration, Is.EqualTo(config));
        var world = Create(config.Match with { CountdownTicks = 1, KillTarget = 20 });
        for (int i = 0; i < 120; i++) { Step(world, new(8, 0, -20)); }
        Step(world, Vector3.Zero);
        Assert.That(Score(world).CircusScore, Is.EqualTo(18.5).Within(1e-9));
        for (int i = 0; i < 120; i++) { Step(world, new(0, 0, -23), false, new(0, 5, -10)); }
        Step(world, Vector3.Zero, position: new(0, 1, -10));
        Assert.That(Score(world).CircusScore, Is.EqualTo(18.5 + 24 + 30 + 10).Within(1e-9));
        Assert.That(GameplayOptions.TryApply(config, new Dictionary<string, double> { ["match.top_speed_exit_ratio"] = 0.9 }, out _, out _), Is.False);
        Assert.That(GameplayOptions.TryApply(config, new Dictionary<string, double> { ["match.airtime_tier_seconds"] = 0 }, out _, out _), Is.False);
    }

    [Test]
    public void SparseCodecFitsFullHistoryAndRejectsMalformedPendingMemory()
    {
        var pending = new StuntState { Life = 1, Tick = 100, Drift = new(30, 2), Airtime = new(30, 3), TopSpeed = new(30, 4), JumpOrigin = Vector3.One, JumpDistance = 10, LongJumpBasePoints = 20 };
        var rows = Enumerable.Range(1, 256).Select(id => new PlayerScore((ulong)id, 0, 0, 0, 0) { CircusScore = id <= 8 ? 6 : 0, Stunts = id <= 8 ? pending : null }).ToArray();
        var awards = Enumerable.Range(1, 8).SelectMany(player => Enum.GetValues<CircusScoreCategory>().Select(category => new CircusScoreAward((ulong)player, category, 1))).ToArray();
        var state = new MatchState(100, 1, 20, MatchPhase.Active, null, null, rows, awards: awards);
        var bytes = MatchCodec.Encode(1, state);
        Assert.That(bytes.Length, Is.LessThan(16384));
        Assert.That(MatchCodec.Decode(bytes).State.Players, Is.EqualTo(rows));
        Assert.That(MatchCodec.Decode(bytes).State.Awards, Is.EqualTo(awards));
        Assert.Throws<ArgumentException>(() => MatchCodec.Decode(bytes[..^1]));
        Assert.Throws<ArgumentException>(() => MatchCodec.Decode(bytes.Concat(new byte[] { 0 }).ToArray()));
        foreach (var invalid in new[] { pending with { Life = 0 }, pending with { Tick = 99 }, pending with { LongJumpBasePoints = double.NaN }, pending with { Drift = new(101, 2) } })
        {
            Assert.Throws<ArgumentException>(() => new MatchState(100, 1, 20, MatchPhase.Active, null, null, [rows[0] with { Stunts = invalid }]));
        }
        bytes[2] = 2;
        Assert.Throws<ArgumentException>(() => MatchCodec.Decode(bytes));
    }

    private static Core.Simulation.Simulation Create(MatchConfiguration? rules = null, bool active = true)
    {
        var world = new Core.Simulation.Simulation(new(60), match: rules ?? new MatchConfiguration { KillTarget = 20, CountdownTicks = 1 });
        world.AddVehicle(1, new(), new(), new(new(0, 1, 0), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        world.AddVehicle(2, new(), new(), new(new(100, 1, 0), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        if (active) { Step(world, Vector3.Zero); Step(world, Vector3.Zero); }
        return world;
    }

    private static PlayerScore Score(Core.Simulation.Simulation world) => world.State.Match!.Players.Single(player => player.Player == 1);

    private static void SetMultiplier(Core.Simulation.Simulation world)
    {
        var state = world.State;
        var rows = state.Match!.Players.Select(player => player.Player == 1 ? player with { Kills = 5, Deaths = 1, ProcessedLife = 1 } : player with { Deaths = 4, ProcessedLife = 4 });
        world.Restore(new SimulationState(state.Tick, state.LastInput, state.Vehicles, new MatchState(state.Tick, state.Match.Revision + 1, 20, MatchPhase.Active, null, null, rows)));
    }

    private static void Step(Core.Simulation.Simulation world, Vector3 velocity, bool grounded = true, Vector3? position = null, InputButtons buttons = 0, bool lethal = false, bool reset = false)
    {
        var frame = new InputFrame(world.State.Tick + 1, 0, 0, 0, buttons, 0, 0);
        world.Step(frame, world.State.Vehicles.Select(vehicle => new VehicleStepRequest(vehicle.VehicleId, frame,
            vehicle.VehicleId == 1 ? new VehicleObservation(new(position ?? new(0, 1, 0), Quaternion.Identity, velocity, Vector3.Zero), grounded ? Vector3.UnitY : Vector3.Zero)
                : new VehicleObservation(vehicle.ObservedPhysics, Vector3.UnitY),
            effects: vehicle.VehicleId == 1 && lethal ? [new(new DamageEffect(1000, Vector3.Zero, Vector3.Zero), new DamageContext("explosion", 0, "stunt test"))] : null,
            reset: vehicle.VehicleId == 1 && reset ? new VehiclePhysicsState(new(0, 1, 0), Quaternion.Identity, Vector3.Zero, Vector3.Zero) : null)).ToArray());
    }
}
