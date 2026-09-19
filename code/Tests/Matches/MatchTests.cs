using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Matches;

/// <summary>Scoring is exercised through the real damage/lifecycle atomic simulation boundary.</summary>
[TestFixture]
internal sealed class MatchTests
{
    /// <summary>Valid sources credit one kill, one death, and one delta despite repeated dead observations.</summary>
    /// <param name="source">Authoritative damage category.</param>
    [TestCase("missile")]
    [TestCase("collision")]
    public void ValidDeathScoresOnceAndSurvivesRestore(string source)
    {
        var world = Create();
        Active(world);
        Step(world, 2, 1, source);
        MatchState scored = world.State.Match!;
        Assert.That(scored.Players.Single(player => player.Player == 1).Kills, Is.EqualTo(1));
        Assert.That(scored.Players.Single(player => player.Player == 2).Deaths, Is.EqualTo(1));
        Assert.That(scored.Changes, Is.EqualTo(new[] { new ScoredDeath(2, 1, 1) }));
        var restored = Create();
        restored.Restore(new SimulationState(world.State.Tick, world.State.LastInput, world.State.Vehicles, MatchCodec.Decode(MatchCodec.Encode(99, scored)).State));
        for (int index = 0; index < 10; index++)
        {
            Step(world, 2, 1, source);
            Step(restored, 2, 1, source);
        }

        Assert.That(world.State.Match, Is.SameAs(scored));
        Assert.That(MatchCodec.Encode(99, restored.State.Match!), Is.EqualTo(MatchCodec.Encode(99, scored)));
    }

    /// <summary>Self, world, unknown players and unsupported sources never inherit another attacker's credit.</summary>
    /// <param name="killer">Final instigator.</param>
    /// <param name="source">Final source category.</param>
    [TestCase(2ul, "missile")]
    [TestCase(0ul, "collision")]
    [TestCase(99ul, "missile")]
    [TestCase(1ul, "environment")]
    public void InvalidAttributionDoesNotFallBackToPreviousAttacker(ulong killer, string source)
    {
        var world = Create();
        Active(world);
        Step(world, 2, 1, "missile", 10);
        Step(world, 2, killer, source);
        Assert.That(world.State.Match!.Players.Sum(player => player.Kills), Is.Zero);
        Assert.That(world.State.Match.Players.Single(player => player.Player == 2).Deaths, Is.EqualTo(1));
        Assert.That(world.State.Match.Changes.Single().Killer, Is.Zero);
    }

    /// <summary>Both pre-active phases consume dead lives without granting delayed points when Active starts.</summary>
    /// <param name="countdown">Whether to enter Countdown before death.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void PreActiveDeathsNeverScoreLater(bool countdown)
    {
        var world = Create(new MatchConfiguration { CountdownTicks = 3 });
        if (countdown)
        {
            Step(world);
        }

        Step(world, 2, 1);
        for (int index = 0; index < 8; index++)
        {
            Step(world);
        }

        Assert.That(world.State.Match!.Phase, Is.EqualTo(MatchPhase.Active));
        Assert.That(world.State.Match.Players.Sum(player => player.Kills + player.Deaths), Is.Zero);
        Assert.That(world.State.Match.Players.Single(player => player.Player == 2).ProcessedLife, Is.EqualTo(1));
    }

    /// <summary>Waiting/countdown use Core ticks and cancel when the required participant leaves.</summary>
    [Test]
    public void CountdownUsesFixedTicksAndParticipantThreshold()
    {
        var world = Create();
        world.LeaveVehicle(2);
        Step(world);
        Assert.That(world.State.Match!.Phase, Is.EqualTo(MatchPhase.Waiting));
        world.JoinVehicle(3, new(), new(), Pose(3));
        Step(world);
        Assert.That(world.State.Match!.Phase, Is.EqualTo(MatchPhase.Countdown));
        Assert.That(world.State.Match.CountdownAtTick, Is.EqualTo(world.State.Tick + 1));
        world.LeaveVehicle(3);
        Step(world);
        Assert.That(world.State.Match.Phase, Is.EqualTo(MatchPhase.Waiting));
    }

    /// <summary>Default and custom targets finish once and remain unchanged through new lives, combat and late joins.</summary>
    /// <param name="target">Winning threshold.</param>
    [TestCase(5)]
    [TestCase(2)]
    public void FirstToTargetFreezesOneWinner(int target)
    {
        Assert.That(new MatchConfiguration().KillTarget, Is.EqualTo(5));
        var world = Create(new MatchConfiguration { KillTarget = target, CountdownTicks = 1 });
        Active(world);
        for (int kill = 1; kill <= target; kill++)
        {
            Step(world, 2, 1);
            Assert.That(world.State.Match!.Phase, Is.EqualTo(kill == target ? MatchPhase.Finished : MatchPhase.Active));
            if (kill != target)
            {
                Step(world, reset: 2);
            }
        }

        MatchState final = world.State.Match!;
        Assert.That(final.Winner, Is.EqualTo(1));
        Assert.That(final.Lifecycle.Phase, Is.EqualTo(GameLoopPhase.Finished));
        Assert.That(final.Lifecycle.AllowsGameplay, Is.False);
        Assert.That(final.Lifecycle.Outcome, Is.EqualTo(new MatchOutcome("kill-target", 1)));
        var decoded = MatchCodec.Decode(MatchCodec.Encode(99, final)).State;
        Assert.That(decoded.Lifecycle.Outcome, Is.EqualTo(final.Lifecycle.Outcome));
        Assert.That(decoded.Lifecycle.Phase, Is.EqualTo(final.Lifecycle.Phase));
        Assert.That(final.Players.Single(player => player.Player == 1).Wins, Is.EqualTo(1));
        Assert.That(final.Players.Single(player => player.Player == 2).Deaths, Is.EqualTo(target));
        Step(world, reset: 2);
        Step(world, 1, 2);
        world.LeaveVehicle(1);
        world.JoinVehicle(3, new(), new(), Pose(3));
        Step(world, 3, 2);
        Assert.That(world.State.Match, Is.SameAs(final));
    }

