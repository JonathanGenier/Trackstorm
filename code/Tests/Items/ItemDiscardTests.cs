using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

/// <summary>Permanent selected-slot deletion, capability retirement and authority continuation.</summary>
[TestFixture]
internal sealed class ItemDiscardTests
{
    [TestCase(0, HeldItem.Wrench)]
    [TestCase(1, HeldItem.Missile)]
    [TestCase(0, HeldItem.Oil)]
    [TestCase(1, HeldItem.ProxyMine)]
    [TestCase(0, HeldItem.Nitro)]
    [TestCase(1, HeldItem.MachineGun)]
    [TestCase(0, HeldItem.Salvo)]
    [TestCase(1, HeldItem.Shield)]
    public void DeletesOnlySelectedPhysicalSlotImmediatelyAndCancelsItsUse(int index, HeldItem item)
    {
        var host = new HostVehicleSession(99);
        host.Items.Grant(host.World, 1, index == 0 ? item : HeldItem.MachineGun);
        host.Items.Grant(host.World, 1, index == 1 ? item : HeldItem.Nitro);
        if (index == 1) { host.SwitchItem(0, 99, 1, 1); }
        var before = host.Items.Slots.Single();
        var older = new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], [], shields: host.Items.Shields);
        var other = (before with { ActiveSlot = (byte)(1 - index) }).Active;
        host.UseItem(0, 99, 1, before.Active.Token);
        ulong revision = host.Items.ReliableRevision;
        Assert.That(host.DiscardItem(0, 99, 1, before.Active.Token, before.SelectionRevision), Is.True);
        var after = host.Items.Slots.Single();
        Assert.That(after.Active.Item, Is.EqualTo(HeldItem.None), "Clears before a world step.");
        Assert.That(after.Active.Token, Is.EqualTo(before.Active.Token), "Consumed capability stays retired.");
        Assert.That((after with { ActiveSlot = (byte)(1 - index) }).Active, Is.EqualTo(other with { NitroDeploymentTicks = 0 }));
        Assert.That((after.Active.NitroCharge, after.Active.SalvoShots, after.Active.SalvoReadyTick, after.Active.Ammo, after.EngagedToken), Is.EqualTo((0d, 0, 0ul, (MachineGunAmmo?)null, 0ul)));
        Assert.That((after.ActiveSlot, after.SelectionRevision), Is.EqualTo((before.ActiveSlot, before.SelectionRevision)));
        Assert.That(host.Items.ReliableRevision, Is.EqualTo(revision + 1));
        Assert.Throws<ArgumentException>(() => host.Items.Restore(older, host.Items.Revision, host.Items.TokenHighWater), "An existing authority cannot restore before its discard floor.");
        Assert.That(host.DiscardItem(0, 99, 1, before.Active.Token, before.SelectionRevision), Is.False);
        host.Step(default, Observe);
        Assert.That(host.Items.Events, Is.Empty);
        Assert.That(host.Items.Missiles, Is.Empty);
        Assert.That(host.Items.Patches, Is.Empty);
        Assert.That(host.Items.Mines, Is.Empty);
        Assert.That(host.Items.Shields, Is.Empty);
        Assert.That(host.Items.Slots.Single().Active.Item, Is.EqualTo(HeldItem.None));
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.Wrench), Is.True);
        Assert.That(host.DiscardItem(0, 99, 1, before.Active.Token, before.SelectionRevision), Is.False, "Delayed discard cannot delete a replacement.");
        Assert.That(host.UseItem(0, 99, 1, before.Active.Token), Is.False);
    }

    [Test]
    public void EmptySelectedSlotAndStaleOrForeignCapabilitiesDoNotChangeInventory()
    {
        var host = new HostVehicleSession(99);
        host.Join(42);
        host.Items.Grant(host.World, 2, HeldItem.Missile);
        MissileTestPreparation.Wait(host);
        var before = host.Items.Slots.Single();
        Assert.That(host.DiscardItem(0, 99, 1, before.Token, 0), Is.False);
        Assert.That(host.DiscardItem(100, 99, 1, before.Token, 0), Is.False);
        Assert.That(host.DiscardItem(42, 98, 1, before.Token, 0), Is.False);
        Assert.That(host.DiscardItem(42, 99, 2, before.Token, 0), Is.False);
        Assert.That(host.DiscardItem(42, 99, 1, before.Token + 1, 0), Is.False);
        host.SwitchItem(42, 99, 1, 1);
        ulong revision = host.Items.Revision;
        Assert.That(host.DiscardItem(42, 99, 1, 0, 1), Is.False);
        Assert.That(host.Items.Revision, Is.EqualTo(revision));
        host.SwitchItem(42, 99, 1, 2);
        Assert.That(host.DiscardItem(42, 99, 1, before.Token, 0), Is.False, "Switch-away/back rejects a delayed selection boundary.");
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.Missile));
        host.Suspend(42);
        Assert.That(host.DiscardItem(42, 99, 1, before.Token, 2), Is.False);
        Assert.That(host.ResumePlayer(43, 2), Is.True);
        Assert.That(host.DiscardItem(43, 99, 1, before.Token, 2), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ResumeMigrationAndRespawnCannotRestoreDiscardedOwnership(bool clearOnDeath)
    {
        var host = new HostVehicleSession(99, respawnConfiguration: new() { DelayTicks = 1, ClearHeldItemOnDeath = clearOnDeath });
        host.Join(42);
        host.Items.Grant(host.World, 2, HeldItem.Shield);
        host.Items.Grant(host.World, 2, HeldItem.Nitro);
        var before = host.Items.Slots.Single();
        Assert.That(host.DiscardItem(42, 99, 1, before.Token, 0), Is.True);
        host.Suspend(42);
        var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new ResumeCheckpoint(
            new ItemPublication(1, host.Snapshot(), host.Items.Slots, host.Items.Missiles, [], shields: host.Items.Shields, discardRevision: host.Items.DiscardRevision), host.World.State.Match!, null, host.Configuration)));
        var restored = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 1);
        Assert.That(restored.ResumePlayer(43, 2), Is.True);
        Assert.That(restored.Items.Slots.Single().Item, Is.EqualTo(HeldItem.None));
        Assert.That(restored.Items.Slots.Single().SecondItem, Is.EqualTo(HeldItem.Nitro));
        Assert.That(restored.Items.Shields, Is.Empty);
        Assert.That(restored.Items.DiscardRevision, Is.EqualTo(1));
        Assert.That(restored.DiscardItem(43, 99, 1, before.Token, 0), Is.False);
        var input = new InputFrame(restored.World.State.Tick + 1, 0, 0, 0, 0, 0, 0);
        restored.Items.Step(restored.World, input, restored.World.State.Vehicles.Select(state => new VehicleStepRequest(state.VehicleId, input, Observe(state),
            state.VehicleId == 2 ? [new VehicleEffectRequest(new DamageEffect(10000, Vector3.Zero, Vector3.Zero), new DamageContext("world", 0, "test"))] : [])).ToArray(), (_, _) => null);
        restored.Step(default, Observe);
        restored.Step(default, Observe);
        Assert.That(restored.World.GetVehicle(2).LifeId, Is.GreaterThan(1));
        Assert.That(restored.Items.Slots.All(slot => slot.Item == HeldItem.None), Is.True);
        Assert.That(restored.DiscardItem(43, 99, 1, before.Token, 0), Is.False);
        restored.Items.Grant(restored.World, 2, HeldItem.Wrench);
        Assert.That(restored.Items.Slots.Single(slot => slot.Vehicle == 2).Token, Is.GreaterThan(before.SecondToken));
    }

    [Test]
    public void DiscardStopsSustainedFireAndPreservesTheOtherAttachedItem()
    {
        var host = new HostVehicleSession(99, matchConfiguration: new() { MinimumPlayers = 1, CountdownTicks = 1 });
        host.Step(default, Observe); host.Step(default, Observe);
        host.Items.Grant(host.World, 1, HeldItem.MachineGun);
        host.Items.Grant(host.World, 1, HeldItem.Shield);
        var slot = host.Items.Slots.Single();
        host.UseItem(0, 99, slot.Life, slot.Token);
        var held = new InputFrame(1, 0, 0, 0, InputButtons.UseItem, InputButtons.UseItem, 0);
        host.Step(held, Observe, raycastWeapon: (_, _, _) => null);
        Assert.That(host.Items.Slots.Single().EngagedToken, Is.EqualTo(slot.Token));
        Assert.That(host.Items.Slots.Single().Ammo!.Remaining, Is.LessThan(slot.Ammo!.Remaining));
        var other = host.Items.Shields.Single();
        Assert.That(host.DiscardItem(0, 99, slot.Life, slot.Token, 0), Is.True);
        host.Step(held, Observe);
        Assert.That(host.Items.Events, Is.Empty, "Held use cannot fire again after deletion.");
        Assert.That(host.Items.Shields.Single(), Is.EqualTo(other));
        Assert.That(host.Items.Slots.Single().SecondToken, Is.EqualTo(slot.SecondToken));
    }

    [Test]
    public void DiscardPreservesAnAlreadyLaunchedProjectileAndOtherSlotPendingUse()
    {
        var host = new HostVehicleSession(99);
        host.Items.Grant(host.World, 1, HeldItem.Missile);
        MissileTestPreparation.Wait(host);
        host.UseItem(0, 99, 1, host.Items.Slots.Single().Token);
        host.Step(default, Observe);
        var missile = host.Items.Missiles.Single();
        host.Items.Grant(host.World, 1, HeldItem.Wrench);
        host.Items.Grant(host.World, 1, HeldItem.Oil);
        var first = host.Items.Slots.Single();
        host.UseItem(0, 99, 1, first.Token);
        host.SwitchItem(0, 99, 1, 1);
        Assert.That(host.DiscardItem(0, 99, 1, first.SecondToken, 1), Is.True);
        host.Step(default, Observe);
        Assert.That(host.Items.Events.Single().Item, Is.EqualTo(HeldItem.Wrench), "Discard cancels only its own pending capability.");
        Assert.That(host.Items.Missiles.Single().Id, Is.EqualTo(missile.Id), "Discard has no effect on existing world entities.");
        Assert.That(host.Items.Patches, Is.Empty);
    }

    [Test]
    public void InactiveApplicationPhaseCannotDiscard()
    {
        var host = new HostVehicleSession(99, requireActiveMatch: true);
        host.Items.Grant(host.World, 1, HeldItem.Wrench);
        var slot = host.Items.Slots.Single();
        Assert.That(host.DiscardItem(0, 99, slot.Life, slot.Token, 0), Is.False);
        Assert.That(host.Items.Slots.Single(), Is.EqualTo(slot));
        Assert.That(host.Items.DiscardRevision, Is.Zero);
    }

    [Test]
    public void ProtocolRejectsMalformedUnrelatedAndPreviousVersionCommands()
    {
        var bytes = ItemCodec.EncodeDiscard(99, 1, 7, 0);
        Assert.That(ItemCodec.DecodeDiscard(bytes), Is.EqualTo((99ul, 1ul, 7ul, 0ul)));
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeUse(bytes));
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeDiscard(bytes[..^1]));
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeDiscard([.. bytes, 0]));
        bytes[2] = 20;
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeDiscard(bytes));
        Assert.Throws<ArgumentException>(() => ItemCodec.EncodeDiscard(99, 1, 0, 0));
    }

    private static VehicleObservation Observe(VehicleSnapshot state) => new(state.Movement.Physics, Vector3.UnitY);
}
