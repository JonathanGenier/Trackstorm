using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

/// <summary>Boost ownership, duration, scoring and complete authority continuation.</summary>
[TestFixture]
internal sealed class NitroTests
{
    [Test]
    public void RepeatedUseExpiresExactlyAndCannotSpendReplacementOrStack()
    {
        var host = Create(3);
        for (int cycle = 0; cycle < 5; cycle++)
        {
            var slot = Activate(host);
            Assert.That(host.World.GetVehicle(1).Movement.Nitro.RemainingTicks, Is.EqualTo(3));
            Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.False);
            Assert.That(host.Items.Grant(host.World, 1, HeldItem.Nitro), Is.True);
            var replacement = host.Items.Slots.Single();
            Assert.That(host.UseItem(0, 99, replacement.Life, replacement.Token), Is.True);
            Step(host);
            Assert.That(host.Items.Slots.Single(), Is.EqualTo(replacement));
            Assert.That(host.World.GetVehicle(1).Movement.Nitro.RemainingTicks, Is.EqualTo(2));
            Step(host);
            Step(host);
            Assert.That(host.World.GetVehicle(1).Movement.Nitro, Is.EqualTo(default(NitroState)));
            Assert.That(host.World.Events.Entries.Count(e => e.Kind == "Effect ended" && e.Cause == "Nitro"), Is.EqualTo(cycle + 1));
        }
        Assert.That(host.World.State.Match!.Players[0].CircusScore, Is.EqualTo(2.5).Within(1e-10));
    }

    [Test]
    public void NeutralStationaryDurationUsesCurrentFractionalKdAndLiveRate()
    {
        var host = Create(4);
        SetScore(host, 3, 1);
        Activate(host);
        Assert.That(host.World.State.Match!.Players[0].CircusScore, Is.EqualTo(0.25).Within(1e-10));
        SetScore(host, 5, 1);
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["match.nitro_points_per_second"] = 12 }, out _), Is.True);
        Step(host);
        Assert.That(host.World.State.Match!.Players[0].CircusScore, Is.EqualTo(0.75).Within(1e-10));
        Assert.That(host.World.State.Match.Awards.Single().Category, Is.EqualTo(CircusScoreCategory.Nitro));
        Assert.That(host.World.GetVehicle(1).Speed, Is.Zero);
    }

    [Test]
    public void ResumeAndAuthorityReplacementContinueWithoutRestartingOrRescoring()
    {
        var host = Create(12);
        host.JoinPlayer(10, 2);
        Activate(host);
        Step(host);
        var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(
            new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], []),
            CurrentMatch(host), null, host.Configuration)));
        var restored = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 2);
        Assert.That(restored.World.GetVehicle(1).Movement.Nitro, Is.EqualTo(host.World.GetVehicle(1).Movement.Nitro));
        Assert.That(restored.World.State.Match!.Players[0].CircusScore, Is.EqualTo(host.World.State.Match!.Players[0].CircusScore));
        Assert.That(restored.Items.Events, Is.Empty);
        Assert.That(restored.World.State.Match.Awards, Is.Empty);
        Assert.That(restored.ResumePlayer(20, 1), Is.True);
        var spent = host.Items.Slots.Single();
        Assert.That(restored.UseItem(20, 99, spent.Life, spent.Token), Is.False);
        for (int i = 0; i < 13; i++) { Step(host); Step(restored); }
        Assert.That(restored.World.GetVehicle(1).Movement.Nitro.Active, Is.False);
        Assert.That(restored.World.State.Match.Players[0].CircusScore, Is.EqualTo(host.World.State.Match.Players[0].CircusScore).Within(1e-10));
        Assert.That(restored.World.State.Match.Players[0].CircusScore, Is.EqualTo(2).Within(1e-10));
    }

    [Test]
    public void PredictionAndCodecContinueBoostAndRejectMalformedState()
    {
        var host = Create(6);
        Activate(host);
        var vehicle = host.Snapshot().Vehicles.Single();
        var prediction = new PredictedVehicle(vehicle, host.Configuration.Configuration);
        prediction.Predict(default, Observe);
        Assert.That(prediction.State.Movement.Nitro.RemainingTicks, Is.EqualTo(5));
        var bytes = VehicleStateCodec.Encode(vehicle.State.Movement);
        Assert.That(VehicleStateCodec.Decode(bytes), Is.EqualTo(vehicle.State.Movement));
        bytes[109] = 0; bytes[110] = 0;
        Assert.Throws<ArgumentException>(() => VehicleStateCodec.Decode(bytes));
        Assert.Throws<ArgumentException>(() => new NitroState(1, float.NaN, 1.4f).Validate());
        Assert.Throws<ArgumentException>(() => new ItemConfiguration { NitroDurationTicks = 0 }.Validate());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ResetAndDeathClearBoostAndStopScoring(bool death)
    {
        var host = Create(60);
        Activate(host);
        double score = host.World.State.Match!.Players[0].CircusScore;
        var v = host.World.GetVehicle(1);
        var frame = new InputFrame(host.World.State.Tick + 1, 0, 0, 0, 0, 0, 0);
        host.Items.Step(host.World, frame, [new VehicleStepRequest(1, frame, Observe(v),
            death ? [new VehicleEffectRequest(new DamageEffect(10000, Vector3.Zero, Vector3.Zero), new DamageContext("world", 0, "test"))] : [],
            reset: death ? null : v.ObservedPhysics)], (_, _) => null);
        Assert.That(host.World.GetVehicle(1).Movement.Nitro.Active, Is.False);
        Assert.That(host.World.State.Match.Players[0].CircusScore, Is.EqualTo(score));
    }

    [Test]
    public void FinishedClearsSameTickAndFreshMatchStartsEmpty()
    {
        var host = Create(60, killTarget: 1);
        host.JoinPlayer(10, 2);
        Activate(host);
        var frame = new InputFrame(host.World.State.Tick + 1, 0, 0, 0, 0, 0, 0);
        host.Items.Step(host.World, frame, host.World.State.Vehicles.Select(v => new VehicleStepRequest(v.VehicleId, frame, Observe(v),
            v.VehicleId == 2 ? [new VehicleEffectRequest(new DamageEffect(10000, Vector3.Zero, Vector3.Zero), new DamageContext("missile", 1, "test"))] : [])).ToArray(), (_, _) => null);
        Assert.That(host.World.State.Match!.Phase, Is.EqualTo(MatchPhase.Finished));
        Assert.That(host.World.GetVehicle(1).Movement.Nitro.Active, Is.False);
        double score = host.World.State.Match.Players[0].CircusScore;
        Step(host);
        Assert.That(host.World.State.Match.Players[0].CircusScore, Is.EqualTo(score));
        Assert.That(Create(60).World.GetVehicle(1).Movement.Nitro.Active, Is.False);
    }

    [Test]
    public void FailedWorldBatchDoesNotConsumeActivation()
    {
        var host = Create(60);
        host.Items.Grant(host.World, 1, HeldItem.Nitro);
        var slot = host.Items.Slots.Single();
        host.UseItem(0, 99, slot.Life, slot.Token);
        var wrong = new InputFrame(host.World.State.Tick + 2, 0, 0, 0, 0, 0, 0);
        Assert.Throws<ArgumentException>(() => host.Items.Step(host.World, wrong, [new VehicleStepRequest(1, wrong, Observe(host.World.GetVehicle(1)))], (_, _) => null));
        Assert.That(host.Items.Slots.Single(), Is.EqualTo(slot));
        Assert.That(host.World.GetVehicle(1).Movement.Nitro.Active, Is.False);
        Step(host);
        Assert.That(host.World.GetVehicle(1).Movement.Nitro.Active, Is.True);
    }

    [Test]
    public void BoostImprovesAccelerationAndCapWithoutChangingConfiguration()
    {
        var config = new VehicleConfiguration();
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(0, 0, -config.ForwardSpeed), Vector3.Zero);
        var normal = new VehicleMovement(config, pose);
        var boosted = new VehicleMovement(config, pose);
        var drive = new InputFrame(1, 0, ushort.MaxValue, 0, 0, 0, 0);
        var a = normal.Step(drive, pose, Vector3.UnitY);
        var b = boosted.Step(drive, pose, Vector3.UnitY, nitro: new NitroState(60, 2, 1.4f));
        Assert.That(-b.Physics.LinearVelocity.Z, Is.GreaterThan(-a.Physics.LinearVelocity.Z + 0.05));
        Assert.That(boosted.Configuration, Is.SameAs(config));
        Assert.That(config.ForwardSpeed, Is.EqualTo(new VehicleConfiguration().ForwardSpeed));
        var rest = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        a = new VehicleMovement(config, rest).Step(drive, rest, Vector3.UnitY);
        b = new VehicleMovement(config, rest).Step(drive, rest, Vector3.UnitY, nitro: new NitroState(60, 2, 1.4f));
        Assert.That(b.LongitudinalAcceleration, Is.GreaterThan(a.LongitudinalAcceleration * 1.1f));
    }

    [Test]
    public void FirstToTargetBoostsWithoutCircusPoints()
    {
        var host = Create(3, mode: MatchMode.FirstToTarget);
        Activate(host);
        Assert.That(host.World.GetVehicle(1).Movement.Nitro.Active, Is.True);
        for (int i = 0; i < 4; i++) { Step(host); }
        Assert.That(host.World.State.Match!.Players[0].CircusScore, Is.Zero);
    }

    private static HostVehicleSession Create(int duration, int killTarget = 100, MatchMode mode = MatchMode.Circus)
    {
        var host = new HostVehicleSession(99, new ItemConfiguration { NitroDurationTicks = duration },
            matchConfiguration: new MatchConfiguration { MinimumPlayers = 1, CountdownTicks = 1, KillTarget = killTarget, Mode = mode });
        Step(host); Step(host);
        return host;
    }

    private static ItemSlot Activate(HostVehicleSession host)
    {
        if (!host.Items.Slots.Any(s => s.Item == HeldItem.Nitro)) { Assert.That(host.Items.Grant(host.World, 1, HeldItem.Nitro), Is.True); }
        var slot = host.Items.Slots.Single();
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.True);
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.False);
        Step(host);
        return slot;
    }

    private static void SetScore(HostVehicleSession host, int kills, int deaths)
    {
        if (host.World.State.Vehicles.Count == 1) { host.JoinPlayer(10, 2); }
        var w = host.World.State;
        var m = w.Match!;
        var next = new MatchState(m.Tick, m.Revision + 1, m.KillTarget, m.Phase, m.CountdownAtTick, m.Winner,
            m.Players.Select(p => p.Player == 1 ? p with { Kills = kills, Deaths = deaths, ProcessedLife = 1 } : p with { Deaths = kills, ProcessedLife = 1 }), mode: m.Mode);
        host.World.Restore(new(w.Tick, w.LastInput, w.Vehicles, next));
    }

    private static MatchState CurrentMatch(HostVehicleSession host)
    {
        var m = host.World.State.Match!;
        return new(m.Tick, m.Revision, m.KillTarget, m.Phase, m.CountdownAtTick, m.Winner, m.Players, mode: m.Mode);
    }

    private static void Step(HostVehicleSession host) => host.Step(default, Observe);
    private static VehicleObservation Observe(VehicleSnapshot state) => new(new VehiclePhysicsState(state.ObservedPhysics.Position, Quaternion.Identity, Vector3.Zero, Vector3.Zero), Vector3.UnitY);
}