    /// <summary>Reversed request ordering cannot change which same-tick terminal death wins.</summary>
    [Test]
    public void SimultaneousDeathsHaveOneStableWinner()
    {
        var world = Create(new MatchConfiguration { KillTarget = 1, CountdownTicks = 1 });
        Active(world);
        ulong tick = world.State.Tick + 1;
        var requests = world.State.Vehicles.Reverse().Select(vehicle => new VehicleStepRequest(vehicle.VehicleId, Frame(tick), new VehicleObservation(vehicle.ObservedPhysics, Vector3.UnitY), [new VehicleEffectRequest(new DamageEffect(100, Vector3.Zero, Vector3.Zero), new DamageContext("missile", vehicle.VehicleId == 1 ? 2ul : 1ul, "simultaneous"))])).ToArray();
        world.Step(Frame(tick), requests);
        Assert.That(world.State.Match!.Winner, Is.EqualTo(2));
        Assert.That(world.State.Match.Changes.Count, Is.EqualTo(1));
        Assert.That(world.State.Match.Players.Sum(player => player.Wins), Is.EqualTo(1));
        Assert.That(world.State.Match.Players.Sum(player => player.Deaths), Is.EqualTo(1));
        Assert.That(world.State.Vehicles.All(vehicle => vehicle.Damage.Destroyed), Is.True);
    }

    /// <summary>Invalid world batches cannot publish score changes or consume a death.</summary>
    [Test]
    public void RejectedBatchAndIncompleteRestoreAreAtomic()
    {
        var world = Create();
        Active(world);
        SimulationState before = world.State;
        Assert.Throws<ArgumentException>(() => world.Step(Frame(before.Tick + 1), []));
        Assert.Throws<ArgumentException>(() => world.Restore(new SimulationState(before.Tick, before.LastInput, before.Vehicles)));
        Assert.That(world.State, Is.EqualTo(before));
        Step(world, 2, 1);
        Assert.That(world.State.Match!.Changes.Count, Is.EqualTo(1));
    }

    /// <summary>Configuration and reliable serialization reject malformed state without losing duplicate protections.</summary>
    [Test]
    public void CodecAndConfigurationValidateBoundaries()
    {
        foreach (int target in new[] { 0, -1, 1000001 })
        {
            Assert.Throws<ArgumentException>(() => new MatchConfiguration { KillTarget = target }.Validate());
        }

        Assert.Throws<ArgumentException>(() => new MatchConfiguration { CountdownTicks = 0 }.Validate());
        var world = Create();
        Active(world);
        Step(world, 2, 1);
        byte[] bytes = MatchCodec.Encode(99, world.State.Match!);
        for (int size = 0; size < bytes.Length; size++)
        {
            Assert.Throws<ArgumentException>(() => MatchCodec.Decode(bytes.AsSpan(0, size)));
        }

        Assert.Throws<ArgumentException>(() => MatchCodec.Decode([.. bytes, 0]));
        Assert.Throws<ArgumentException>(() => new MatchState(1, 1, 5, MatchPhase.Finished, null, 1, [new PlayerScore(1, 0, 0, 1, 0)]));
        Assert.Throws<ArgumentException>(() => new MatchState(1, 1, 5, MatchPhase.Active, null, null, [new PlayerScore(1, 0, 0, 0, 0), new PlayerScore(1, 0, 0, 0, 0)]));
        Assert.That(MatchCodec.Encode(99, MatchCodec.Decode(bytes).State), Is.EqualTo(bytes));
    }

    private static Core.Simulation.Simulation Create(MatchConfiguration? configuration = null)
    {
        var world = new Core.Simulation.Simulation(new SimulationConfiguration(60), match: configuration ?? new MatchConfiguration { CountdownTicks = 1 });
        world.AddVehicle(1, new(), new(), Pose(1));
        world.AddVehicle(2, new(), new(), Pose(2));
        return world;
    }

    private static VehiclePhysicsState Pose(ulong id) => new(new Vector3(id * 10, 1, 0), Quaternion.Identity, Vector3.Zero, Vector3.Zero);
    private static InputFrame Frame(ulong tick) => new(tick, 0, 0, 0, 0, 0, 0);
    private static void Active(Core.Simulation.Simulation world)
    {
        Step(world);
        Step(world);
        Assert.That(world.State.Match!.Phase, Is.EqualTo(MatchPhase.Active));
    }

    private static void Step(Core.Simulation.Simulation world, ulong victim = 0, ulong killer = 0, string source = "missile", float damage = 100, ulong reset = 0)
    {
        ulong tick = world.State.Tick + 1;
        world.Step(Frame(tick), world.State.Vehicles.Select(vehicle => new VehicleStepRequest(vehicle.VehicleId, Frame(tick), new VehicleObservation(vehicle.ObservedPhysics, Vector3.UnitY), vehicle.VehicleId == victim ? [new VehicleEffectRequest(new DamageEffect(damage, Vector3.Zero, Vector3.Zero), new DamageContext(source, killer, "test"))] : [], reset: vehicle.VehicleId == reset ? Pose(reset) : null)).ToArray());
    }
}
