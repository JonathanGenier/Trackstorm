using System.Numerics;
using System.Text.Json;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Sessions;

[TestFixture]
internal sealed class PondPracticeTests
{
    private static ArenaConfiguration Map()
    {
        var prototype = PrototypeArena.Configuration;
        return new(new(-500), new(500), prototype.Players,
            [new ArenaSpawn("item-infield-04-water", new(77, .101f, -43), -MathF.PI / 2)], prototype.Surfaces);
    }

    private static HostVehicleSession Create()
    {
        var host = new HostVehicleSession(100, arena: Map(), respawnConfiguration: new() { DelayTicks = 2 });
        host.RegisterSpawns(Map());
        for (ulong id = 2; id <= 5; id++) { host.DrivePracticeCar(id, true); }
        return host;
    }

    private static void Step(HostVehicleSession host, bool collect = true)
    {
        host.Step(default, vehicle =>
        {
            var pose = vehicle.Movement.Physics;
            var velocity = pose.LinearVelocity with { Y = 0 };
            return new(new(pose.Position + velocity / 60, pose.Orientation, velocity, Vector3.Zero), Vector3.UnitY);
        });
        if (collect) { host.CollectPickups(); host.CollectPickups(); }
    }

    private static void Until(HostVehicleSession host, Func<bool> ready, bool collect = true)
    {
        for (int i = 0; i < 2400 && !ready(); i++) { Step(host, collect); }
        Assert.That(ready(), Is.True, $"phase={host.CaptureAuthority().PondPractice}, pose={host.World.GetVehicle(5).Movement.Physics}");
    }

    private static void Destroy(HostVehicleSession host)
    {
        var input = new InputFrame(host.World.State.Tick + 1, 0, 0, 0, 0, 0, 0);
        host.World.Step(input, host.World.State.Vehicles.Select(v => new VehicleStepRequest(v.VehicleId, input,
            new(v.Movement.Physics, Vector3.UnitY), v.VehicleId == 5
                ? [new VehicleEffectRequest(new(10000, Vector3.Zero, Vector3.Zero), new("test", 1, "pond"))] : [])).ToArray());
    }

