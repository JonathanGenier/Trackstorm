using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Matches;

[TestFixture]
internal sealed class ItemDamageScoringTests
{
    [TestCase("missile")]
    [TestCase("proxy-mine")]
    [TestCase("salvo")]
    [TestCase("machine-gun")]
    public void AppliedLossUsesFractionalCurrentKdAndSeparateKillAward(string source)
    {
        var world = Create();
        var state = world.State;
        world.Restore(new(state.Tick, state.LastInput, state.Vehicles, new MatchState(state.Tick, 3, 20, MatchPhase.Active, null, null,
            state.Match!.Players.Select(p => p.Player == 1 ? p with { Kills = 5, Deaths = 1, ProcessedLife = 1 } : p.Player == 3 ? p with { Deaths = 4, ProcessedLife = 1 } : p))));
        Hit(world, source, 12.5f);
        Assert.That(Score(world), Is.EqualTo(12.5 * 0.5 * 2.5));
        Assert.That(world.State.Match!.Awards, Is.EqualTo(new[] { new CircusScoreAward(1, CircusScoreCategory.ItemDamage, 15.625) }));
        Hit(world, source, 1000);
        Assert.That(world.State.Match!.Awards, Is.EqualTo(new[]
        {
            new CircusScoreAward(1, CircusScoreCategory.ItemDamage, 87.5 * 0.5 * 2.5),
            new CircusScoreAward(1, CircusScoreCategory.Kill, 100 * 3),
        }));
        Assert.That(Score(world), Is.EqualTo(425));
        Hit(world, source, 1000);
        Assert.That(Score(world), Is.EqualTo(425));
    }

    [TestCase("missile")]
    [TestCase("proxy-mine")]
    [TestCase("salvo")]
    [TestCase("machine-gun")]
    public void InvalidAttributionAndZeroAppliedLossNeverScore(string source)
    {
        var world = Create();
        Hit(world, source, 0);
        Hit(world, source, float.Epsilon);

        foreach (ulong owner in new ulong[] { 0, 2, 99 }) { Hit(world, source, 5, owner); }
        Hit(world, "unknown-item", 5);
        Assert.That(Score(world), Is.Zero);
        Assert.That(world.State.Match!.Awards, Is.Empty);
    }

    [Test]
    public void MultipleTargetsAndRepeatedHitsConsumeEveryDistinctApplication()
    {
        var world = Create();
        Step(world, v => v.VehicleId == 1 ? [] : [Effect("salvo", 10), Effect("salvo", 15), Effect("missile", 7)]);
        Assert.That(Score(world), Is.EqualTo(32)); // two rivals, 32 HP each, rate .5
        Assert.That(world.State.Match!.Awards, Is.EqualTo(new[] { new CircusScoreAward(1, CircusScoreCategory.ItemDamage, 32) }));
        Assert.That(world.State.Match.Players.Skip(1).All(p => p.ProcessedDamageSequence == 3), Is.True);
        Step(world, v => v.VehicleId == 1 ? [] : [Effect("salvo", 10)]);
        Assert.That(Score(world), Is.EqualTo(42));
        var restored = Create();
        restored.Restore(new(world.State.Tick, world.State.LastInput, world.State.Vehicles, MatchCodec.Decode(MatchCodec.Encode(1, world.State.Match!)).State));
        Step(restored);
        Assert.That(Score(restored), Is.EqualTo(42), "Restoration emits no historical scoring.");
        Hit(restored, "salvo", 8);
        Assert.That(Score(restored), Is.EqualTo(46));
    }

