using System.Numerics;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class WrenchDeploymentTests
{
    [Test]
    public void EarlyPressIsRejectedAndNeverReplayedThenReadyUseConsumesExactlyOnce()
    {
        var host = new HostVehicleSession(99);
        host.Items.Grant(host.World, 1, HeldItem.Wrench);
        var slot = host.Items.Slots.Single();
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.False);
        WrenchTestPreparation.Wait(host);
        Assert.That(host.Items.Events, Is.Empty);
        Assert.That(host.Items.Slots.Single().Active.Item, Is.EqualTo(HeldItem.Wrench));
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.True);
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.False);
        host.Step(default, Observe);
        Assert.That(host.Items.Events.Count(e => e.Item == HeldItem.Wrench), Is.EqualTo(1));
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.False);
        host.Step(default, Observe);
        Assert.That(host.Items.Events, Is.Empty);
    }

    [Test]
    public void SwitchingDuplicateWrenchesAndRestoringCannotReuseOldReadiness()
    {
        var host = new HostVehicleSession(99);
        host.Items.Grant(host.World, 1, HeldItem.Wrench);
        host.Items.Grant(host.World, 1, HeldItem.Wrench);
        WrenchTestPreparation.Wait(host);
        host.SwitchItem(0, 99, 1, 1);
        var slot = host.Items.Slots.Single();
        Assert.That(host.UseItem(0, 99, 1, slot.SecondToken), Is.False);
        WrenchTestPreparation.Wait(host);
        var publication = new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], []);
        host.Items.Restore(publication, host.Items.Revision, host.Items.TokenHighWater);
        Assert.That(host.UseItem(0, 99, 1, slot.SecondToken), Is.False);
        WrenchTestPreparation.Wait(host);
        Assert.That(host.DiscardItem(0, 99, 1, slot.SecondToken, 1), Is.True);
        Assert.That(host.UseItem(0, 99, 1, slot.SecondToken), Is.False);
    }

    [Test]
    public void SlowRackTuningStillBlocksUseUntilRackAndArmHaveFinished()
    {
        var host = new HostVehicleSession(99);
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["vehicle.trunk_deployment_speed"] = 1, ["vehicle.rack_deployment_speed"] = 1 }, out _), Is.True);
        host.Items.Grant(host.World, 1, HeldItem.Wrench);
        var slot = host.Items.Slots.Single();
        for (int tick = 0; tick < 180; tick++) { host.Step(default, Observe); }
        Assert.That(host.UseItem(0, 99, 1, slot.Token), Is.False);
        WrenchTestPreparation.Wait(host);
        Assert.That(host.UseItem(0, 99, 1, slot.Token), Is.True);
    }

    private static VehicleObservation Observe(VehicleSnapshot state) => new(state.ObservedPhysics, Vector3.UnitY);
}
