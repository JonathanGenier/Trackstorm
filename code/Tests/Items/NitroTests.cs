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
    public void PartialReleaseRepeatedUseAndDepletionPreserveExactSlot()
    {
        var host = Create(10);
        var grant = Activate(host);
        Assert.That(host.Items.Slots.Single().NitroCharge, Is.EqualTo(90));
        for (int i = 0; i < 3; i++) { Step(host, held: true); }
        Assert.That(host.Items.Slots.Single().NitroCharge, Is.EqualTo(60));
        Step(host);
        Assert.That(host.World.GetVehicle(1).Movement.Nitro.Active, Is.False);
        Assert.That(host.Items.Slots.Single().NitroCharge, Is.EqualTo(60));
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.Wrench), Is.True);
        for (int i = 0; i < 6; i++)
        {
            Assert.That(host.UseItem(0, 99, grant.Life, grant.Token), Is.True);
            Step(host, held: true);
            Step(host);
        }
        var slot = host.Items.Slots.Single();
        Assert.That(slot.Item, Is.EqualTo(HeldItem.None));
        Assert.That(slot.NitroCharge, Is.Zero);
        Assert.That(slot.SecondItem, Is.EqualTo(HeldItem.Wrench));
        Assert.That(host.UseItem(0, 99, grant.Life, grant.Token), Is.False);
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.Nitro), Is.True);
        Assert.That(host.Items.Slots.Single().NitroCharge, Is.EqualTo(100));
        Assert.That(host.UseItem(0, 99, grant.Life, grant.Token), Is.False);
    }

    [Test]
    public void StationaryBoostDoesNotScoreButActualOverspeedUsesCurrentKdAfterRelease()
    {
        var host = Create(60);
        SetScore(host, 3, 1);
        Activate(host);
        Assert.That(host.World.State.Match!.Players[0].CircusScore, Is.Zero);
        Step(host, held: true, speed: 50);
        Assert.That(host.World.State.Match.Players[0].CircusScore, Is.EqualTo(0.25).Within(1e-10));
        SetScore(host, 5, 1);
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["match.nitro_points_per_second"] = 12 }, out _), Is.True);
        Step(host, speed: 50);
        Assert.That(host.World.GetVehicle(1).Movement.Nitro.Active, Is.False);
        Assert.That(host.World.State.Match.Players[0].CircusScore, Is.EqualTo(0.75).Within(1e-10));
        Assert.That(host.World.State.Match.Awards.Single(a => a.Player == 1).Category, Is.EqualTo(CircusScoreCategory.Nitro));
        Step(host, speed: host.Configuration.Configuration.Vehicle.ForwardSpeed);
        Assert.That(host.World.State.Match.Players[0].CircusScore, Is.EqualTo(0.75).Within(1e-10));
    }

    [Test]
    public void ResumeAndAuthorityReplacementRetainPartialChargeWithoutReplayingAwards()
    {
        var host = Create(12);
        host.JoinPlayer(10, 2);
        Activate(host);
        Step(host, held: true, speed: 50);
        var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(
            new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], []),
            CurrentMatch(host), null, host.Configuration)));
        var restored = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 2);
        Assert.That(restored.Items.Slots, Is.EqualTo(host.Items.Slots));
        Assert.That(restored.World.GetVehicle(1).Movement.Nitro, Is.EqualTo(host.World.GetVehicle(1).Movement.Nitro));
        Assert.That(restored.Items.Events, Is.Empty);
        Assert.That(restored.World.State.Match!.Awards, Is.Empty);
        double charge = restored.Items.Slots.Single().NitroCharge;
        Step(restored);
        Assert.That(restored.Items.Slots.Single().NitroCharge, Is.EqualTo(charge));
        Assert.That(restored.World.GetVehicle(1).Movement.Nitro.Active, Is.False);
        Assert.That(restored.ResumePlayer(20, 1), Is.True);
        var retained = restored.Items.Slots.Single();
        Assert.That(restored.UseItem(20, 99, retained.Life, retained.Token), Is.True);
        Assert.That(restored.Receive(20, 99, [new SequencedInput(1, new InputFrame(0, 0, 0, 0, InputButtons.UseItem, InputButtons.UseItem, 0))], retained.Life), Is.True);
        Step(restored);
        Assert.That(restored.Items.Slots.Single().NitroCharge, Is.LessThan(charge));
    }

    [Test]
    public void PredictionReleaseStopsBoostAndCodecsRejectMalformedCharge()
    {
        var host = Create(6);
        Activate(host);
        var vehicle = host.Snapshot().Vehicles.Single();
        var prediction = new PredictedVehicle(vehicle, host.Configuration.Configuration);
        prediction.Predict(new InputFrame(0, 0, 0, 0, InputButtons.UseItem, 0, 0), Observe);
        Assert.That(prediction.State.Movement.Nitro.RemainingTicks, Is.EqualTo(5));
        prediction.Predict(default, Observe);
        Assert.That(prediction.State.Movement.Nitro.Active, Is.False);
        var bytes = VehicleStateCodec.Encode(vehicle.State.Movement);
        Assert.That(VehicleStateCodec.Decode(bytes), Is.EqualTo(vehicle.State.Movement));
        bytes[109] = 0; bytes[110] = 0;
        Assert.Throws<ArgumentException>(() => VehicleStateCodec.Decode(bytes));
        foreach (double invalid in new[] { double.NaN, -1, 0, 100.1 })
        {
            Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), [host.Items.Slots.Single() with { NitroCharge = invalid }], [], []));
        }
        Assert.Throws<ArgumentException>(() => new ItemConfiguration { NitroConsumptionPerSecond = 0 }.Validate());
    }

    [Test]
    public void SwitchingStopsDrainAndTwoNitroChargesRemainIndependent()
    {
        var host = Create(10);
        Activate(host);
        host.Items.Grant(host.World, 1, HeldItem.Nitro);
        Assert.That(host.SwitchItem(0, 99, 1, 1), Is.True);
        Step(host, held: true);
        Assert.That(host.World.GetVehicle(1).Movement.Nitro.Active, Is.False);
        var slot = host.Items.Slots.Single();
        Assert.That(slot.NitroCharge, Is.EqualTo(90));
        Assert.That(slot.SecondNitroCharge, Is.EqualTo(100));
        Assert.That(host.UseItem(0, 99, 1, slot.SecondToken), Is.True);
        Step(host, held: true);
        Assert.That(host.Items.Slots.Single().NitroCharge, Is.EqualTo(90));
        Assert.That(host.Items.Slots.Single().SecondNitroCharge, Is.EqualTo(90));
        var decoded = ItemCodec.DecodeState(ItemCodec.EncodeState(new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], [])));
        Assert.That(decoded.Slots, Is.EqualTo(host.Items.Slots));
    }

    [Test]
    public void ReliableUseCanPrecedeHeldInputButReleaseCancelsWaitingIntent()
    {
        var host = Create(10);
        host.Items.Grant(host.World, 1, HeldItem.Nitro);
        var slot = host.Items.Slots.Single();
        host.UseItem(0, 99, 1, slot.Token);
        Step(host);
        Assert.That(host.Items.Slots.Single().NitroCharge, Is.EqualTo(100));
        Step(host, held: true);
        Assert.That(host.Items.Slots.Single().NitroCharge, Is.EqualTo(90));
        Step(host);
        host.UseItem(0, 99, 1, slot.Token);
        host.Step(new InputFrame(0, 0, 0, 0, 0, 0, InputButtons.UseItem), Observe);
        Step(host, held: true);
        Assert.That(host.Items.Slots.Single().NitroCharge, Is.EqualTo(90));
    }

    [Test]
    public void EarlyReliableRepressWaitsForItsSequencedInputBoundary()
    {
        var host = Create(60);
        host.JoinPlayer(10, 2);
        host.Items.Grant(host.World, 2, HeldItem.Nitro);
        var slot = host.Items.Slots.Single();
        host.UseItem(10, 99, slot.Life, slot.Token, 1);
        host.Receive(10, 99, [new SequencedInput(1, new InputFrame(0, 0, 0, 0, InputButtons.UseItem, InputButtons.UseItem, 0))]);
        Step(host);
        double charge = host.Items.Slots.Single().NitroCharge;
        Assert.That(host.UseItem(10, 99, slot.Life, slot.Token, 3), Is.True);
        host.Receive(10, 99, [new SequencedInput(2, new InputFrame(0, 0, 0, 0, 0, 0, InputButtons.UseItem))]);
        Step(host);
        Assert.That(host.Items.Slots.Single().NitroCharge, Is.EqualTo(charge));
        host.Receive(10, 99, [new SequencedInput(3, new InputFrame(0, 0, 0, 0, InputButtons.UseItem, InputButtons.UseItem, 0))]);
        Step(host);
        Assert.That(host.Items.Slots.Single().NitroCharge, Is.LessThan(charge));
        Assert.That(host.World.GetVehicle(2).Movement.Nitro.Active, Is.True);
        host.Suspend(10);
        charge = host.Items.Slots.Single().NitroCharge;
        Step(host);
        Assert.That(host.Items.Slots.Single().NitroCharge, Is.EqualTo(charge));
        Assert.That(host.World.GetVehicle(2).Movement.Nitro.Active, Is.False);
    }

    [Test]
    public void LiveConsumptionEditsPreservePercentageAndChangeSubsequentDrain()
    {
        var host = Create(10);
        Activate(host);
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["items.nitro_consumption_per_second"] = 60 }, out _), Is.True);
        Step(host, held: true);
        Assert.That(host.Items.Slots.Single().NitroCharge, Is.EqualTo(89));
    }

    [Test]
    public void OverspeedRecoveryIsBoundedAndReturnsToNormalUnderThrottle()
    {
        var c = new VehicleConfiguration();
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -55), Vector3.Zero);
        var movement = new VehicleMovement(c, pose);
        movement.Restore(new VehicleState(0, pose, true, false, 0, 0, nitro: new NitroState(60, 2, 1.4f)));
        float previous = 55;
        for (ulong tick = 1; tick <= 300; tick++)
        {
            var state = movement.Step(new InputFrame(tick, 0, ushort.MaxValue, 0, 0, 0, 0), pose, Vector3.UnitY);
            float speed = -state.Physics.LinearVelocity.Z;
            Assert.That(previous - speed, Is.InRange(-0.0001f, c.OverspeedDeceleration / 60 + 0.0001f));
            pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -speed), Vector3.Zero);
            previous = speed;
        }
        Assert.That(previous, Is.EqualTo(c.ForwardSpeed).Within(0.001));
    }

    [Test]
    public void InactiveOverspeedRecoverySurvivesMovementCodecAndContinuation()
    {
        var config = new VehicleConfiguration();
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -55), Vector3.Zero);
        var original = new VehicleMovement(config, pose);
        original.Restore(new VehicleState(0, pose, true, false, 0, 0, nitro: NitroState.Recovery));
        var restored = new VehicleMovement(config, pose);
        restored.Restore(VehicleStateCodec.Decode(VehicleStateCodec.Encode(original.State)));
        var input = new InputFrame(1, 0, ushort.MaxValue, 0, 0, 0, 0);
        Assert.That(restored.Step(input, pose, Vector3.UnitY), Is.EqualTo(original.Step(input, pose, Vector3.UnitY)));
        Assert.That(restored.State.Nitro.Active, Is.False);
        Assert.That(restored.State.Nitro.Recovering, Is.True);
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
        Step(host, held: true);
        Assert.That(host.World.GetVehicle(1).Movement.Nitro.Active, Is.True);
    }

    [Test]
    public void BoostImprovesAccelerationAndCapWithoutChangingConfiguration()
    {
        var config = new VehicleConfiguration();
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(0, 0, -config.ForwardSpeed), Vector3.Zero);
        var normal = new VehicleMovement(config, pose);
        var boosted = new VehicleMovement(config, pose);
        var drive = new InputFrame(1, 0, ushort.MaxValue, 0, InputButtons.UseItem, 0, 0);
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
        var host = new HostVehicleSession(99, new ItemConfiguration { NitroConsumptionPerSecond = 6000.0 / duration },
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
        Step(host, held: true);
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

    private static void Step(HostVehicleSession host, bool held = false, float speed = 0) => host.Step(
        new InputFrame(0, 0, 0, 0, held ? InputButtons.UseItem : 0, 0, 0),
        state => new VehicleObservation(new VehiclePhysicsState(state.ObservedPhysics.Position, Quaternion.Identity, new(0, 0, -speed), Vector3.Zero), Vector3.UnitY));
    private static VehicleObservation Observe(VehicleSnapshot state) => new(new VehiclePhysicsState(state.ObservedPhysics.Position, Quaternion.Identity, Vector3.Zero, Vector3.Zero), Vector3.UnitY);
}
