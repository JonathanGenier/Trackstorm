using System.Numerics;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Development;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class EmpClawTests
{
    [Test]
    public void ExclusiveSpawnWeightsGrantRealEmpLoadAndAllowAnotherPickupAfterDiscard()
    {
        var edits = ItemRegistry.All.ToDictionary(i => $"spawns.{i.Key}_weight", i => i.Identity == HeldItem.EmpClaw ? 1d : 0d);
        Assert.That(GameplayOptions.TryApply(new(), edits, out var configuration, out _), Is.True);
        var host = new HostVehicleSession(99, configuration: configuration);
        host.RegisterSpawns(PrototypeArena.Configuration);
        var marker = PrototypeArena.Configuration.Items[0];
        var pose = new VehiclePhysicsState(marker.Position, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        host.World.Restore(new SimulationState(0, default, host.World.State.Vehicles.Select(v => v.VehicleId == 1
            ? new VehicleSnapshot(1, v.LifeId, new VehicleState(0, pose, false, false, 0, 0), v.Damage, pose) : v), host.World.State.Match));
        Assert.That(host.Spawns!.TryPickup(host.World, marker.Id, 1), Is.True);
        var first = host.Items.Slots.Single();
        Assert.That(first.Item, Is.EqualTo(HeldItem.EmpClaw));
        Assert.That(first.EmpClaw, Is.EqualTo(new EmpClawLoad(first.Token, 3)));
        Assert.That(host.Items.Discard(host.World, 1, first.Life, first.Token, 0), Is.True);
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.EmpClaw), Is.True);
        Assert.That(host.Items.Slots.Single().EmpClaw!.Magazine, Is.GreaterThan(first.Token));
        Assert.That(ItemCodec.DecodeState(ItemCodec.EncodeState(new(1, host.Snapshot(), host.Items.Slots, [], []))).Slots, Is.EqualTo(host.Items.Slots));
    }

    [Test]
    public void ThreeAcceptedVisualShotsRetireCapabilitiesRespectCadenceAndNeverCreateCombat()
    {
        var host = new HostVehicleSession(99, matchConfiguration: new() { MinimumPlayers = 1, CountdownTicks = 1 });
        host.Step(default, state => new VehicleObservation(state.Movement.Physics, Vector3.UnitY));
        host.Step(default, state => new VehicleObservation(state.Movement.Physics, Vector3.UnitY));
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.EmpClaw), Is.True);
        var initial = host.Items.Slots.Single();
        Assert.That(host.Items.RequestUse(host.World, 1, initial.Life, initial.Token), Is.False, "Deployment gates use.");
        for (int i = 0; i < 100; i++) { Step(host); }
        var ticks = new List<ulong>();
        for (int round = 0; round < 3; round++)
        {
            var slot = host.Items.Slots.Single();
            Assert.That(host.Items.RequestUse(host.World, 1, slot.Life, slot.Token), Is.True);
            Step(host);
            ticks.Add(host.World.State.Tick);
            var after = host.Items.Slots.Single();
            Assert.That(after.EmpClaw!.Remaining, Is.EqualTo(2 - round));
            Assert.That(after.EmpClaw.Magazine, Is.EqualTo(initial.Token));
            Assert.That(host.Items.Events.Single().Item, Is.EqualTo(HeldItem.EmpClaw));
            Assert.That(host.Items.Missiles, Is.Empty);
            Assert.That(host.Items.RequestUse(host.World, 1, slot.Life, slot.Token), Is.False, "Spent capability cannot replay.");
            var publication = new ItemPublication(host.Items.Revision, host.Snapshot(), host.Items.Slots, [], host.Items.Events);
            Assert.That(ItemCodec.DecodeState(ItemCodec.EncodeState(publication)).Slots, Is.EqualTo(publication.Slots));
            if (round < 2)
            {
                Assert.That(host.Items.RequestUse(host.World, 1, after.Life, after.Token), Is.False, "Cooldown gates next capability.");
                for (int i = 0; i < 50; i++) { Step(host); }
            }
        }
        Assert.That(ticks[2] - ticks[0], Is.LessThanOrEqualTo(120));
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.None));
        var empty = host.Items.Slots.Single();
        Assert.That(host.Items.RequestUse(host.World, 1, empty.Life, empty.Token), Is.False, "No fourth shot from the exhausted magazine.");
        Assert.That(host.Items.Patches.Concat<object>(host.Items.Mines).Concat(host.Items.Shields), Is.Empty);
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.EmpClaw), Is.True);
        Assert.That(host.Items.RequestUse(host.World, 1, initial.Life, initial.Token), Is.False);
    }

    [Test]
    public void BothSlotsAndRecoveryPreserveLoadsWithoutAcceptingOldLayoutsOrInvalidAmmo()
    {
        var host = new HostVehicleSession(99);
        host.Items.Grant(host.World, 1, HeldItem.EmpClaw);
        host.Items.Grant(host.World, 1, HeldItem.EmpClaw);
        var slot = host.Items.Slots.Single();
        Assert.That(slot.SecondEmpClaw!.Magazine, Is.GreaterThan(slot.EmpClaw!.Magazine));
        Assert.That((slot with { ActiveSlot = 1 }).Active.EmpClaw, Is.EqualTo(slot.SecondEmpClaw));
        var state = new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], []);
        var wire = ItemCodec.EncodeState(state);
        var decoded = ItemCodec.DecodeState(wire);
        var recovered = new ItemAuthority();
        recovered.Restore(decoded, host.Items.Revision, host.Items.TokenHighWater);
        Assert.That(recovered.Slots, Is.EqualTo(host.Items.Slots));
        wire[2] = 27;
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeState(wire));
        Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), [slot with { EmpClaw = new(slot.Token, 4) }], [], []));
        Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), [slot with { EmpClaw = null }], [], []));
    }

    [Test]
    public void DeveloperTimingControlsRoundTripAndRejectInvalidTransactions()
    {
        var edits = new Dictionary<string, double> { ["items.emp_claw_deploy_seconds"] = 2, ["items.emp_claw_stow_seconds"] = 1.5, ["items.emp_claw_interval_ticks"] = 30 };
        Assert.That(GameplayOptions.TryApply(new(), edits, out var tuning, out _), Is.True);
        Assert.That(GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(1, new(1, tuning))).State.Configuration, Is.EqualTo(tuning));
        Assert.That(GameplayOptions.TryApply(tuning, new Dictionary<string, double> { ["items.emp_claw_interval_ticks"] = 0 }, out var unchanged, out _), Is.False);
        Assert.That(unchanged, Is.EqualTo(tuning));
        var saved = DeveloperSettingsFile.Read(string.Empty, tuning);
        Assert.That(DeveloperSettingsFile.Read(saved.Write(tuning)).Configuration, Is.EqualTo(tuning));
    }

    [Test]
    public void LiveTimingEditDoesNotStretchAnAlreadyDeployingMagazine()
    {
        var host = new HostVehicleSession(99, matchConfiguration: new() { MinimumPlayers = 1, CountdownTicks = 1 });
        host.Items.Grant(host.World, 1, HeldItem.EmpClaw);
        Step(host);
        Assert.That(host.TryConfigure(host.Configuration.Revision, new Dictionary<string, double> { ["items.emp_claw_deploy_seconds"] = 4 }, out _), Is.True);
        for (int i = 0; i < 80; i++) { Step(host); }
        Assert.That(host.Items.EmpClawReady(host.Items.Slots.Single()), Is.True, "Current magazine retains its captured 1.2-second deployment.");
        var slot = host.Items.Slots.Single();
        host.Items.Discard(host.World, 1, slot.Life, slot.Token, 0);
        host.Items.Grant(host.World, 1, HeldItem.EmpClaw);
        for (int i = 0; i < 160; i++) { Step(host); }
        Assert.That(host.Items.EmpClawReady(host.Items.Slots.Single()), Is.False, "Replacement captures the new four-second duration.");
        for (int i = 0; i < 160; i++) { Step(host); }
        Assert.That(host.Items.EmpClawReady(host.Items.Slots.Single()), Is.True);
    }

    [Test]
    public void EnlargedCannonClearanceClampsDownwardAimWithoutChangingOtherWeapons()
    {
        var host = new HostVehicleSession(99);
        WeaponAimSolution? narrow = null, wide = null;
        for (ulong tick = 1; tick < 100; tick++)
        {
            Vector3 desired = Vector3.Normalize(new Vector3(0, -.3f, -1));
            narrow = WeaponAim.Solve(host.World.GetVehicle(1), 1, desired, narrow, new(), tick);
            wide = WeaponAim.Solve(host.World.GetVehicle(1), 1, desired, wide, new(), tick, clearanceRadius: .37f);
        }
        Assert.That(wide!.Clear, Is.False);
        Assert.That(wide.Pitch, Is.GreaterThan(narrow!.Pitch));
        Assert.That(WeaponAim.BodyDistance(WeaponAim.Pivot, WeaponAim.Direction(wide.Yaw, wide.Pitch), .37f), Is.Null);
    }

    private static void Step(HostVehicleSession host)
    {
        var slot = host.Items.Slots.Single().Active;
        if (slot.Item == HeldItem.EmpClaw) { host.Items.RequestAim(host.World, 1, slot.Life, slot.Token, 0, host.World.State.Tick + 1, -Vector3.UnitZ); }
        host.Step(default, state => new VehicleObservation(state.Movement.Physics, Vector3.UnitY));
    }
}
