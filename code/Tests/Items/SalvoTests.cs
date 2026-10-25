using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class SalvoTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void OneActivationCommitsExactlyFiveLaunchesWithoutGroundOrFurtherInput(bool held)
    {
        var host = Ready();
        ulong start = host.World.State.Tick, token = host.Items.TokenHighWater;
        Use(host);
        var launches = new List<(ulong Tick, ulong Token)>();
        for (int i = 0; i < 500; i++)
        {
            host.Step(new(0, 0, 0, 0, held ? InputButtons.UseItem : 0, 0, 0), Observe);
            launches.AddRange(host.Items.Events.Where(e => !e.Impact).Select(e => (host.World.State.Tick - start, e.Token)));
            Assert.That(host.Items.RequestUse(host.World, 1, 1, token), Is.False);
            Assert.That(host.Items.Missiles.Count + host.Items.Slots.Sum(s => s.ReservedSalvoShots), Is.LessThanOrEqualTo(ItemAuthority.MaximumProjectiles));
        }
        Assert.That(launches.Select(x => x.Tick), Is.EqualTo(new ulong[] { 6, 81, 156, 231, 306 }));
        Assert.That(launches.Select(x => x.Token).Distinct().Count(), Is.EqualTo(5));
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.None));
        Assert.That(host.Items.Slots.Single().SalvoShots, Is.Zero);
        Assert.That(host.Items.Missiles, Is.Empty);
    }

    [Test]
    public void PreloadWaitsIndefinitelyAndEarlyUseIsNotQueued()
    {
        var host = new HostVehicleSession(99);
        host.Items.Grant(host.World, 1, HeldItem.Salvo);
        Assert.That(host.Items.RequestUse(host.World, 1, 1, host.Items.TokenHighWater), Is.False);
        for (int i = 0; i < 2400; i++) { host.Step(default, Observe); }
        Assert.That(host.Items.Missiles, Is.Empty);
        Assert.That(host.Items.Slots.Single().SalvoShots, Is.EqualTo(5));
        Assert.That(host.Items.Slots.Single().SalvoReadyTick, Is.Zero);
        Assert.That(host.Items.SalvoReady(host.Items.Slots.Single()), Is.True);
    }

    [TestCase(0f)]
    [TestCase(90f)]
    [TestCase(180f)]
    public void EachLaunchUsesCurrentCameraAimAndVehiclePoseWithoutSteeringEarlierRounds(float roll)
    {
        var host = Ready(new() { SalvoSpeed = 20, SalvoIntervalTicks = 30 });
        Use(host);
        var targets = new Dictionary<ulong, Vector3>();
        var directions = new[] { -Vector3.UnitZ, Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY };
        for (int i = 0; i < 140; i++)
        {
            var slot = host.Items.Slots.Single();
            var pose = new VehiclePhysicsState(new(i * .02f, 30, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, roll * MathF.PI / 180), Vector3.Zero, Vector3.Zero);
            if (slot.Item == HeldItem.Salvo)
            { host.AimItem(0, 99, 1, slot.Token, slot.SelectionRevision, (ulong)i + 1, directions[Math.Min(4, (i + 24) / 30)]); }
            host.Step(default, _ => new(pose, Vector3.UnitY));
            foreach (var missile in host.Items.Missiles)
            {
                var arc = missile.Arc!;
                if (targets.TryGetValue(missile.Id, out var prior)) { Assert.That(arc.Target, Is.EqualTo(prior)); }
                else
                {
                    targets.Add(missile.Id, arc.Target);
                    var aim = host.Items.Aims.SingleOrDefault();
                    if (aim is not null)
                    {
                        Assert.That(Vector3.Distance(arc.Target, SalvoFlight.ResolveTarget(pose, aim.Direction, 65, null)), Is.LessThan(.001));
                        Assert.That(Vector3.Distance(arc.Origin, SalvoFlight.LaunchOrigin(pose, aim.Yaw)), Is.LessThan(.001));
                    }
                }
            }
        }
        Assert.That(targets.Count, Is.EqualTo(5));
        Assert.That(targets.Values.Distinct().Count(), Is.EqualTo(5));
    }

    [Test]
    public void ResumeAndReplacementHostContinueDeadlinesAndFlightWithoutReplay()
    {
        var host = Ready(new() { SalvoSpeed = 20 }); host.Join(42);
        Use(host);
        for (int i = 0; i < 90; i++) { host.Step(default, Observe); }
        var state = new ItemPublication(1, host.Snapshot(), host.Items.Slots, host.Items.Missiles, []);
        var decoded = ItemCodec.DecodeState(ItemCodec.EncodeState(state));
        var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(decoded, host.World.State.Match!, null, host.Configuration)));
        var restored = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 2);
        Assert.That(restored.Items.Events, Is.Empty);
        Assert.That(restored.Items.Slots.Single().SalvoShots, Is.EqualTo(3));
        for (int i = 0; i < 500; i++)
        {
            host.Step(default, Observe); restored.Step(default, Observe);
            Assert.That(restored.Items.Missiles, Is.EqualTo(host.Items.Missiles));
            Assert.That(restored.Items.Events, Is.EqualTo(host.Items.Events));
            Assert.That(restored.Items.Slots, Is.EqualTo(host.Items.Slots));
        }
    }

    [TestCase(65f)]
    [TestCase(90f)]
    [TestCase(250f)]
    public void EachShotSamplesDeliberateAimReachWhileUnaimedFireKeepsConfiguredRange(float range)
    {
        var host = Ready(new() { SalvoRange = range });
        Use(host);
        var distances = new List<float>();
        for (int i = 0; i < 350; i++)
        {
            var slot = host.Items.Slots.Single();
            if (slot.Active.Item == HeldItem.Salvo)
            { host.AimItem(0, 99, 1, slot.Token, 0, (ulong)i + 1, Vector3.UnitX); }
            var held = (i / 75) % 2 == 0 ? InputButtons.AimItem : InputButtons.None;
            host.Step(new(0, 0, 0, 0, held, 0, 0), Observe);
            foreach (var launch in host.Items.Events.Where(e => !e.Impact))
            {
                var pose = host.World.GetVehicle(1).ObservedPhysics;
                Vector3 pivot = pose.Position + Vector3.Transform(WeaponAim.Pivot, pose.Orientation);
                distances.Add(Vector3.Distance(pivot, host.Items.Missiles.Single(m => m.Id == launch.Token).Arc!.Target));
            }
        }
        Assert.That(distances, Is.EqualTo(new[] { range * 2, range, range * 2, range, range * 2 }).Within(.001f));
    }

    [Test]
    public void ExpiredAimCannotKeepExtendedReachEvenWithAimingHeld()
    {
        var host = Ready();
        Use(host);
        var distances = new List<float>();
        for (int i = 0; i < 350; i++)
        {
            var slot = host.Items.Slots.Single();
            if (i < 10) { host.AimItem(0, 99, 1, slot.Token, 0, (ulong)i + 1, Vector3.UnitX); }
            host.Step(new(0, 0, 0, 0, InputButtons.AimItem, 0, 0), Observe);
            foreach (var launch in host.Items.Events.Where(e => !e.Impact))
            {
                var pose = host.World.GetVehicle(1).ObservedPhysics;
                Vector3 pivot = pose.Position + Vector3.Transform(WeaponAim.Pivot, pose.Orientation);
                distances.Add(Vector3.Distance(pivot, host.Items.Missiles.Single(m => m.Id == launch.Token).Arc!.Target));
            }
        }
        Assert.That(distances, Is.EqualTo(new[] { 130f, 65, 65, 65, 65 }).Within(.001f));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SwitchingAndDiscardCannotCancelCommittedShotsAndSecondMagazineRemainsUntouched(bool beforeFirstStep)
    {
        var host = Ready(); host.Items.Grant(host.World, 1, HeldItem.Salvo);
        Use(host);
        if (!beforeFirstStep) { host.Step(default, Observe); }
        var slot = host.Items.Slots.Single();
        Assert.That(host.Items.Discard(host.World, 1, 1, slot.Token, slot.SelectionRevision), Is.False);
        Assert.That(host.Items.Switch(host.World, 1, 1, 1), Is.True, "Acknowledge without deferring a switch.");
        Assert.That(host.Items.Slots.Single().ActiveSlot, Is.Zero);
        for (int i = 0; i < 500; i++) { host.Step(default, Observe); }
        Assert.That(host.Items.Slots.Single().SecondSalvoShots, Is.EqualTo(5));
        Assert.That(host.Items.Switch(host.World, 1, 1, 2), Is.True);
        for (int i = 0; i < 240; i++) { host.Step(default, Observe); }
        Use(host);
        int launches = 0;
        for (int i = 0; i < 500; i++) { host.Step(default, Observe); launches += host.Items.Events.Count(e => !e.Impact); }
        Assert.That(launches, Is.EqualTo(5));
        Assert.That(host.Items.Slots.Single().SecondItem, Is.EqualTo(HeldItem.None));
    }

    [Test]
    public void FullCapacityRejectsActivationWithoutConsumingAndReservationsProtectAcceptedVolley()
    {
        var host = Ready(); var slot = host.Items.Slots.Single();
        var full = Enumerable.Range(0, 12).Select(i => new MissileState((ulong)i + 100, 1, new(0, 100, 0), -Vector3.UnitZ, 900)).ToArray();
        host.Items.Restore(new(1, host.Snapshot(), [slot], full, []), 1, 200);
        for (int i = 0; i < 240; i++) { host.Step(default, Observe); }
        Use(host); host.Step(default, Observe);
        Assert.That(host.Items.Slots.Single().SalvoReadyTick, Is.Zero);
        Assert.That(host.Items.Slots.Single().SalvoShots, Is.EqualTo(5));
        Assert.That(host.Items.TokenHighWater, Is.EqualTo(200));
        Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), [slot with { SalvoReadyTick = host.World.State.Tick + 5 }], full, []));
    }

    [Test]
    public void FailedWorldBatchDoesNotConsumeLaunchDeadlineOrCapability()
    {
        var host = Ready(); Use(host);
        for (int i = 0; i < 5; i++) { host.Step(default, Observe); }
        var slots = host.Items.Slots; var token = host.Items.TokenHighWater;
        Assert.Throws<ArgumentException>(() => host.Step(default, Observe, (_, _) => float.NaN));
        Assert.That(host.Items.Slots, Is.EqualTo(slots));
        Assert.That(host.Items.TokenHighWater, Is.EqualTo(token));
        Assert.That(host.Items.Missiles, Is.Empty);
        host.Step(default, Observe);
        Assert.That(host.Items.Slots.Single().SalvoShots, Is.EqualTo(4));
    }

    [Test]
    public void ConcurrentActivationsReserveCapacityBeforeAcceptingWholeMagazines()
    {
        var host = Ready();
        for (ulong peer = 42; peer < 45; peer++) { host.Join(peer); }
        foreach (var vehicle in host.World.State.Vehicles.Where(v => v.VehicleId != 1))
        { Assert.That(host.Items.Grant(host.World, vehicle.VehicleId, HeldItem.Salvo), Is.True); }
        for (int i = 0; i < 240; i++) { host.Step(default, Observe); }
        foreach (var slot in host.Items.Slots)
        { Assert.That(host.Items.RequestUse(host.World, slot.Vehicle, slot.Life, slot.Token), Is.True); }
        host.Step(default, Observe);
        Assert.That(host.Items.Slots.Count(s => s.SalvoCommitted), Is.EqualTo(3));
        Assert.That(host.Items.Slots.Single(s => !s.SalvoCommitted).SalvoShots, Is.EqualTo(5));
        var launches = new List<ItemEvent>();
        for (int i = 0; i < 500; i++)
        {
            host.Step(default, Observe);
            launches.AddRange(host.Items.Events.Where(e => !e.Impact));
            Assert.That(host.Items.Missiles.Count + host.Items.Slots.Sum(s => s.ReservedSalvoShots), Is.LessThanOrEqualTo(ItemAuthority.MaximumProjectiles));
        }
        Assert.That(launches.Count, Is.EqualTo(15));
        Assert.That(launches.GroupBy(e => e.Owner).Select(g => g.Count()), Is.All.EqualTo(5));
        Assert.That(launches.Select(e => e.Token).Distinct().Count(), Is.EqualTo(15));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DeathAndRespawnCannotResumeCommittedMagazine(bool clearInventory)
    {
        var host = Ready(respawn: new() { DelayTicks = 1, ClearHeldItemOnDeath = clearInventory });
        host.Items.Grant(host.World, 1, HeldItem.Wrench);
        Use(host);
        for (int i = 0; i < 10; i++) { host.Step(default, Observe); }
        ulong life = host.World.GetVehicle(1).LifeId;
        var input = new InputFrame(host.World.State.Tick + 1, 0, 0, 0, 0, 0, 0);
        host.Items.Step(host.World, input, [new(1, input, Observe(host.World.GetVehicle(1)),
            [new(new DamageEffect(10000, Vector3.Zero, Vector3.Zero), new DamageContext("test", 0, "lethal"))])], (_, _) => null);
        Assert.That(host.Items.Missiles, Is.Empty);
        Assert.That(host.Items.Slots.Sum(s => s.ReservedSalvoShots), Is.Zero);
        for (int i = 0; i < 500; i++)
        {
            host.Step(default, Observe);
            Assert.That(host.Items.Events.Where(e => e.Item == HeldItem.Salvo), Is.Empty);
        }
        Assert.That(host.World.GetVehicle(1).LifeId, Is.GreaterThan(life));
        if (!clearInventory) { Assert.That(host.Items.Slots.Single().SecondItem, Is.EqualTo(HeldItem.Wrench)); }
    }

    [Test]
    public void FinishedMatchClearsCommittedMagazineAndFlight()
    {
        var host = Ready(); Use(host);
        for (int i = 0; i < 10; i++) { host.Step(default, Observe); }
        var current = host.World.State; var match = current.Match!;
        var finished = new Trackstorm.Core.Matches.MatchState(current.Tick, match.Revision + 1, match.KillTarget,
            Trackstorm.Core.Matches.MatchPhase.Finished, null, 1, match.Players.Select(p => p with { Wins = 1, Stunts = null }),
            mode: match.Mode, activeStartedAtTick: match.ActiveStartedAtTick, durationTicks: match.DurationTicks);
        host.World.Restore(new(current.Tick, current.LastInput, current.Vehicles, finished));
        host.Step(default, Observe);
        Assert.That(host.Items.Missiles, Is.Empty);
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.None));
        Assert.That(host.Items.Slots.Single().ReservedSalvoShots, Is.Zero);
    }

    [Test]
    public void ResetDepartureAndNewMatchClearScheduledAndFlyingRounds()
    {
        var host = Ready(); Use(host);
        for (int i = 0; i < 10; i++) { host.Step(default, Observe); }
        var input = new InputFrame(host.World.State.Tick + 1, 0, 0, 0, 0, 0, 0);
        var pose = host.World.GetVehicle(1).Movement.Physics;
        host.Items.Step(host.World, input, [new(1, input, new(pose, Vector3.UnitY), reset: pose)], (_, _) => null);
        Assert.That(host.Items.Missiles, Is.Empty);
        Assert.That(host.Items.Slots, Is.Empty);
        host = Ready(); Use(host); host.Step(default, Observe); host.Items.RemovePlayer(1);
        Assert.That(host.Items.Slots, Is.Empty);
        Assert.That(new HostVehicleSession(100).Items.Slots, Is.Empty);
    }

    [Test]
    public void LiveIntervalTuningRoundTripsAndCannotRefillOrBendExistingFlight()
    {
        var host = Ready(); Use(host);
        for (int i = 0; i < 10; i++) { host.Step(default, Observe); }
        var before = host.Items.Missiles.ToArray(); var deadline = host.Items.Slots.Single().SalvoReadyTick;
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["items.salvo_interval_ticks"] = 90, ["items.salvo_range"] = 80 }, out _), Is.True);
        Assert.That(host.Items.Missiles, Is.EqualTo(before));
        Assert.That(host.Items.Slots.Single().SalvoReadyTick, Is.EqualTo(deadline));
        Assert.That(GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(99, host.Configuration)).State, Is.EqualTo(host.Configuration));
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["items.salvo_count"] = 8 }, out _), Is.False);
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["items.salvo_interval_ticks"] = 1 }, out _), Is.False);
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.Salvo), Is.True);
        Assert.That(host.Items.Slots.Single().SecondSalvoShots, Is.EqualTo(5));
        while (host.World.State.Tick < deadline) { host.Step(default, Observe); }
        Assert.That(host.Items.Slots.Single().SalvoReadyTick, Is.EqualTo(deadline + 90));
    }

    private static HostVehicleSession Ready(ItemConfiguration? configuration = null, RespawnConfiguration? respawn = null)
    {
        var host = new HostVehicleSession(99, configuration, respawnConfiguration: respawn);
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["match.minimum_players"] = 1, ["match.countdown_ticks"] = 1 }, out _), Is.True);
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.Salvo), Is.True);
        for (int i = 0; i < 240; i++) { host.Step(default, Observe); }
        Assert.That(host.Items.SalvoReady(host.Items.Slots.Single()), Is.True);
        return host;
    }
    private static void Use(HostVehicleSession host)
    {
        var slot = host.Items.Slots.Single(s => s.Vehicle == 1);
        Assert.That(host.Items.RequestUse(host.World, 1, slot.Life, slot.Active.Token), Is.True);
    }
    private static VehicleObservation Observe(VehicleSnapshot state) => new(state.Movement.Physics, Vector3.UnitY);
}
