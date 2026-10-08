using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

/// <summary>Firing consumes the shared accepted solution, never desired input or retained presentation.</summary>
[TestFixture]
internal sealed class MachineGunAimTests
{
    private HostVehicleSession _host = null!;
    private Vector3 _desired;
    private VehiclePhysicsState _pose = default;
    private readonly List<(Vector3 Start, Vector3 End)> _rays = [];

    [SetUp]
    public void SetUp()
    {
        _host = new(99, new() { MachineGunSpread = 0, MachineGunFireRate = 60 },
            matchConfiguration: new() { MinimumPlayers = 1, CountdownTicks = 1 });
        _pose = new(new(10, 4, 12), Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        _desired = -Vector3.UnitZ;
        _rays.Clear();
        Step(); Step();
        _host.Items.Grant(_host.World, 1, HeldItem.MachineGun);
    }

    [TestCase(0f, 0f)]
    [TestCase(90f, 0f)]
    [TestCase(180f, 0f)]
    [TestCase(-90f, 45f)]
    [TestCase(30f, 70f)]
    public void AcceptedDirectionAndPivotDriveActualRoundsOnRotatedChassis(float yaw, float pitch)
    {
        _pose = new(_pose.Position, Quaternion.CreateFromYawPitchRoll(.7f, .2f, -.3f), Vector3.Zero, Vector3.Zero);
        _desired = Vector3.Transform(WeaponAim.Direction(yaw * MathF.PI / 180, pitch * MathF.PI / 180), _pose.Orientation);
        Ready(); Engage(); Step(true);
        Assert.That(_rays, Has.Count.EqualTo(1));
        var ray = _rays.Single();
        Assert.That(Vector3.Distance(ray.Start, _pose.Position + Vector3.Transform(WeaponAim.Pivot, _pose.Orientation)), Is.LessThan(.0001));
        Assert.That(Vector3.Distance(Vector3.Normalize(ray.End - ray.Start), _desired), Is.LessThan(.0001));
        Assert.That(_host.Items.Events.Single().Origin, Is.EqualTo(ray.Start));
    }

    [Test]
    public void DeploymentMissingExpiredAndBlockedAimPreserveAmmoAndDoNotBankBurst()
    {
        Engage(); Step(true, false);
        Assert.That(_rays, Is.Empty);
        for (int i = 0; i < 5; i++) { Step(true); }
        Assert.That(_rays, Is.Empty, "Deployment must finish before held fire");
        Ready(); Engage(); Step(true);
        Assert.That(_rays, Has.Count.EqualTo(1));
        for (int i = 0; i < 40; i++) { Step(true, false); }
        var ammo = _host.Items.Slots.Single().Ammo;
        int shots = _rays.Count;
        for (int i = 0; i < 30; i++) { Step(true, false); }
        Assert.That(_host.Items.Slots.Single().Ammo, Is.EqualTo(ammo));
        Assert.That(_rays, Has.Count.EqualTo(shots));
        Step(true);
        Assert.That(_rays, Has.Count.EqualTo(shots + 1), "Fresh aim resumes one cadence tick, no backlog");
        _desired = Vector3.Normalize(new(0, -1, -1));
        for (int i = 0; i < 60; i++) { Step(); }
        Assert.That(_host.Items.Aims.Single().Ready, Is.False);
        ammo = _host.Items.Slots.Single().Ammo;
        Engage(); for (int i = 0; i < 30; i++) { Step(true); }
        Assert.That(_host.Items.Slots.Single().Ammo, Is.EqualTo(ammo));
    }

    [Test]
    public void TurningFiresAlongAcceptedRateLimitedDirectionAndCurrentObservedPose()
    {
        Ready(); Engage();
        _desired = Vector3.UnitX;
        _pose = new(new(90, 60, -10), _pose.Orientation, Vector3.Zero, Vector3.Zero);
        Step(true);
        var aim = _host.Items.Aims.Single();
        Assert.That(Vector3.Dot(aim.Direction, _desired), Is.LessThan(.3));
        Assert.That(Vector3.Distance(Vector3.Normalize(_rays.Single().End - _rays.Single().Start), aim.Direction), Is.LessThan(.0001));
        Assert.That(_rays.Single().Start, Is.EqualTo(_pose.Position + WeaponAim.Pivot));
    }

    [Test]
    public void SpreadRoundThatCrossesOwnerStopsAsCoverWithoutSelfDamageOrCadenceChange()
    {
        _host.TryConfigure(0, new Dictionary<string, double> { ["items.machine_gun_spread"] = 30 }, out _);
        Ready(); Engage();
        int blocked = 0;
        for (int i = 0; i < 120; i++)
        {
            Step(true);
            var shot = _host.Items.Events.Single();
            var direction = Vector3.Normalize(_rays.Last().End - _rays.Last().Start);
            var distance = WeaponAim.BodyDistance(WeaponAim.Pivot, direction);
            Assert.That(Vector3.Dot(direction, _desired), Is.GreaterThanOrEqualTo(MathF.Cos(MathF.PI / 6) - .00001f));
            if (distance.HasValue)
            {
                blocked++;
                Assert.That(shot.Impact, Is.True);
                Assert.That(Vector3.Distance(shot.Origin, shot.Position), Is.EqualTo(distance.Value).Within(.0001));
            }
        }
        Assert.That(blocked, Is.GreaterThan(0));
        Assert.That(_host.Items.Slots.Single().Ammo!.Remaining, Is.EqualTo(680));
        Assert.That(_host.World.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(_host.World.GetVehicle(1).Damage.MaxHP));
    }

    [Test]
    public void SwitchBackRequiresNewUseAndFreshReadyAim()
    {
        Ready(); Engage(); Step(true);
        _host.Items.Grant(_host.World, 1, HeldItem.Wrench);
        _host.SwitchItem(0, 99, 1, 1); Step(true);
        _host.SwitchItem(0, 99, 1, 2);
        for (int i = 0; i < 180; i++) { Step(true); }
        Assert.That(_rays, Has.Count.EqualTo(1));
        Engage(); Step(true);
        Assert.That(_rays, Has.Count.EqualTo(2));
    }

    private void Ready() { for (int i = 0; i < 180; i++) { Step(); } }
    private void Engage()
    {
        var slot = _host.Items.Slots.Single();
        Assert.That(_host.UseItem(0, 99, slot.Life, slot.Active.Token), Is.True);
    }
    private void Step(bool held = false, bool aim = true)
    {
        if (aim && _host.Items.Slots.FirstOrDefault() is { } slot && slot.Active.Item == HeldItem.MachineGun)
        { _host.Items.RequestAim(_host.World, 1, slot.Life, slot.Active.Token, slot.SelectionRevision, _host.World.State.Tick + 1, _desired); }
        _host.Step(new(0, 0, 0, 0, held ? InputButtons.UseItem : 0, 0, 0), _ => new(_pose, Vector3.UnitY),
            raycastWeapon: (_, start, end) => { _rays.Add((start, end)); return null; });
    }
}
