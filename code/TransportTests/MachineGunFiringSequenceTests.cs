using Trackstorm.Client.Items;
using Trackstorm.Core.Items;

namespace Trackstorm.Transport.Tests;

internal sealed class MachineGunFiringSequenceTests
{
    [Test]
    public void ConfirmedConsumptionSurvivesReleaseBatchingAndReconstruction()
    {
        var sequence = new MachineGunFiringSequence();
        var slot = new ItemSlot(1, 1, 9, HeldItem.MachineGun) { Ammo = new(799, 800) };
        Assert.That(sequence.Observe(slot, 9, 1), Is.Zero);
        Assert.That(sequence.Observe(slot with { Ammo = new(797, 800) }, 9, 2), Is.EqualTo(1));
        Assert.That(sequence.Observe(slot with { Ammo = new(797, 800), EngagedToken = 0 }, 9, 0), Is.EqualTo(3));
        Assert.That(sequence.Observe(slot with { Ammo = new(796, 800) }, 9, 1), Is.EqualTo(3));
        Assert.That(new MachineGunFiringSequence().Observe(slot with { Ammo = new(298, 800) }, 9, 1), Is.EqualTo(501));
    }

    [Test]
    public void SecondSlotAndNewCapabilityDoNotBorrowOldBarrelPhase()
    {
        var sequence = new MachineGunFiringSequence();
        var slot = new ItemSlot(1, 1, 9, HeldItem.MachineGun) { Ammo = new(797, 800),
            SecondItem = HeldItem.MachineGun, SecondToken = 10, SecondAmmo = new(798, 800), ActiveSlot = 1 };
        Assert.That(sequence.Observe(slot, 9, 1), Is.EqualTo(2));
        Assert.That(sequence.Observe(slot, 10, 2), Is.Zero);
        Assert.That(sequence.Observe(slot with { Ammo = new(796, 800), ActiveSlot = 0 }, 9, 1), Is.EqualTo(3));
    }

    [Test]
    public void ExhaustedPublicationContinuesFinalRoundsAfterAmmoIsCleared()
    {
        var sequence = new MachineGunFiringSequence();
        var slot = new ItemSlot(1, 1, 9, HeldItem.MachineGun) { Ammo = new(2, 801) };
        sequence.Observe(slot, 9, 0);
        Assert.That(sequence.Observe(slot with { Item = HeldItem.None, Ammo = null }, 9, 2), Is.EqualTo(799));
    }
}
