using System.Numerics;
using Trackstorm.Client.Hud;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Transport.Tests;

[TestFixture]
internal sealed class EmpHudTests
{
    private static VehicleSnapshot Vehicle(ulong life = 1)
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        return new(1, life, new(0, pose, false, false, 0, 0), new(1000, 1000, null, null), pose);
    }

    [Test]
    public void IndependentTimersRemovalAndFinalClearUseAcceptedMembership()
    {
        var feedback = new EmpHudFeedback();
        var claws = Enumerable.Range(1, 3).Select(i => new EmpClawAttachment((ulong)i, 2, 1, 1,
            Vector3.Zero, Vector3.UnitY, (ulong)i * 60, (ulong)i * 60 + 1800)).ToArray();
        string text = feedback.Project(Vehicle(), claws, 180, 1, 0);
        Assert.That(text, Does.Contain("3 CLAWS").And.Contain("1: 28s").And.Contain("2: 29s").And.Contain("3: 30s"));
        text = feedback.Project(Vehicle(), [claws[0], claws[2]], 240, 1, 100);
        Assert.That(text, Does.Contain("1: 27s").And.Contain("3: 29s").And.Contain("1 CLAW REMOVED · 2 ACTIVE"));
        Assert.That(text, Does.Not.Contain("POWER RESTORED"));
        text = feedback.Project(Vehicle(), [claws[0], claws[2]], 1860, 1, 2000);
        Assert.That(text, Does.Contain("1 CLAW").And.Contain("3: 2s"));
        text = feedback.Project(Vehicle(), [], 1980, 1, 3000);
        Assert.That(text, Is.EqualTo("EMP CLEARED · POWER RESTORED"));
        Assert.That(feedback.Project(Vehicle(), [], 1980, 1, 4801), Is.Empty);
    }

    [Test]
    public void NewAttachmentClearsObsoletePowerRestoredFeedback()
    {
        var feedback = new EmpHudFeedback();
        EmpClawAttachment claw = new(1, 2, 1, 1, Vector3.Zero, Vector3.UnitY, 0, 1800);
        feedback.Project(Vehicle(), [claw], 0, 1, 0);
        Assert.That(feedback.Project(Vehicle(), [], 1, 1, 1), Does.Contain("POWER RESTORED"));
        string text = feedback.Project(Vehicle(), [claw with { Id = 2 }], 2, 1, 2);
        Assert.That(text, Does.Contain("EMP ACTIVE"));
        Assert.That(text, Does.Not.Contain("POWER RESTORED"));
    }

    [Test]
    public void ReseedLifeAndUnavailableVehicleDoNotReplayRemoval()
    {
        var feedback = new EmpHudFeedback();
        EmpClawAttachment claw = new(1, 2, 1, 1, Vector3.Zero, Vector3.UnitY, 0, 1800);
        Assert.That(feedback.Project(Vehicle(), [claw], 0, 1, 0), Does.Contain("30s"));
        Assert.That(feedback.Project(Vehicle(), [], 1, 2, 1), Is.Empty);
        feedback.Project(Vehicle(), [claw], 1, 2, 2);
        Assert.That(feedback.Project(Vehicle(2), [claw], 2, 2, 3), Is.Empty);
        Assert.That(feedback.Project(null, [claw], 2, 2, 4), Is.Empty);
    }

    [Test]
    public void AmmoUsesEachPhysicalMagazineAndClearsOnDepletionReplacementAndLifeChange()
    {
        var slot = new ItemSlot(1, 1, 10, HeldItem.EmpClaw) { EmpClaw = new(10, 3),
            SecondToken = 20, SecondItem = HeldItem.EmpClaw, SecondEmpClaw = new(20, 2) };
        for (byte remaining = 3; remaining > 0; remaining--)
        {
            var view = CombatHudView.From(Vehicle(), slot with { EmpClaw = new(10, remaining), ActiveSlot = 1 }, 0);
            Assert.That(view.FirstSlot.Resource!.Text, Is.EqualTo(remaining.ToString()));
            Assert.That(view.SecondSlot.Resource!.Text, Is.EqualTo("2"));
        }
        Assert.That(CombatHudView.From(Vehicle(), slot with { Item = HeldItem.None, EmpClaw = new(10, 0) }, 0).FirstSlot, Is.EqualTo(ItemHudSlotView.Empty));
        Assert.That(CombatHudView.From(Vehicle(), slot with { Item = HeldItem.Missile }, 0).FirstSlot.Resource, Is.Null);
        Assert.That(CombatHudView.From(Vehicle(2), slot, 0).FirstSlot, Is.EqualTo(ItemHudSlotView.Empty));
    }
}
