using Trackstorm.Client.Items;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;
using System.Numerics;

namespace Trackstorm.Transport.Tests;

[TestFixture]
internal sealed class PickupSmashProjectionTests
{
    [Test]
    public void FirstFreshHostPublicationCanContainARealCollection()
    {
        var hostView = new PickupSmashProjection(["stack"], freshHost: true);
        hostView.Observe(State(1, 1), false, out var first);
        Assert.That(first.Single().Token, Is.EqualTo(1));
        Assert.That(hostView.Observe(State(1, 1), false, out _), Is.False);
        var lateClient = new PickupSmashProjection(["stack"]);
        lateClient.Observe(State(1, 1), false, out var history);
        Assert.That(history, Is.Empty);
    }

    [Test]
    public void DuplicateClaimsUnrelatedPublicationsAndRespawnsDoNotRepeatSmashes()
    {
        var view = new PickupSmashProjection(["stack"]);
        Assert.That(view.Observe(State(1, 0), false, out var baseline), Is.True);
        Assert.That(baseline, Is.Empty);
        var claim = State(2, 41);
        Assert.That(view.Observe(claim, false, out var fresh), Is.True);
        Assert.That(fresh.Select(s => s.Token), Is.EqualTo(new ulong[] { 41 }));
        Assert.That(view.Observe(claim, false, out var duplicate), Is.False);
        Assert.That(duplicate, Is.Empty);
        Assert.That(view.Observe(State(1, 0), false, out _), Is.False);
        view.Observe(State(3, 41), false, out var unrelated);
        view.Observe(State(4, 41, active: true), false, out var respawn);
        Assert.That(unrelated, Is.Empty);
        Assert.That(respawn, Is.Empty);
        view.Observe(State(5, 42), false, out var next);
        Assert.That(next.Single().Token, Is.EqualTo(42));
    }

    [Test]
    public void LateJoinAndRecoverySeedHistoryIncludingAuthorityRollback()
    {
        var view = new PickupSmashProjection(["stack"]);
        view.Observe(State(80, 40), false, out var admission);
        Assert.That(admission, Is.Empty);
        view.Observe(State(81, 41), false, out var claim);
        Assert.That(claim, Has.Count.EqualTo(1));
        view.Observe(State(10, 30), true, out var recovery);
        Assert.That(recovery, Is.Empty);
        Assert.That(view.Observe(State(10, 30), false, out _), Is.False);
        view.Observe(State(11, 31), false, out var afterRecovery);
        Assert.That(afterRecovery.Single().Token, Is.EqualTo(31));
    }

    [Test]
    public void NewClaimSurvivesFastReactivationAndDoesNotRequireClaimantsInventory()
    {
        var view = new PickupSmashProjection(["stack"]);
        view.Observe(State(1, 0), false, out _);
        var delayed = ItemCodec.DecodeState(ItemCodec.EncodeState(State(2, 9, active: true)));
        view.Observe(delayed, false, out var claims);
        Assert.That(delayed.Slots, Is.Empty);
        Assert.That(claims.Single().Token, Is.EqualTo(9));
        view.Observe(State(3, 8), false, out var old);
        Assert.That(old, Is.Empty);
    }

    private static ItemPublication State(ulong revision, ulong token, bool active = false)
    {
        ulong tick = active ? 600ul : 1;
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        var vehicle = new VehicleSnapshot(1, 1, new(tick, pose, false, false, 0, 0), new(100, 100, null, null), pose);
        return new(revision, new WorldSnapshot(311, tick, [new(vehicle, 0)]), [], [], [],
            [new("stack", token == 0 || active, token == 0 ? 0ul : 600ul, token == 0 ? 0ul : 1, token, token == 0 ? HeldItem.None : HeldItem.Missile)]);
    }
}
