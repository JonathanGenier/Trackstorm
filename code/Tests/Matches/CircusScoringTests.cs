using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Matches;

/// <summary>Exercises Circus through applied health outcomes and complete restoration boundaries.</summary>
[TestFixture]
internal sealed class CircusScoringTests
{
    /// <summary>Division is fractional and has the required floor.</summary>
    [TestCase(5, 0, 5d)]
    [TestCase(5, 1, 2.5d)]
    [TestCase(0, 0, 1d)]
    [TestCase(1, 5, 1d)]
    public void MultiplierUsesAuthoritativeTotals(int kills, int deaths, double expected)
    {
        var score = new PlayerScore(1, kills, deaths, 0, (ulong)deaths);
        Assert.That(score.KdMultiplier, Is.EqualTo(expected));
        Assert.That(CircusScoring.Bank(score, 7).CircusScore, Is.EqualTo(7 * expected));
    }

    /// <summary>Kill points use the newly credited kill; death resets only the streak.</summary>
    [Test]
    public void KillProgressionAndDeathPreserveBankedPoints()
    {
        var world = Create();
        Hit(world, 2, 1, "missile", 100);
        Assert.That(Score(world, 1), Is.EqualTo(new PlayerScore(1, 1, 0, 0, 0) { CircusScore = 100, KillStreak = 1 }));
        Reset(world, 2);
        Hit(world, 2, 1, "missile", 100);
        Assert.That(Score(world, 1).CircusScore, Is.EqualTo(350));
        Assert.That(Score(world, 1).KillStreak, Is.EqualTo(2));
        Hit(world, 1, 0, "environment", 100);
        Assert.That(Score(world, 1).CircusScore, Is.EqualTo(350));
        Assert.That(Score(world, 1).KillStreak, Is.Zero);
        Assert.That(Score(world, 1).KdMultiplier, Is.EqualTo(1));
        Reset(world, 1);
        Reset(world, 2);
        Hit(world, 2, 1, "missile", 100);
        Assert.That(Score(world, 1).CircusScore, Is.EqualTo(500));
        Assert.That(Score(world, 1).KillStreak, Is.EqualTo(1));
    }

    /// <summary>Nonlethal applied HP, not requested damage or only lethal state, banks points.</summary>
    [Test]
    public void CollisionUsesActualLossAndCurrentMultiplierBeforeLethalKill()
    {
        var world = Create(new MatchConfiguration { KillTarget = 20, CountdownTicks = 1, CollisionPointsPerDamage = 2 });
        Hit(world, 2, 1, "missile", 100);
        Reset(world, 2);
        Hit(world, 2, 1, "missile", 100);
        Reset(world, 2);
        Hit(world, 2, 1, "collision", 10);
        Assert.That(Score(world, 1).CircusScore, Is.EqualTo(390));
        Assert.That(Score(world, 1).Kills, Is.EqualTo(2));
        Assert.That(world.State.Match!.Awards, Is.EqualTo(new[] { new CircusScoreAward(1, CircusScoreCategory.Collision, 40) }));
        Hit(world, 2, 1, "collision", 1000);
        // Remaining 90 HP * 2 conversion * x2, followed by (100 + 50) * x3.
        Assert.That(Score(world, 1).CircusScore, Is.EqualTo(1200));
        Assert.That(world.State.Match!.Awards, Is.EqualTo(new[]
        {
            new CircusScoreAward(1, CircusScoreCategory.Collision, 360),
            new CircusScoreAward(1, CircusScoreCategory.Kill, 450),
        }));
        Hit(world, 2, 1, "collision", 1000);
        Assert.That(Score(world, 1).CircusScore, Is.EqualTo(1200));
    }

    /// <summary>All applied hits survive intervening sources in the same candidate batch.</summary>
    [Test]
    public void MultipleDamageOutcomesAreConsumedBeforeLastDamageIsOverwritten()
    {
        var world = Create();
        Step(world, vehicle => vehicle.VehicleId == 2 ? [Effect(10, "collision", 1), Effect(5, "missile", 1), Effect(7, "collision", 1)] : []);
        Assert.That(world.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(78));
        Assert.That(Score(world, 1).CircusScore, Is.EqualTo(17));
        Assert.That(Score(world, 2).ProcessedDamageSequence, Is.EqualTo(3));
    }

    /// <summary>Contact manifolds, cooldown repeats and restoration do not re-award an applied hit.</summary>
    [Test]
    public void RepeatedContactsAndRestoredDamageCannotDoubleScore()
    {
        var world = Create();
        var contact = new VehicleContact(new Vector3(-10, 0, 0), Vector3.UnitX, 0, 1);
        Contact(world, [contact, contact, contact]);
        Assert.That(Score(world, 1).CircusScore, Is.EqualTo(18));
        var restored = Create();
        restored.Restore(new SimulationState(world.State.Tick, world.State.LastInput, world.State.Vehicles, MatchCodec.Decode(MatchCodec.Encode(1, world.State.Match!)).State));
        for (int i = 0; i < 11; i++)
        {
            Contact(world, [contact]);
            Contact(restored, [contact]);
            Assert.That(Score(restored, 1).CircusScore, Is.EqualTo(18));
        }

        Contact(world, [contact]);
        Contact(restored, [contact]);
        Assert.That(Score(world, 1).CircusScore, Is.EqualTo(36));
        Assert.That(MatchCodec.Encode(1, restored.State.Match!), Is.EqualTo(MatchCodec.Encode(1, world.State.Match!)));
        Reset(restored, 2);
        Contact(restored, [contact]);
        Assert.That(Score(restored, 1).CircusScore, Is.EqualTo(54), "A new life's sequence one is a new application.");
    }