    private static HostVehicleSession Recover(HostVehicleSession host, HostRestoreState? supplied = null)
    {
        var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(
            new ItemPublication(1, host.Snapshot(), host.Items.Slots, host.Items.Missiles, [], host.Spawns!.States,
                balances: host.Spawns.Balances, discardRevision: host.Items.DiscardRevision), host.World.State.Match!, null, host.Configuration)));
        // Exercise the same System.Text.Json host supplement used by the migration envelope.
        var continuation = JsonSerializer.Deserialize<HostRestoreState>(JsonSerializer.Serialize(supplied ?? host.CaptureAuthority()))!;
        var restored = HostVehicleSession.Restore(checkpoint, continuation, 1, arena: Map());
        for (ulong id = 2; id <= 5; id++) { restored.DrivePracticeCar(id, true); }
        return restored;
    }

    [Test]
    public void OnePickupTripPerLifeReturnsAndStaysParkedEvenAfterDiscard()
    {
        var host = Create();
        var home = host.World.GetVehicle(5).Movement.Physics;
        Assert.That(home.Position.X, Is.EqualTo(77 - 6.096f).Within(.001f));
        Until(host, () => host.CaptureAuthority().PondPractice?.Phase == PondPracticePhase.Parked);
        Assert.That(host.Spawns!.Balances.Single(b => b.Player == 5).Total, Is.EqualTo(1));
        var parked = host.World.GetVehicle(5).Movement.Physics;
        Assert.That(Vector3.Distance(parked.Position, home.Position), Is.LessThan(.25));
        var slot = host.Items.Slots.Single(s => s.Vehicle == 5);
        Assert.That(host.Items.Discard(host.World, 5, slot.Life, slot.Active.Token, slot.SelectionRevision), Is.True);
        host = Recover(host);
        for (int i = 0; i < 900; i++) { Step(host); }
        Assert.That(host.World.GetVehicle(5).Movement.Physics, Is.EqualTo(parked));
        Assert.That(host.Spawns!.Balances.Single(b => b.Player == 5).Total, Is.EqualTo(1));
        Assert.That(host.Spawns.States.Single().Available, Is.True);
        Assert.That(host.ResumePlayer(20, 5), Is.False);
    }

    [Test]
    public void DelayedPickupWaitsWithoutOscillationAndRestoresEveryMovingPhase()
    {
        var host = Create();
        for (int i = 0; i < 80; i++) { Step(host, false); }
        host = Recover(host);
        Until(host, () => host.CaptureAuthority().PondPractice?.Phase == PondPracticePhase.Waiting, false);
        var wait = host.World.GetVehicle(5).Movement.Physics;
        Assert.That(Math.Abs(wait.Position.X - 77), Is.LessThan(.25));
        host = Recover(host);
        for (int i = 0; i < 300; i++) { Step(host, false); }
        Assert.That(host.World.GetVehicle(5).Movement.Physics, Is.EqualTo(wait));
        Step(host);
        Until(host, () => host.CaptureAuthority().PondPractice?.Phase == PondPracticePhase.Returning);
        for (int i = 0; i < 60; i++) { Step(host); }
        host = Recover(host);
        Until(host, () => host.CaptureAuthority().PondPractice?.Phase == PondPracticePhase.Parked);
        Assert.That(host.Spawns!.Balances.Single(b => b.Player == 5).Total, Is.EqualTo(1));
    }

    [Test]
    public void FullInventoryAtStartNeverApproachesAndDestructionRestartsTrip()
    {
        var host = Create();
        Assert.That(host.Items.Grant(host.World, 5, HeldItem.Missile), Is.True);
        Assert.That(host.Items.Grant(host.World, 5, HeldItem.Wrench), Is.True);
        var home = host.World.GetVehicle(5).Movement.Physics;
        for (int i = 0; i < 700; i++) { Step(host); }
        Assert.That(host.World.GetVehicle(5).Movement.Physics, Is.EqualTo(home));
        Assert.That(host.Spawns!.Balances, Is.Empty);
        for (int cycle = 0; cycle < 3; cycle++)
        {
            ulong life = host.World.GetVehicle(5).LifeId;
            Destroy(host);
            Until(host, () => host.World.GetVehicle(5).LifeId > life);
            Assert.That(host.World.GetVehicle(5).Movement.Physics.Position, Is.EqualTo(home.Position));
            Step(host);
            Until(host, () => host.CaptureAuthority().PondPractice?.Phase == PondPracticePhase.Parked);
            Assert.That(host.Spawns.Balances.Single(b => b.Player == 5).Total, Is.EqualTo(cycle + 1));
        }
    }

    [Test]
    public void FullInventoryDuringApproachReturnsWithoutAnotherPickup()
    {
        var host = Create();
        var home = host.World.GetVehicle(5).Movement.Physics.Position;
        for (int i = 0; i < 70; i++) { Step(host, false); }
        Assert.That(host.Items.Grant(host.World, 5, HeldItem.Missile), Is.True);
        Assert.That(host.Items.Grant(host.World, 5, HeldItem.Wrench), Is.True);
        Until(host, () => host.CaptureAuthority().PondPractice?.Phase == PondPracticePhase.Parked);
        Assert.That(Vector3.Distance(host.World.GetVehicle(5).Movement.Physics.Position, home), Is.LessThan(.25));
        Assert.That(host.Spawns!.Balances, Is.Empty);
    }

    [Test]
    public void HomeRespawnWaitsUntilAnotherVehicleClearsItsFootprint()
    {
        var host = Create();
        var home = host.World.GetVehicle(5).Movement.Physics;
        Destroy(host);
        PlaceHuman(home);
        for (int i = 0; i < 10; i++) { Step(host); }
        Assert.That(host.World.GetVehicle(5).CanInteract, Is.False);
        PlaceHuman(Map().Spawn(0));
        Step(host);
        Assert.That(host.World.GetVehicle(5).LifeId, Is.EqualTo(2));
        Assert.That(host.World.GetVehicle(5).Movement.Physics.Position, Is.EqualTo(home.Position));

        void PlaceHuman(VehiclePhysicsState pose)
        {
            var boundary = host.World.State;
            host.World.Restore(new(boundary.Tick, boundary.LastInput, boundary.Vehicles.Select(v => v.VehicleId == 1
                ? new VehicleSnapshot(v.VehicleId, v.LifeId, new(boundary.Tick, pose, false, false, 0, 0), v.Damage, pose) : v), boundary.Match));
        }
    }

    [Test]
    public void RecoveryRejectsInvalidPondIdentityLifeAndPhase()
    {
        var host = Create();
        Step(host);
        var continuation = host.CaptureAuthority();
        foreach (var invalid in new[]
        {
            continuation.PondPractice! with { Vehicle = 1 },
            continuation.PondPractice! with { Life = 100 },
            continuation.PondPractice! with { Phase = (PondPracticePhase)99 },
        })
            Assert.Throws<ArgumentException>(() => Recover(host, continuation with { PondPractice = invalid }));
    }
}