    [TestCase("missile")]
    [TestCase("proxy-mine")]
    [TestCase("salvo")]
    public void DuplicateIdentityIsIgnoredAndNewLifeSequenceOneScores(string source)
    {
        var world = Create();
        var before = world.State;
        Hit(world, source, 10);
        var scored = world.State.Match!;
        var vehicles = before.Vehicles.Select(v => new VehicleSnapshot(v.VehicleId, v.LifeId,
            new VehicleState(world.State.Tick, v.ObservedPhysics, false, false, 0, 0), v.Damage, v.ObservedPhysics));
        world.Restore(new(world.State.Tick, world.State.LastInput, vehicles, scored));
        Hit(world, source, 10);
        Assert.That(Score(world), Is.EqualTo(5));
        Hit(world, source, 10);
        Assert.That(Score(world), Is.EqualTo(10));
        Step(world, reset: 2);
        Hit(world, source, 10);
        Assert.That(Score(world), Is.EqualTo(15));
    }

    [Test]
    public void RetainedMineOwnerCanScoreAfterVehicleDeparture()
    {
        var world = Create();
        world.LeaveVehicle(1);
        Hit(world, "proxy-mine", 10);
        Assert.That(Score(world), Is.EqualTo(5));
    }

    [Test]
    public void PhaseModeAndZeroRatePreventAwards()
    {
        var inactive = Create(active: false);
        Hit(inactive, "salvo", 10);
        Step(inactive);
        Step(inactive);
        Assert.That(Score(inactive), Is.Zero);
        Hit(inactive, "salvo", 10);
        Assert.That(Score(inactive), Is.EqualTo(5));
        var disabled = Create(rate: 0);
        Hit(disabled, "missile", 1000);
        Assert.That(Score(disabled), Is.EqualTo(100), "Zero damage rate preserves the separate kill award.");
        var firstToTarget = Create(mode: MatchMode.FirstToTarget);
        Hit(firstToTarget, "missile", 1000);
        Assert.That(Score(firstToTarget), Is.Zero);
        Assert.That(firstToTarget.State.Match!.Players[0].Kills, Is.EqualTo(1));
        var terminal = Create(target: 1, duration: 1);
        Hit(terminal, "missile", 1000);
        double final = Score(terminal);
        Step(terminal, v => v.VehicleId == 3 ? [Effect("salvo", 10)] : []);
        Assert.That(Score(terminal), Is.EqualTo(final));
    }

    private static Core.Simulation.Simulation Create(bool active = true, double rate = 0.5, MatchMode mode = MatchMode.Circus, int target = 20, ulong duration = 36000)
    {
        var world = new Core.Simulation.Simulation(new(60), match: new MatchConfiguration { CountdownTicks = 1, KillTarget = target, ItemPointsPerDamage = rate, Mode = mode, DurationTicks = duration });
        for (ulong id = 1; id <= 3; id++) { world.AddVehicle(id, new(), new(), new(new Vector3(id * 10, 1, 0), Quaternion.Identity, Vector3.Zero, Vector3.Zero)); }
        if (active) { Step(world); Step(world); }
        return world;
    }
    private static double Score(Core.Simulation.Simulation world) => world.State.Match!.Players.Single(p => p.Player == 1).CircusScore;
    private static VehicleEffectRequest Effect(string source, float amount, ulong owner = 1) => new(new DamageEffect(amount, Vector3.Zero, Vector3.Zero), new DamageContext(source, owner, "shared application"));
    private static void Hit(Core.Simulation.Simulation world, string source, float amount, ulong owner = 1) => Step(world, v => v.VehicleId == 2 ? [Effect(source, amount, owner)] : []);
    private static void Step(Core.Simulation.Simulation world, Func<VehicleSnapshot, VehicleEffectRequest[]>? effects = null, ulong reset = 0)
    {
        var input = new InputFrame(world.State.Tick + 1, 0, 0, 0, 0, 0, 0);
        world.Step(input, world.State.Vehicles.Select(v => new VehicleStepRequest(v.VehicleId, input,
            new(v.ObservedPhysics, Vector3.UnitY), effects?.Invoke(v), reset: v.VehicleId == reset ? v.ObservedPhysics : null)).ToArray());
    }
}