    /// <summary>Only valid opponent collision damage scores; zero contacts and non-collision hits do not.</summary>
    [TestCase(0ul, "collision")]
    [TestCase(2ul, "collision")]
    [TestCase(99ul, "collision")]
    [TestCase(1ul, "missile")]
    public void InvalidOrOtherNonlethalSourcesDoNotBank(ulong attacker, string source)
    {
        var world = Create();
        Hit(world, 2, attacker, source, 10);
        Contact(world, [new VehicleContact(new Vector3(-4, 0, 0), Vector3.UnitX, 0, 1)]);
        Assert.That(world.State.Match!.Players.Sum(player => player.CircusScore), Is.Zero);
    }

    /// <summary>Inactive phases consume outcomes but never bank delayed points.</summary>
    [Test]
    public void CountdownDamageIsNotReplayedOnActivation()
    {
        var world = Create(active: false);
        Hit(world, 2, 1, "collision", 10);
        Step(world);
        Step(world);
        Assert.That(Score(world, 1).CircusScore, Is.Zero);
        Assert.That(Score(world, 2).ProcessedDamageSequence, Is.EqualTo(1));
        Hit(world, 2, 1, "collision", 10);
        Assert.That(Score(world, 1).CircusScore, Is.EqualTo(10));
    }

    /// <summary>A previously consumed outcome identity cannot be scored again, even if an observation is repeated.</summary>
    [Test]
    public void ConsumedDamageWatermarkRejectsRepeatedApplicationIdentity()
    {
        var world = Create();
        var before = world.State;
        Hit(world, 2, 1, "collision", 10);
        var scored = world.State.Match!;
        // Deliberately restore the earlier health memory at the new tick while keeping the
        // committed match watermark, then recreate sequence one. The score must not replay.
        var vehicles = before.Vehicles.Select(vehicle => new VehicleSnapshot(vehicle.VehicleId, vehicle.LifeId,
            new VehicleState(world.State.Tick, vehicle.ObservedPhysics, false, false, 0, 0), vehicle.Damage, vehicle.ObservedPhysics));
        world.Restore(new SimulationState(world.State.Tick, world.State.LastInput, vehicles, scored));
        Hit(world, 2, 1, "collision", 10);
        Assert.That(world.State.Match, Is.SameAs(scored));
        Hit(world, 2, 1, "collision", 10);
        Assert.That(Score(world, 1).CircusScore, Is.EqualTo(20), "The next distinct application still scores.");
    }

    /// <summary>Victim ordering controls same-tick death/reset and kill awards regardless of request order.</summary>
    [Test]
    public void MutualLethalCollisionsUseStableOrderAndFreezeAtTarget()
    {
        foreach (int target in new[] { 1, 20 })
        {
            var world = Create(new MatchConfiguration { KillTarget = target, CountdownTicks = 1 });
            var reversed = Create(new MatchConfiguration { KillTarget = target, CountdownTicks = 1 });
            foreach (var simulation in new[] { world, reversed })
            {
                var frame = new InputFrame(simulation.State.Tick + 1, 0, 0, 0, 0, 0, 0);
                var requests = simulation.State.Vehicles.Select(vehicle => new VehicleStepRequest(vehicle.VehicleId, frame,
                    new VehicleObservation(vehicle.ObservedPhysics, Vector3.UnitY), [Effect(100, "collision", vehicle.VehicleId == 1 ? 2ul : 1ul)])).ToArray();
                simulation.Step(frame, simulation == reversed ? requests.Reverse().ToArray() : requests);
            }

            Assert.That(MatchCodec.Encode(1, reversed.State.Match!), Is.EqualTo(MatchCodec.Encode(1, world.State.Match!)));
            Assert.That(Score(world, 2).CircusScore, Is.EqualTo(200));
            Assert.That(Score(world, 1).CircusScore, Is.EqualTo(target == 1 ? 0 : 200));
            Assert.That(Score(world, 2).KillStreak, Is.EqualTo(target == 1 ? 1 : 0));
            if (target == 1)
            {
                var frozen = world.State.Match;
                Reset(world, 1);
                Hit(world, 1, 2, "collision", 10);
                Assert.That(world.State.Match, Is.SameAs(frozen));
            }
        }
    }

