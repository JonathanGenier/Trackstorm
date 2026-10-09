using System.Numerics;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class WeaponAimTests
{
    [TestCase(0, 0)]
    [TestCase(90, 0)]
    [TestCase(180, 0)]
    [TestCase(-90, 60)]
    public void AuthoritativeSolutionReachesForwardSideRearAndAirborneDirections(float yawDegrees, float pitchDegrees)
    {
        var host = Create();
        var slot = host.Items.Slots.Single();
        Vector3 desired = WeaponAim.Direction(yawDegrees * MathF.PI / 180, pitchDegrees * MathF.PI / 180);
        for (ulong i = 1; i <= 130; i++)
        {
            Assert.That(host.AimItem(0, 99, slot.Life, slot.Token, 0, i, desired), Is.True);
            host.Step(default, Observe);
        }
        var aim = host.Items.AcceptedAim(1, slot.Life, slot.Token)!;
        Assert.That(aim, Is.Not.Null);
        Assert.That(Vector3.Distance(aim.Direction, desired), Is.LessThan(.0001));
        Assert.That(WeaponAim.IntersectsBody(WeaponAim.Pivot, WeaponAim.Direction(aim.Yaw, aim.Pitch)), Is.False);
    }

    [Test]
    public void DownwardSelfIntersectionIsClampedAndCannotAuthorizeFire()
    {
        var host = Create();
        var state = host.World.GetVehicle(1);
        WeaponAimSolution? previous = null;
        for (int i = 0; i < 30; i++) { previous = WeaponAim.Solve(state, 1, Vector3.Normalize(new(0, -1, -1)), previous, new(), (ulong)i); }
        Assert.That(previous!.Clear, Is.False);
        Assert.That(WeaponAim.IntersectsBody(WeaponAim.Pivot, WeaponAim.Direction(previous.Yaw, previous.Pitch)), Is.False);
        Assert.That(previous.Direction.Y, Is.LessThan(0), "Useful bounded downward aim remains possible.");
        Assert.That(previous.Direction.Y, Is.GreaterThan(-.15));
    }

    [Test]
    public void UnitBoundsSenderLifeCapabilityOrderAndSelectionAreValidated()
    {
        var host = Create();
        var slot = host.Items.Slots.Single();
        Assert.That(host.AimItem(10, 99, slot.Life, slot.Token, 0, 1, -Vector3.UnitZ), Is.False);
        Assert.That(host.AimItem(0, 98, slot.Life, slot.Token, 0, 1, -Vector3.UnitZ), Is.False);
        Assert.That(host.AimItem(0, 99, slot.Life + 1, slot.Token, 0, 1, -Vector3.UnitZ), Is.False);
        Assert.That(host.AimItem(0, 99, slot.Life, slot.Token + 1, 0, 1, -Vector3.UnitZ), Is.False);
        foreach (Vector3 invalid in new[] { Vector3.Zero, Vector3.One, new Vector3(float.NaN, 0, -1), new Vector3(0, float.PositiveInfinity, 0) })
        { Assert.That(host.AimItem(0, 99, slot.Life, slot.Token, 0, 1, invalid), Is.False); }
        Assert.That(host.AimItem(0, 99, slot.Life, slot.Token, 0, 2, -Vector3.UnitZ), Is.True);
        Assert.That(host.AimItem(0, 99, slot.Life, slot.Token, 0, 2, -Vector3.UnitZ), Is.False);
        Assert.That(host.SwitchItem(0, 99, slot.Life, 1), Is.True);
        Assert.That(host.Items.Aims, Is.Empty);
        Assert.That(host.SwitchItem(0, 99, slot.Life, 2), Is.True);
        Assert.That(host.AimItem(0, 99, slot.Life, slot.Token, 0, 3, -Vector3.UnitZ), Is.False);
        Assert.That(host.AimItem(0, 99, slot.Life, slot.Token, 2, 3, -Vector3.UnitZ), Is.True);
    }

    [Test]
    public void AimExpiresAndRecoveryRequiresFreshInputWithoutResettingReadyDeploymentOnFirstInput()
    {
        var host = Create();
        var slot = host.Items.Slots.Single();
        for (int i = 0; i < 110; i++) { host.Step(default, Observe); }
        host.AimItem(0, 99, slot.Life, slot.Token, 0, 1, -Vector3.UnitZ);
        host.Step(default, Observe);
        Assert.That(host.Items.AcceptedAim(1, slot.Life, slot.Token), Is.Not.Null);
        for (int i = 0; i < 17; i++) { host.Step(default, Observe); }
        Assert.That(host.Items.Aims, Is.Empty);
        host.AimItem(0, 99, slot.Life, slot.Token, 0, 2, -Vector3.UnitZ);
        host.Step(default, Observe);
        Assert.That(host.Items.AcceptedAim(1, slot.Life, slot.Token), Is.Not.Null);
        var publication = new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], []);
        host.Items.Restore(publication, host.Items.Revision, host.Items.TokenHighWater);
        Assert.That(host.Items.Aims, Is.Empty);
        host.AimItem(0, 99, slot.Life, slot.Token, 0, 3, -Vector3.UnitZ);
        host.Step(default, Observe);
        Assert.That(host.Items.Aims.Single().Ready, Is.False, "Recovery reconstructs deployment rather than retaining transient readiness.");
    }

    [Test]
    public void TurnRateAndElevationCannotBeBypassedByRequestMagnitudeOrRepetition()
    {
        var host = Create();
        var slot = host.Items.Slots.Single();
        for (ulong i = 1; i <= 50; i++) { host.AimItem(0, 99, slot.Life, slot.Token, 0, i, Vector3.UnitZ); }
        host.Step(default, Observe);
        Assert.That(Math.Abs(host.Items.Aims.Single().Yaw), Is.EqualTo(9 * MathF.PI / 180).Within(.00001));
        Assert.Throws<ArgumentException>(() => WeaponAim.Solve(host.World.GetVehicle(1), slot.Token, Vector3.UnitZ * 2, null, new(), 1));
    }

    [Test]
    public void AimWireIsBoundedAndRejectsCorruptionAndTrailingBytes()
    {
        byte[] request = ItemCodec.EncodeAim(99, 1, 1, 0, 1, -Vector3.UnitZ);
        Assert.That(request.Length, Is.EqualTo(56));
        Assert.That(ItemCodec.DecodeAim(request).Direction, Is.EqualTo(-Vector3.UnitZ));
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeAim(request.AsSpan(0, request.Length - 1)));
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeAim([.. request, 0]));
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeAim(new byte[1024]));
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeAims(new byte[558]));
        BitConverter.GetBytes(float.NaN).CopyTo(request, 44);
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeAim(request));
        var aim = WeaponAim.Solve(Create().World.GetVehicle(1), 1, -Vector3.UnitZ, null, new(), 3);
        byte[] publication = ItemCodec.EncodeAims(99, 3, 0, [aim]);
        Assert.That(ItemCodec.DecodeAims(publication).Aims, Is.EqualTo(new[] { aim }));
        Assert.Throws<ArgumentException>(() => ItemCodec.EncodeAims(99, 3, 0, [aim, aim]));
        Assert.Throws<ArgumentException>(() => ItemCodec.EncodeAims(99, 3, 0, [aim with { Yaw = float.NaN }]));
    }

    [Test]
    public void RepeatedEvaluationOfTheSameAuthorityTickCannotAdvanceMountAgain()
    {
        var host = Create();
        var slot = host.Items.Slots.Single();
        host.AimItem(0, 99, slot.Life, slot.Token, 0, 1, Vector3.UnitX);
        host.Items.AdvanceAim(host.World, host.Configuration.Configuration.Vehicle, true);
        var before = host.Items.Aims.Single();
        for (int i = 0; i < 50; i++) { host.Items.AdvanceAim(host.World, host.Configuration.Configuration.Vehicle, true); }
        Assert.That(host.Items.Aims.Single(), Is.EqualTo(before));
    }

    [Test]
    public void LoweringLiveElevationLimitsCannotPublishOldOutOfBoundsReadyAngles()
    {
        var host = Create();
        var slot = host.Items.Slots.Single();
        for (ulong i = 1; i < 100; i++)
        {
            host.AimItem(0, 99, slot.Life, slot.Token, 0, i, Vector3.UnitY);
            host.Step(default, Observe);
        }
        Assert.That(host.Items.Aims.Single().Pitch, Is.GreaterThan(1));
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["items.aim_up"] = 0, ["items.aim_down"] = 0 }, out _), Is.True);
        host.Items.AdvanceAim(host.World, host.Configuration.Configuration.Vehicle, true);
        Assert.That(host.Items.Aims.Single().Pitch, Is.Zero);
    }

    [Test]
    public void PerDevicePreferencesValidateAndRoundTripWithoutAffectingSharedRules()
    {
        var settings = new Trackstorm.Core.Settings.PlayerSettings { MouseAimSensitivity = 2.3, StickAimSensitivity = .6, StickAimCurve = 2.4 };
        var restored = Trackstorm.Core.Settings.PlayerSettingsJson.Deserialize(Trackstorm.Core.Settings.PlayerSettingsJson.Serialize(settings));
        Assert.That(restored.MouseAimSensitivity, Is.EqualTo(2.3));
        Assert.That(restored.StickAimSensitivity, Is.EqualTo(.6));
        Assert.That(restored.StickAimCurve, Is.EqualTo(2.4));
        Assert.That((settings with { MouseAimSensitivity = double.NaN }).MouseAimSensitivity, Is.EqualTo(1));
        Assert.That((settings with { StickAimSensitivity = 100 }).StickAimSensitivity, Is.EqualTo(3));
        Assert.That((settings with { StickAimCurve = 0 }).StickAimCurve, Is.EqualTo(1));
    }

    [Test]
    public void ConsumedCapabilityReplacementWithoutSwitchRetractsBeforeDeploying()
    {
        var host = Create(HeldItem.Missile);
        MissileTestPreparation.Wait(host);
        host.Step(default, Observe);
        var old = host.Items.Slots.Single();
        Assert.That(host.Items.RequestUse(host.World, 1, old.Life, old.Token), Is.True);
        host.Step(default, Observe);
        for (int i = 0; i < 4; i++) { host.Step(default, Observe); }
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.MachineGun), Is.True);
        var slot = host.Items.Slots.Single();
        Assert.That(slot.SelectionRevision, Is.Zero);
        host.AimItem(0, 99, slot.Life, slot.Token, 0, 1, -Vector3.UnitZ);
        host.Step(default, Observe);
        Assert.That(host.Items.Aims.Single().Ready, Is.False);
        for (ulong i = 2; i <= 40; i++)
        { host.AimItem(0, 99, slot.Life, slot.Token, 0, i, -Vector3.UnitZ); host.Step(default, Observe); }
        Assert.That(host.Items.Aims.Single().Ready, Is.False, "A single initial-deployment duration cannot complete replacement.");
        for (ulong i = 41; i <= 80; i++)
        { host.AimItem(0, 99, slot.Life, slot.Token, 0, i, -Vector3.UnitZ); host.Step(default, Observe); }
        Assert.That(host.Items.Aims.Single().Ready, Is.True);
    }

    [Test]
    public void LiveMechanicalRetuningPreservesProgressAndDelaysReadiness()
    {
        var host = Create();
        var slot = host.Items.Slots.Single();
        for (ulong i = 1; i <= 10; i++)
        { host.AimItem(0, 99, slot.Life, slot.Token, 0, i, -Vector3.UnitZ); host.Step(default, Observe); }
        Assert.That(host.TryConfigure(0, new Dictionary<string, double>
        { ["vehicle.trunk_deployment_speed"] = .1, ["vehicle.rack_deployment_speed"] = .1 }, out _), Is.True);
        for (ulong i = 11; i <= 70; i++)
        {
            host.AimItem(0, 99, slot.Life, slot.Token, 0, i, -Vector3.UnitZ);
            host.Step(default, Observe);
        }
        Assert.That(host.Items.Aims.Single().Ready, Is.False);
    }

    [Test]
    public void AcceptedOriginUsesTheCurrentObservedPoseAndFailedBatchRollsBackAim()
    {
        var host = Create();
        var slot = host.Items.Slots.Single();
        host.AimItem(0, 99, slot.Life, slot.Token, 0, 1, Vector3.UnitX);
        var pose = new VehiclePhysicsState(new Vector3(40, 8, 10), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1), Vector3.Zero, Vector3.Zero);
        host.Step(default, _ => new VehicleObservation(pose, Vector3.UnitY));
        var accepted = host.Items.Aims.Single();
        Assert.That(accepted.Origin, Is.EqualTo(pose.Position + Vector3.Transform(WeaponAim.Pivot, pose.Orientation)));
        Assert.That(host.Items.RequestUse(host.World, 1, slot.Life, slot.Token), Is.True);
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["items.machine_gun_fire_rate"] = 60 }, out _), Is.True);
        var firing = new Trackstorm.Core.Input.InputFrame(0, 0, 0, 0, Trackstorm.Core.Input.InputButtons.UseItem, 0, 0);
        // Collision throws after aim preparation, inside the uncommitted item batch.
        Assert.Throws<InvalidOperationException>(() => host.Step(firing, Observe, raycastWeapon: (_, _, _) => throw new InvalidOperationException("native ray failed")));
        Assert.That(host.Items.Aims.Single(), Is.EqualTo(accepted));
        host.Step(firing, Observe, raycastWeapon: (_, _, _) => null);
        Assert.That(Math.Abs(host.Items.Aims.Single().Yaw - accepted.Yaw), Is.LessThanOrEqualTo(9 * MathF.PI / 180 + .0001f));
    }

    [Test]
    public void SharedMechanicalPathReversesPartialCyclesWithoutJumping()
    {
        var tuning = new VehicleConfiguration();
        float progress = RackDeploymentPath.Advance(0, true, .1f, tuning);
        float reversing = RackDeploymentPath.Advance(progress, false, 1f / 60, tuning);
        Assert.That(reversing, Is.InRange(progress - .032f, progress));
        float retuned = RackDeploymentPath.Advance(reversing, true, 1f / 60,
            tuning with { TrunkDeploymentSpeed = .1f, RackDeploymentSpeed = .1f });
        Assert.That(retuned, Is.InRange(reversing, reversing + .0011f));
        Assert.That(RackDeploymentPath.Advance(.6f, false, .1f, tuning), Is.EqualTo(.4125f).Within(.00001));
    }

    [Test]
    public void AcceptedMinePlacementAndArmReturnDelayReplacementReadiness()
    {
        var host = Create(HeldItem.ProxyMine);
        host.Step(default, Observe);
        var mine = host.Items.Slots.Single();
        Assert.That(host.Items.RequestUse(host.World, 1, mine.Life, mine.Token), Is.True);
        ProxyMineState Place(ItemSlot slot, VehiclePhysicsState pose) => new(slot.Token, slot.Vehicle,
            pose.Position + Vector3.UnitZ * 3, Vector3.Zero, Vector3.UnitY, 30);
        host.Step(default, Observe, placeMine: Place, moveMine: (_, candidate) => new(candidate));
        Assert.That(host.Items.Mines.Single().IsPlacing, Is.True);
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.MachineGun), Is.True);
        var slot = host.Items.Slots.Single();
        for (ulong i = 1; i <= 90; i++)
        {
            host.AimItem(0, 99, slot.Life, slot.Token, 0, i, -Vector3.UnitZ);
            host.Step(default, Observe, placeMine: Place, moveMine: (_, candidate) => new(candidate));
        }
        Assert.That(host.Items.Aims.Single().Ready, Is.False, "The arm owns the rack before the replacement path starts.");
        for (ulong i = 91; i <= 180; i++)
        {
            host.AimItem(0, 99, slot.Life, slot.Token, 0, i, -Vector3.UnitZ);
            host.Step(default, Observe, placeMine: Place, moveMine: (_, candidate) => new(candidate));
        }
        Assert.That(host.Items.Aims.Single().Ready, Is.True);
    }

    private static HostVehicleSession Create(HeldItem item = HeldItem.MachineGun)
    {
        var host = new HostVehicleSession(99, matchConfiguration: new() { MinimumPlayers = 1, CountdownTicks = 1 });
        host.Step(default, Observe); host.Step(default, Observe);
        host.Items.Grant(host.World, 1, item);
        return host;
    }
    private static VehicleObservation Observe(VehicleSnapshot state) => new(new VehiclePhysicsState(state.ObservedPhysics.Position, Quaternion.Identity, Vector3.Zero, Vector3.Zero), Vector3.UnitY);
}
