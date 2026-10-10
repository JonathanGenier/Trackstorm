using System.Numerics;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class MissileDeploymentTests
{
    private static VehicleObservation Observe(VehicleSnapshot state) => new(state.ObservedPhysics, Vector3.UnitY);

    [Test]
    public void EarlyPressIsRejectedAndNeverReplayedAtReadiness()
    {
        var host = new HostVehicleSession(99);
        Assert.That(host.GiveItem(0, HeldItem.Missile), Is.True);
        var slot = host.Items.Slots.Single();
        Assert.That(slot.MissileReadyTick - slot.MissileDeployStartTick, Is.EqualTo(96));
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.False);
        while (host.World.State.Tick < slot.MissileReadyTick)
        {
            Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.False);
            host.Step(default, Observe);
        }
        host.Step(default, Observe);
        Assert.That(host.Items.Missiles, Is.Empty, "An early tap never becomes a deferred shot.");
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.Missile));
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.True);
        host.Step(default, Observe);
        Assert.That(host.Items.Missiles, Has.Count.EqualTo(1));
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.None));
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.False);
    }

    [Test]
    public void NewPickupKeepsItsCapabilityAndWaitsForCompleteReturn()
    {
        var host = new HostVehicleSession(99);
        host.GiveItem(0, HeldItem.Missile);
        MissileTestPreparation.Wait(host);
        var original = host.Items.Slots.Single();
        host.UseItem(0, 99, original.Life, original.Token);
        host.Step(default, Observe);
        var stow = host.Items.Slots.Single();
        Assert.That(stow.MissileStowEndTick - stow.MissileStowStartTick, Is.EqualTo(54));
        Assert.That(host.GiveItem(0, HeldItem.Missile), Is.True);
        var next = host.Items.Slots.Single();
        Assert.That(next.Token, Is.Not.EqualTo(original.Token));
        Assert.That(next.MissileDeployStartTick, Is.EqualTo(stow.MissileStowEndTick));
        Assert.That(next.MissileReadyTick, Is.EqualTo(stow.MissileStowEndTick + 96));
        Assert.That(host.UseItem(0, 99, next.Life, next.Token), Is.False);
        var publication = new ItemPublication(1, host.Snapshot(), host.Items.Slots, host.Items.Missiles, []);
        var decoded = ItemCodec.DecodeState(ItemCodec.EncodeState(publication));
        Assert.That(decoded.Slots.Single(), Is.EqualTo(next));
        Assert.That(decoded.Missiles.Single().Id, Is.EqualTo(original.Token));
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["items.missile_deploy_seconds"] = 3 }, out _), Is.True);
        Assert.That(host.Items.Slots.Single().MissileReadyTick, Is.EqualTo(next.MissileReadyTick), "Live retuning cannot jump an in-progress mechanism.");
        MissileTestPreparation.Wait(host);
        Assert.That(host.Items.Slots.Single().Token, Is.EqualTo(next.Token));
        Assert.That(host.UseItem(0, 99, next.Life, next.Token), Is.True);
    }

    [Test]
    public void RejectedLaunchDoesNotBeginStowAndRetainsTheReadyRound()
    {
        var host = new HostVehicleSession(99);
        host.GiveItem(0, HeldItem.Missile);
        MissileTestPreparation.Wait(host);
        var ready = host.Items.Slots.Single();
        host.UseItem(0, 99, ready.Life, ready.Token);
        Assert.Throws<ArgumentException>(() => host.Step(default, Observe, (_, _) => float.NaN));
        Assert.That(host.Items.Slots.Single(), Is.EqualTo(ready));
        Assert.That(host.Items.Missiles, Is.Empty);
        host.Step(default, Observe);
        Assert.That(host.Items.Missiles, Has.Count.EqualTo(1));
        Assert.That(host.Items.Slots.Single().MissileStowEndTick, Is.GreaterThan(host.World.State.Tick));
    }

    [Test]
    public void WireRejectsInvertedOrExcessiveDeploymentIntervals()
    {
        var host = new HostVehicleSession(99);
        host.GiveItem(0, HeldItem.Missile);
        var slot = host.Items.Slots.Single();
        foreach (var bad in new[] { slot with { MissileDeployStartTick = 1000 }, slot with { MissileReadyTick = 1000 },
            slot with { MissileStowStartTick = 2, MissileStowEndTick = 1 }, slot with { MissileStowEndTick = 181 } })
        { Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), [bad], [], [])); }
    }
}