    /// <summary>Score and tuning reject invalid values and round-trip the full bounded roster.</summary>
    [Test]
    public void WireAndTuningPreserveFractionalValuesAndValidateBounds()
    {
        var configuration = new GameplayConfiguration { Match = new() { BaseKillPoints = 123.5, KillStreakBonusStep = 7.25, CollisionPointsPerDamage = 0.5 } };
        Assert.That(GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(1, new(2, configuration))).State.Configuration, Is.EqualTo(configuration));
        var oldFile = DeveloperSettingsFile.Read("{\"schema\":2}\n{\"key\":\"match.kill_target\",\"value\":20}");
        Assert.That(oldFile.Configuration.Match.BaseKillPoints, Is.EqualTo(100));
        Assert.That(oldFile.Configuration.Match.KillStreakBonusStep, Is.EqualTo(25));
        Assert.That(oldFile.Configuration.Match.CollisionPointsPerDamage, Is.EqualTo(1));
        Assert.That(DeveloperSettingsFile.Read(oldFile.Write(configuration)).Configuration, Is.EqualTo(configuration));
        foreach (double value in new[] { -1d, double.NaN, double.PositiveInfinity, 1000001d })
        {
            Assert.Throws<ArgumentException>(() => new MatchConfiguration { BaseKillPoints = value }.Validate());
            Assert.Throws<ArgumentException>(() => new MatchConfiguration { KillStreakBonusStep = value }.Validate());
            Assert.Throws<ArgumentException>(() => new MatchConfiguration { CollisionPointsPerDamage = value }.Validate());
        }

        var scores = Enumerable.Range(1, 256).Select(id => new PlayerScore((ulong)id, 2, 1, 0, 1) { CircusScore = 125.125, KillStreak = 1, ProcessedDamageLife = 2, ProcessedDamageSequence = 3 });
        var state = new MatchState(5, 8, 20, MatchPhase.Active, null, null, scores.Select(score => score with { Deaths = 2 }));
        byte[] bytes = MatchCodec.Encode(1, state);
        Assert.That(bytes.Length, Is.LessThan(16384));
        Assert.That(MatchCodec.Decode(bytes).State.Players, Is.EqualTo(state.Players));
        var awarded = new MatchState(5, 9, 20, MatchPhase.Active, null, null, state.Players, awards:
        [
            new CircusScoreAward(1, CircusScoreCategory.Collision, 12.5),
            new CircusScoreAward(1, CircusScoreCategory.Kill, 100),
        ]);
        Assert.That(MatchCodec.Decode(MatchCodec.Encode(1, awarded)).State.Awards, Is.EqualTo(awarded.Awards));
        foreach (double value in new[] { -1d, double.NaN, double.PositiveInfinity })
        {
            Assert.Throws<ArgumentException>(() => new MatchState(0, 1, 20, MatchPhase.Active, null, null, [new PlayerScore(1, 0, 0, 0, 0) { CircusScore = value }]));
        }

        bytes[2] = 1;
        Assert.Throws<ArgumentException>(() => MatchCodec.Decode(bytes));
    }

    private static Core.Simulation.Simulation Create(MatchConfiguration? rules = null, bool active = true)
    {
        var world = new Core.Simulation.Simulation(new(60), match: rules ?? new MatchConfiguration { KillTarget = 20, CountdownTicks = 1 });
        world.AddVehicle(1, new(), new(), Pose(1));
        world.AddVehicle(2, new(), new(), Pose(2));
        if (active) { Step(world); Step(world); }
        return world;
    }

    private static PlayerScore Score(Core.Simulation.Simulation world, ulong id) => world.State.Match!.Players.Single(score => score.Player == id);
    private static VehiclePhysicsState Pose(ulong id) => new(new Vector3(id * 10, 1, 0), Quaternion.Identity, Vector3.Zero, Vector3.Zero);
    private static VehicleEffectRequest Effect(float amount, string source, ulong attacker) => new(new DamageEffect(amount, Vector3.Zero, Vector3.Zero), new DamageContext(source, attacker, "Circus check"));
    private static void Hit(Core.Simulation.Simulation world, ulong victim, ulong attacker, string source, float amount) => Step(world, vehicle => vehicle.VehicleId == victim ? [Effect(amount, source, attacker)] : []);
    private static void Reset(Core.Simulation.Simulation world, ulong id) => Step(world, reset: id);
    private static void Contact(Core.Simulation.Simulation world, VehicleContact[] contacts) => Step(world, contacts: contacts);
    private static void Step(Core.Simulation.Simulation world, Func<VehicleSnapshot, VehicleEffectRequest[]>? effects = null, ulong reset = 0, VehicleContact[]? contacts = null)
    {
        ulong tick = world.State.Tick + 1;
        var frame = new InputFrame(tick, 0, 0, 0, 0, 0, 0);
        world.Step(frame, world.State.Vehicles.Select(vehicle => new VehicleStepRequest(vehicle.VehicleId, frame,
            new VehicleObservation(vehicle.ObservedPhysics, Vector3.UnitY, vehicle.VehicleId == 2 ? contacts : null), effects?.Invoke(vehicle),
            reset: vehicle.VehicleId == reset ? Pose(reset) : null)).ToArray());
    }
}
