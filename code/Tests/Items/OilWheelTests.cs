using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class OilWheelTests
{
    private static readonly VehicleConfiguration Tuning = new();
    private static readonly VehiclePhysicsState Pose = new(new(0, 0.9f, 0), Quaternion.Identity, new(0, 0, -15), Vector3.Zero);

    [TestCase(WheelMask.FrontLeft, 0)]
    [TestCase(WheelMask.FrontRight, 1)]
    [TestCase(WheelMask.RearLeft, 2)]
    [TestCase(WheelMask.RearRight, 3)]
    public void OnlyTheSupportedTireTouchingTheFootprintIsContaminated(WheelMask mask, int index)
    {
        var observation = Observe(Pose);
        var point = observation.Wheels!.Value.Contacts!.Value[index]!.Value;
        var patch = new OilPatch(1, 2, point, Vector3.UnitY, 0.1f);
        Assert.That(patch.ContactWheels(observation, Tuning), Is.EqualTo(mask));
        var state = Drive(mask);
        for (int i = 0; i < 4; i++) { Assert.That(state.Oil[i], Is.EqualTo(i == index ? 120 : 0)); }
        Assert.That(patch.ContactWheels(new(Pose, Vector3.Zero, wheels: new(default, contacts: default(WheelContactPoints))), Tuning), Is.EqualTo(WheelMask.None));
    }

    [Test]
    public void BodyOverlapAndWrongRoadLevelCannotInventTireContact()
    {
        var centre = new OilPatch(1, 2, Vector3.Zero, Vector3.UnitY, 0.1f);
        Assert.That(centre.ContactWheels(Observe(Pose), Tuning), Is.EqualTo(WheelMask.None));
        var full = centre with { Radius = 3 };
        Assert.That(full.ContactWheels(new(Pose, Vector3.UnitY), Tuning), Is.EqualTo(WheelMask.None));
        var upper = new VehiclePhysicsState(Pose.Position + Vector3.UnitY * 4, Pose.Orientation, Pose.LinearVelocity, Vector3.Zero);
        Assert.That(full.ContactWheels(Observe(upper), Tuning), Is.EqualTo(WheelMask.None));
    }

    [Test]
    public void BankedUnevenSupportUsesActualContactLocations()
    {
        var normal = Vector3.Normalize(new Vector3(0.4f, 1, 0));
        var patch = new OilPatch(1, 2, new(1, 2, 3), normal, 0.2f);
        var points = new WheelContactPoints(patch.Position, patch.Position + normal * 0.2f, null, null);
        var observation = new VehicleObservation(Pose, normal, wheels: new(new Vector4(0.5f, 0.3f, 0, 0), contacts: points));
        Assert.That(patch.ContactWheels(observation, Tuning), Is.EqualTo(WheelMask.FrontLeft));
    }

    [TestCase(0.1f)]
    [TestCase(8f)]
    [TestCase(30f)]
    public void FrontAndRearImpairDifferentForcesWithoutSuppressingDriverInputs(float speed)
    {
        var pose = new VehiclePhysicsState(Pose.Position, Pose.Orientation, new(3, 0, -speed), Vector3.Zero);
        var dry = Drive(WheelMask.None, pose, steering: 22000);
        var front = Drive(WheelMask.FrontLeft | WheelMask.FrontRight, pose, steering: 22000);
        var rear = Drive(WheelMask.RearLeft | WheelMask.RearRight, pose, steering: 22000);
        Assert.That(front.SteeringAngle, Is.EqualTo(dry.SteeringAngle).And.Not.Zero);
        Assert.That(rear.Throttle, Is.EqualTo(dry.Throttle).And.GreaterThan(0));
        Assert.That(front.FrontSlip, Is.GreaterThan(dry.FrontSlip));
        Assert.That(front.RearSlip, Is.EqualTo(dry.RearSlip));
        Assert.That(rear.FrontSlip, Is.EqualTo(dry.FrontSlip));
        Assert.That(rear.RearSlip, Is.GreaterThan(dry.RearSlip));
        Assert.That(Drive(WheelMask.All, pose, steering: -22000).SteeringAngle, Is.LessThan(0), "Countersteering remains accepted.");
    }

    [Test]
    public void RearSplitGripCreatesPhysicalMirroredTorqueAndRetainsBraking()
    {
        var left = Drive(WheelMask.RearLeft);
        var right = Drive(WheelMask.RearRight);
        Assert.That(left.LongitudinalAcceleration, Is.LessThan(Drive(WheelMask.None).LongitudinalAcceleration));
        Assert.That(left.LongitudinalAcceleration, Is.EqualTo(right.LongitudinalAcceleration).Within(0.0001));
        Assert.That(left.Physics.AngularVelocity.Y, Is.Not.Zero);
        Assert.That(left.Physics.AngularVelocity.Y, Is.EqualTo(-right.Physics.AngularVelocity.Y).Within(0.0001));
        Assert.That(Drive(WheelMask.All).Physics.AngularVelocity.Y, Is.Zero, "Symmetric contact adds no scripted spin.");
        Assert.That(Drive(WheelMask.All, brake: ushort.MaxValue).LongitudinalAcceleration, Is.LessThan(0));
    }

    [Test]
    public void TwoSecondEffectIncludingSmoothRecoverySurvivesCodecAndAirborneContinuation()
    {
        var movement = new VehicleMovement(Tuning, Pose);
        var first = movement.Step(new(1, 0, 0, 0, 0, 0, 0), Pose, Vector3.UnitY, oilContact: WheelMask.FrontLeft);
        var replay = new VehicleMovement(Tuning, Pose);
        replay.Restore(VehicleStateCodec.Decode(VehicleStateCodec.Encode(first)));
        float previous = 1;
        for (ulong tick = 2; tick <= 121; tick++)
        {
            var input = new InputFrame(tick, 0, 0, 0, 0, 0, 0);
            Vector3 support = tick < 100 ? Vector3.Zero : Vector3.UnitY;
            var next = movement.Step(input, Pose, support);
            Assert.That(replay.Step(input, Pose, support), Is.EqualTo(next));
            float strength = next.Oil.Strength(0, Tuning);
            Assert.That(strength, Is.LessThanOrEqualTo(previous));
            Assert.That(previous - strength, Is.LessThan(0.08f), "No recovery step can snap grip.");
            if (tick <= 100) { Assert.That(strength, Is.EqualTo(1)); }
            previous = strength;
        }
        Assert.That(movement.State.Oil, Is.EqualTo(default(OilTractionState)));
    }

    [Test]
    public void RecoveryLongerThanConfiguredEffectCannotExtendTotalDuration()
    {
        var tuning = Tuning with { OilImpairmentSeconds = .1f, OilRecoverySeconds = 10 };
        var movement = new VehicleMovement(tuning, Pose);
        var state = movement.Step(new(1, 0, 0, 0, 0, 0, 0), Pose, Vector3.UnitY, oilContact: WheelMask.All);
        Assert.That(state.Oil.RemainingTicks, Is.EqualTo(6));
        for (ulong tick = 2; tick <= 7; tick++) { state = movement.Step(new(tick, 0, 0, 0, 0, 0, 0), Pose, Vector3.UnitY); }
        Assert.That(state.Oil.RemainingTicks, Is.Zero);
    }

    [Test]
    public void SharedSegmentsCountOnceWhileIndividualWheelEntriesRefreshIndependently()
    {
        var host = Create();
        var segments = new OilFootprint([
            new(new(-1, 0, -3), new(-1, 0, 3), Vector3.UnitY, 1.5f),
            new(new(1, 0, -3), new(1, 0, 3), Vector3.UnitY, 1.5f)]);
        var patch = new OilPatch(1, 1, Vector3.Zero, Vector3.UnitY, 0.1f) { AdditionalSegments = segments, ExpiresAtTick = host.World.State.Tick + 1800 };
        host.Items.Restore(new(1, host.Snapshot(), [], [], [], patches: [patch]), 1, 1);
        Step(host, WheelMask.FrontLeft);
        Assert.That(host.Items.Patches.Count, Is.EqualTo(1));
        Assert.That(Score(host), Is.EqualTo(50));
        for (int i = 0; i < 10; i++) { Step(host, WheelMask.FrontLeft); }
        Step(host, WheelMask.All);
        var oil = host.World.GetVehicle(2).Movement.Oil;
        Assert.That(oil.FrontLeft, Is.EqualTo(109));
        Assert.That(oil.FrontRight, Is.EqualTo(120));
        Assert.That(host.Items.Patches.Count, Is.EqualTo(1));
        Assert.That(Score(host), Is.EqualTo(50));
        Step(host, WheelMask.All & ~WheelMask.FrontLeft);
        Step(host, WheelMask.All);
        Assert.That(host.World.GetVehicle(2).Movement.Oil.FrontLeft, Is.EqualTo(120));
        Assert.That(host.Items.Patches.Count, Is.EqualTo(1));

        var restored = Restore(host);
        Assert.That(restored.Items.Patches, Is.EqualTo(host.Items.Patches));
        Assert.That(restored.World.GetVehicle(2).Movement.Oil, Is.EqualTo(host.World.GetVehicle(2).Movement.Oil));
        Step(restored, WheelMask.All);
        Assert.That(Score(restored), Is.EqualTo(50));
        Step(restored, WheelMask.None);
        Step(restored, WheelMask.All);
        Assert.That(Score(restored), Is.EqualTo(100));
        Assert.That(restored.Items.Patches.Single(), Is.EqualTo(patch), "Every piece survives repeated contacts unchanged.");
        Assert.That(restored.Items.OilContacts.Count, Is.EqualTo(1));
        Assert.That(restored.World.GetVehicle(2).Movement.Oil.RemainingTicks, Is.EqualTo(120));
    }

    [Test]
    public void DefaultDeadlineExpiresAtThirtySecondsWithoutRefreshingOnContactOrRestore()
    {
        var host = Create();
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.Oil), Is.True);
        var slot = host.Items.Slots.Single();
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.True);
        host.Step(default, v => Observe(new(new(6, 0.9f, 0), Quaternion.Identity, Vector3.Zero, Vector3.Zero)), placeOil: (s, p) => new(s.Token, s.Vehicle, Vector3.Zero, Vector3.UnitY, 3));
        ulong expires = host.Items.Patches.Single().ExpiresAtTick;
        Assert.That(expires - host.World.State.Tick, Is.EqualTo(1800));
        Step(host, WheelMask.All);
        host = Restore(host);
        while (host.World.State.Tick < expires - 1) { Step(host, WheelMask.All); }
        Assert.That(host.Items.Patches.Count, Is.EqualTo(1));
        Assert.That(host.Items.OilContacts.Count, Is.EqualTo(1), "Recovery does not unlatch an overlapping vehicle.");
        Assert.That(host.World.GetVehicle(2).Movement.OilTicks, Is.Zero);
        Step(host, WheelMask.All);
        Assert.That(host.Items.Patches, Is.Empty);
        Assert.That(host.Items.OilContacts, Is.Empty);
        Assert.That(Score(host), Is.EqualTo(50));
    }

    [Test]
    public void AllOilTuningRoundTripsAndRetunePreservesEachWheelsPhase()
    {
        var host = Create();
        var state = host.World.GetVehicle(2);
        var movement = new VehicleState(state.Movement.Tick, state.Movement.Physics, true, false, 0, 0, oil: new(120, 21, 0, 0));
        host.World.Restore(new(host.World.State.Tick, host.World.State.LastInput, host.World.State.Vehicles.Select(v => v.VehicleId == 2 ? new VehicleSnapshot(v.VehicleId, v.LifeId, movement, v.Damage, v.ObservedPhysics) : v), host.World.State.Match));
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> {
            ["vehicle.oil_grip_reduction"] = 0.9, ["vehicle.oil_rear_drive_reduction"] = 0.8,
            ["vehicle.oil_rear_lateral_reduction"] = 0.6, ["vehicle.oil_impairment_seconds"] = 10,
            ["vehicle.oil_recovery_seconds"] = 4, ["vehicle.oil_recovery_curve"] = 2,
            ["items.oil_stream_width"] = .3, ["items.oil_lifetime_seconds"] = 45 }, out _), Is.True);
        Assert.That(host.World.GetVehicle(2).Movement.Oil, Is.EqualTo(new OilTractionState(600, 240, 0, 0)));
        Assert.That(GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(99, host.Configuration)).State, Is.EqualTo(host.Configuration));
        Assert.Throws<ArgumentException>(() => new OilTractionState(-1, 0, 0, 0).Validate());
        Assert.Throws<ArgumentException>(() => new OilFootprint([new(Vector3.Zero, Vector3.UnitY, Vector3.UnitY, 1)]));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DeathOrNewLifeClearsEveryContaminatedTire(bool reset)
    {
        var world = new Core.Simulation.Simulation(new(60));
        world.AddVehicle(1, Tuning, new(), Pose);
        world.Step(new(1, 0, 0, 0, 0, 0, 0), [new(1, new(1, 0, 0, 0, 0, 0, 0), Observe(Pose), oilContact: WheelMask.All)]);
        Assert.That(world.GetVehicle(1).Movement.OilTicks, Is.EqualTo(120));
        world.Step(new(2, 0, 0, 0, 0, 0, 0), [new(1, new(2, 0, 0, 0, 0, 0, 0), Observe(Pose),
            reset ? [] : [new(new DamageEffect(10000, Vector3.Zero, Vector3.Zero), new DamageContext("test", 0, "death"))],
            reset: reset ? Pose : null, oilContact: WheelMask.All)]);
        Assert.That(world.GetVehicle(1).Movement.Oil, Is.EqualTo(default(OilTractionState)));
    }

    [Test]
    public void StationaryBrakeCannotBypassContaminatedLateralGrip()
    {
        var rules = Tuning with { TireFriction = 0.15f, HandbrakeHoldSpeed = 1 };
        var tilted = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.3f);
        var pose = new VehiclePhysicsState(Pose.Position, tilted, Vector3.Zero, Vector3.Zero);
        Vector3 support = Vector3.Transform(Vector3.UnitY, tilted);
        VehicleState Step(WheelMask mask) => new VehicleMovement(rules, pose).Step(new(1, 0, 0, 0, InputButtons.Drift, InputButtons.Drift, 0), pose, support, oilContact: mask);
        Assert.That(Step(WheelMask.All).Physics.LinearVelocity.Length(), Is.GreaterThan(Step(WheelMask.None).Physics.LinearVelocity.Length()));
    }

    private static VehicleState Drive(WheelMask mask, VehiclePhysicsState? pose = null, short steering = 0, ushort brake = 0)
    {
        var physics = pose ?? Pose;
        var movement = new VehicleMovement(Tuning, physics);
        movement.Restore(new(0, physics, true, false, 0, 0, throttle: 1));
        return movement.Step(new(1, steering, brake == 0 ? ushort.MaxValue : (ushort)0, brake, 0, 0, 0), physics, Vector3.UnitY, oilContact: mask);
    }

    private static VehicleObservation Observe(VehiclePhysicsState pose, WheelMask mask = WheelMask.All)
    {
        Vector3? Point(int index) => (mask & (WheelMask)(1 << index)) == 0 ? null : pose.Position + Vector3.Transform(new Vector3(
            (index % 2 == 0 ? -1 : 1) * VehicleDimensions.WheelTrack / 2, -0.9f,
            (index < 2 ? -1 : 1) * Tuning.Wheelbase / 2), pose.Orientation);
        var contacts = new WheelContactPoints(Point(0), Point(1), Point(2), Point(3));
        return new(pose, mask == WheelMask.None ? Vector3.Zero : Vector3.UnitY, wheels: new(new Vector4(0.745f), contacts: contacts));
    }

    private static HostVehicleSession Create()
    {
        var host = new HostVehicleSession(99, matchConfiguration: new() { MinimumPlayers = 2, CountdownTicks = 1, Mode = MatchMode.Circus });
        host.JoinPlayer(10, 2);
        Step(host, WheelMask.None); Step(host, WheelMask.None);
        return host;
    }

    private static void Step(HostVehicleSession host, WheelMask mask) => host.Step(default, v => Observe(
        new(v.VehicleId == 2 ? Pose.Position : new(8, 0.9f, 0), Quaternion.Identity, Vector3.Zero, Vector3.Zero), v.VehicleId == 2 ? mask : WheelMask.None));
    private static double Score(HostVehicleSession host) => host.World.State.Match!.Players.Single(p => p.Player == 1).CircusScore;
    private static HostVehicleSession Restore(HostVehicleSession host) => HostVehicleSession.Restore(ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(
        new ItemPublication(host.Items.Revision, host.Snapshot(), host.Items.Slots, [], [], patches: host.Items.Patches, oilContacts: host.Items.OilContacts),
        CurrentMatch(host), null, host.Configuration))), host.CaptureAuthority(), 2);
    private static MatchState CurrentMatch(HostVehicleSession host)
    {
        var m = host.World.State.Match!;
        return new(m.Tick, m.Revision, m.KillTarget, m.Phase, m.CountdownAtTick, m.Winner, m.Players, mode: m.Mode, activeStartedAtTick: m.ActiveStartedAtTick, durationTicks: m.DurationTicks, recoveryElapsedTicks: m.RecoveryElapsedTicks);
    }
}
