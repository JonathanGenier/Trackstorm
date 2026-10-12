using System.Numerics;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class MissileAimTests
{
    private static VehicleObservation Observe(VehicleSnapshot state) => new(state.ObservedPhysics, Vector3.UnitY);

    [TestCase(0, 0)]
    [TestCase(90, 0)]
    [TestCase(180, 0)]
    [TestCase(-90, 45)]
    [TestCase(0, 90)]
    [TestCase(180, -30)]
    public void LaunchConsumesCurrentAcceptedDirectionAndNeverTracksLaterAim(float yaw, float pitch)
    {
        var host = new HostVehicleSession(99);
        host.GiveItem(0, HeldItem.Missile);
        var slot = host.Items.Slots.Single();
        Vector3 direction = WeaponAim.Direction(yaw * MathF.PI / 180, pitch * MathF.PI / 180);
        for (ulong sequence = 1; sequence <= 150; sequence++)
        {
            Assert.That(host.AimItem(0, 99, slot.Life, slot.Token, slot.SelectionRevision, sequence, direction), Is.True);
            host.Step(default, Observe);
        }
        var accepted = host.Items.AcceptedAim(1, slot.Life, slot.Token)!;
        Assert.That(accepted, Is.Not.Null);
        MissileState? launched = null;
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.True);
        host.Step(default, Observe, (missile, _) => { launched = missile; return null; });
        Assert.That(Vector3.Distance(launched!.Position, accepted.Origin), Is.LessThan(.0001));
        Assert.That(Vector3.Distance(Vector3.Normalize(launched.Velocity), direction), Is.LessThan(.0001));
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.None));
        Assert.That(host.Items.Events.Single(e => !e.Impact).Position, Is.EqualTo(launched.Position));
        Assert.That(host.AimItem(0, 99, slot.Life, slot.Token, slot.SelectionRevision, 151, Vector3.UnitX), Is.False);
        for (int tick = 0; tick < 30; tick++) { host.Step(default, Observe); }
        Assert.That(host.Items.Missiles.Single().Velocity, Is.EqualTo(launched.Velocity));
        Assert.That(host.Items.Missiles.Single().Position,
            Is.EqualTo(launched.Position + launched.Velocity * (31f / 60)).Using<Vector3>((a, b) => Vector3.Distance(a, b) < .001f));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BlockedOrExpiredCameraAimCannotFallbackOrQueue(bool expire)
    {
        var host = new HostVehicleSession(99);
        host.GiveItem(0, HeldItem.Missile);
        var slot = host.Items.Slots.Single();
        Vector3 direction = expire ? -Vector3.UnitZ : Vector3.Normalize(new(0, -1, -1));
        for (ulong i = 1; i <= 150; i++)
        {
            host.AimItem(0, 99, slot.Life, slot.Token, 0, i, direction);
            host.Step(default, Observe);
        }
        if (expire) { for (int i = 0; i < 20; i++) { host.Step(default, Observe); } }
        host.UseItem(0, 99, slot.Life, slot.Token);
        host.Step(default, Observe);
        Assert.That(host.Items.Missiles, Is.Empty);
        Assert.That(host.Items.Slots.Single().Token, Is.EqualTo(slot.Token));
        for (ulong i = 200; i < 230; i++)
        {
            host.AimItem(0, 99, slot.Life, slot.Token, 0, i, -Vector3.UnitZ);
            host.Step(default, Observe);
        }
        Assert.That(host.Items.Missiles, Is.Empty, "Rejected fire never waits for a clear aim.");
        Assert.That(host.Items.AcceptedAim(1, slot.Life, slot.Token), Is.Not.Null);
        host.UseItem(0, 99, slot.Life, slot.Token);
        host.Step(default, Observe);
        Assert.That(host.Items.Missiles, Has.Count.EqualTo(1));
    }
}
