using System.Numerics;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class ProxyMinePlacementTests
{
    [Test]
    public void ConsumesAtStartAndCannotAttractOrContactUntilCurrentGroundIsReached()
    {
        var host = Start();
        int contacts = 0;
        void Step(float x) => host.Step(default, s => new(new VehiclePhysicsState(new(x, 1, 0), Quaternion.Identity, Vector3.Zero, Vector3.Zero), Vector3.UnitY),
            placeMine: Ground, moveMine: (_, next) => { contacts++; return new(next, 1); });
        Step(0);
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.None));
        Assert.That(host.Items.Mines.Single().IsPlacing, Is.True);
        for (int i = 1; i < ProxyMineState.PlacementDurationTicks; i++) { Step(i * .02f); }
        var mine = host.Items.Mines.Single();
        Assert.That(contacts, Is.Zero);
        Assert.That(mine.IsPlacing, Is.False);
        Assert.That(mine.SeatingTicks, Is.EqualTo(30));
        Assert.That(mine.Velocity, Is.EqualTo(Vector3.Zero));
        Assert.That(mine.Position.X, Is.EqualTo((ProxyMineState.PlacementDurationTicks - 1) * .02f));
        Step(4);
        Assert.That(contacts, Is.EqualTo(1));
        Assert.That(host.Items.Mines, Is.Empty);
    }

    [Test]
    public void BusyArmRetainsSecondItemAndMissingGroundHoldsUntilSupportReturns()
    {
        var host = Start();
        Tick(host);
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.ProxyMine), Is.True);
        var held = host.Items.Slots.Single();
        Assert.That(host.UseItem(0, 99, held.Life, held.Token), Is.True);
        Tick(host);
        Assert.That(host.Items.Slots.Single(), Is.EqualTo(held));
        for (int i = 0; i < ProxyMineState.PlacementDurationTicks + 10; i++) { host.Step(default, Observe, placeMine: (_, _) => null); }
        Assert.That(host.Items.Mines.Single().IsPlacing, Is.True);
        Assert.That(host.Items.Mines.Single().PlacementTicks, Is.EqualTo(ProxyMineState.PlacementLoweringTicks));
        for (int i = 0; i < ProxyMineState.PlacementLoweringTicks; i++) { Tick(host); }
        Assert.That(host.Items.Mines.Single().IsPlacing, Is.False);
    }

    [Test]
    public void CodecCheckpointAndAuthorityReplacementContinueTheSamePlacement()
    {
        var host = Start();
        host.JoinPlayer(10, 2);
        for (int i = 0; i < 80; i++) { Tick(host); }
        var publication = new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], [], mines: host.Items.Mines);
        var decoded = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(publication, host.World.State.Match!, null, host.Configuration)));
        var restored = HostVehicleSession.Restore(decoded, host.CaptureAuthority(), 2);
        Assert.That(restored.Items.Mines, Is.EqualTo(host.Items.Mines));
        Assert.That(host.PrepareJoin(3, 2)!.Items.Mines, Is.EqualTo(host.Items.Mines));
        for (int i = 80; i < ProxyMineState.PlacementDurationTicks + 1; i++) { Tick(host); Tick(restored); }
        Assert.That(restored.Items.Mines, Is.EqualTo(host.Items.Mines));
        Assert.That(restored.Items.Mines.Single().IsPlacing, Is.False);
        Assert.That(restored.Items.Events, Is.Empty, "recovery does not replay use");
    }

    [Test]
    public void CannotReleaseOntoTerrainBeyondThePhysicalArmsReach()
    {
        var host = Start(); Tick(host);
        for (int i = 0; i < ProxyMineState.PlacementDurationTicks; i++)
        {
            host.Step(default, Observe, placeMine: (slot, pose) => Ground(slot, pose) with { Position = pose.Position + new Vector3(0, -5, 4.5f) });
        }
        Assert.That(host.Items.Mines.Single().IsPlacing, Is.True);
        for (int i = 0; i < ProxyMineState.PlacementLoweringTicks; i++) { Tick(host); }
        Assert.That(host.Items.Mines.Single().IsPlacing, Is.False);
    }

    [Test]
    public void DepartureCancelsCarriedMineButReleasedMineRemainsMatchOwned()
    {
        var pending = Start(); Tick(pending);
        pending.Items.RemovePlayer(1);
        Assert.That(pending.Items.Mines, Is.Empty);
        var placed = Start();
        for (int i = 0; i < ProxyMineState.PlacementDurationTicks; i++) { Tick(placed); }
        placed.Items.RemovePlayer(1);
        Assert.That(placed.Items.Mines.Count, Is.EqualTo(1));
    }

    [Test]
    public void RejectsMalformedContinuationAndNativeMotionCannotInventPlacement()
    {
        var host = Start(); Tick(host);
        var mine = host.Items.Mines.Single();
        foreach (var invalid in new[] { mine with { PlacementTicks = -1 }, mine with { PlacementTicks = 181 },
            mine with { PlacementLife = 0 }, mine with { PlacementLife = 999 }, mine with { Velocity = Vector3.One }, mine with { SeatingTicks = 29 } })
        {
            Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], [], mines: [invalid]));
        }
        Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], [], mines: [mine, mine with { Id = mine.Id + 1 }]));
        byte[] bytes = ItemCodec.EncodeState(new(1, host.Snapshot(), host.Items.Slots, [], [], mines: [mine]));
        bytes[2] = 13;
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeState(bytes));
        for (int i = 1; i < ProxyMineState.PlacementDurationTicks; i++) { Tick(host); }
        var before = host.Items.Mines.Single();
        Assert.Throws<ArgumentException>(() => host.Step(default, Observe, moveMine: (_, next) => new(next with { PlacementLife = 1, SeatingTicks = 30 })));
        Assert.That(host.Items.Mines.Single(), Is.EqualTo(before));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void DeathOrResetInterruptsPlacementInTheSameTransaction(bool death)
    {
        var host = Start(); Tick(host);
        var vehicle = host.World.GetVehicle(1);
        var input = new Trackstorm.Core.Input.InputFrame(host.World.State.Tick + 1, 0, 0, 0, 0, 0, 0);
        host.Items.Step(host.World, input, [new VehicleStepRequest(1, input, Observe(vehicle),
            death ? [new VehicleEffectRequest(new DamageEffect(10000, Vector3.Zero, Vector3.Zero), new DamageContext("world", 0, "test"))] : [],
            reset: death ? null : vehicle.ObservedPhysics)], (_, _) => null, placeMine: Ground);
        Assert.That(host.Items.Mines, Is.Empty);
        Assert.That(host.Items.Events.Any(e => e.Impact), Is.False);
    }

    private static HostVehicleSession Start()
    {
        var host = new HostVehicleSession(99);
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.ProxyMine), Is.True);
        var slot = host.Items.Slots.Single();
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.True);
        return host;
    }
    private static void Tick(HostVehicleSession host) => host.Step(default, Observe, placeMine: Ground, moveMine: (_, next) => new(next));
    private static VehicleObservation Observe(VehicleSnapshot state) => new(state.Movement.Physics, Vector3.UnitY);
    private static ProxyMineState Ground(ItemSlot slot, VehiclePhysicsState pose) => new(slot.Token, slot.Vehicle, pose.Position + Vector3.UnitZ * 4.5f, Vector3.Zero, Vector3.UnitY, 30);
}
