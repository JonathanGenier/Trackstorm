using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

/// <summary>Deliberate surface differences and traction-bounded stationary holding.</summary>
[TestFixture]
internal sealed class SurfaceBrakingTests
{
    private static readonly SurfaceType[] Surfaces = [SurfaceType.Asphalt, SurfaceType.Dirt, SurfaceType.Grass];

    [TestCase(false)]
    [TestCase(true)]
    public void DeliberateBrakingHasOrderedDecelerationAndLateralPurchase(bool handbrake)
    {
        var states = new List<VehicleState>();
        foreach (var surface in Surfaces)
        {
            var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(5, 0, -20), Vector3.Zero);
            var movement = new VehicleMovement(new(), pose);
            movement.Restore(new(0, pose, true, false, 0, handbrake ? 1 : 0));
            states.Add(movement.Step(new(1, 0, 0, handbrake ? (ushort)0 : ushort.MaxValue,
                handbrake ? InputButtons.Drift : InputButtons.Brake, 0, 0), pose, Vector3.UnitY, surface: surface));
        }
        for (int i = 1; i < states.Count; i++)
        {
            Assert.That(states[i].Physics.LinearVelocity.X, Is.GreaterThan(states[i - 1].Physics.LinearVelocity.X));
            Assert.That(states[i].Physics.LinearVelocity.Z, Is.LessThan(states[i - 1].Physics.LinearVelocity.Z));
        }
    }

    [TestCase(SurfaceType.Asphalt, 20, 0)]
    [TestCase(SurfaceType.Dirt, -20, 0)]
    [TestCase(SurfaceType.Grass, 20, 0)]
    [TestCase(SurfaceType.Asphalt, 20, 90)]
    [TestCase(SurfaceType.Dirt, 20, 90)]
    [TestCase(SurfaceType.Grass, -20, 90)]
    public void HeldHandbrakeStopsCreepOnInclinesAndReleases(SurfaceType surface, float grade, float yaw)
    {
        var slope = Quaternion.CreateFromAxisAngle(Vector3.UnitX, grade * MathF.PI / 180);
        var orientation = Quaternion.Normalize(slope * Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw * MathF.PI / 180));
        var normal = Vector3.Transform(Vector3.UnitY, slope);
        var pose = new VehiclePhysicsState(Vector3.Zero, orientation, Vector3.Transform(new Vector3(0.1f, 0, -0.1f), slope), Vector3.Zero);
        var movement = new VehicleMovement(new(), pose);
        for (ulong tick = 1; tick <= 600; tick++)
        {
            var state = movement.Step(new(tick, short.MaxValue, ushort.MaxValue, 0, InputButtons.Drift, 0, 0), pose, normal, surface: surface);
            var tangent = state.Physics.LinearVelocity - normal * Vector3.Dot(state.Physics.LinearVelocity, normal);
            Assert.That(tangent.Length(), Is.LessThan(0.00001f));
            Assert.That(Vector3.Dot(state.Physics.AngularVelocity, normal), Is.EqualTo(0).Within(0.00001f));
            Assert.That(state.Physics.Position, Is.EqualTo(pose.Position), "Hold never teleports the pose.");
            pose = new(pose.Position + tangent / 60, orientation, tangent, Vector3.Zero);
        }
        var released = movement.Step(new(601, 0, 0, 0, 0, 0, InputButtons.Drift), pose, normal, surface: surface);
        Assert.That((released.Physics.LinearVelocity - normal * Vector3.Dot(released.Physics.LinearVelocity, normal)).Length(), Is.GreaterThan(0.01f));
    }

    [Test]
    public void HoldCannotInventTractionOrSupportOrArrestAMovingSlide()
    {
        var orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.3f);
        var normal = Vector3.Transform(Vector3.UnitY, orientation);
        var pose = new VehiclePhysicsState(Vector3.Zero, orientation, Vector3.Zero, Vector3.Zero);
        foreach (var tuning in new[] { new VehicleConfiguration { AsphaltGrip = 0 }, new VehicleConfiguration { AsphaltBraking = new() { Deceleration = 0 } } })
        {
            var state = new VehicleMovement(tuning, pose).Step(new(1, 0, 0, 0, InputButtons.Drift, 0, 0), pose, normal, surface: SurfaceType.Asphalt);
            Assert.That((state.Physics.LinearVelocity - normal * Vector3.Dot(state.Physics.LinearVelocity, normal)).Length(), Is.GreaterThan(0.01f));
        }
        var airborne = new VehicleMovement(new(), pose).Step(new(1, 0, 0, 0, InputButtons.Drift, 0, 0), pose, Vector3.Zero);
        Assert.That(airborne.Physics.LinearVelocity.Y, Is.LessThan(0));
        var moving = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(8, 0, 0), Vector3.Zero);
        var slide = new VehicleMovement(new(), moving).Step(new(1, 0, 0, 0, InputButtons.Drift, 0, 0), moving, Vector3.UnitY, surface: SurfaceType.Grass);
        Assert.That(slide.Physics.LinearVelocity.X, Is.GreaterThan(7));
    }

    [Test]
    public void NewSurfaceOptionsPersistAndReplicateWithoutLosingSiblingTuning()
    {
        var defaults = GameplayConfiguration.HostedDefaults;
        foreach (var option in GameplayOptions.All.TakeLast(12))
        {
            Assert.That(GameplayOptions.TryApply(defaults, new Dictionary<string, double> { [option.Key] = option.Read(defaults) * 0.75 }, out var changed, out var error), Is.True, error);
            Assert.That(GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(1, new(1, changed))).State.Configuration, Is.EqualTo(changed));
            Assert.That(DeveloperSettingsFile.Read(DeveloperSettingsFile.Read(string.Empty).Write(changed)).Configuration, Is.EqualTo(changed));
            Assert.That(changed.Vehicle.HighSpeedSteeringScale, Is.EqualTo(defaults.Vehicle.HighSpeedSteeringScale));
            Assert.That(changed.Vehicle.Mud, Is.EqualTo(defaults.Vehicle.Mud));
            foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity, 101d })
                Assert.That(GameplayOptions.TryApply(defaults, new Dictionary<string, double> { [option.Key] = invalid }, out _, out _), Is.False);
        }
        byte[] obsolete = GameplayConfigurationCodec.Encode(1, new(0, defaults));
        obsolete[2] = 36;
        Assert.Throws<ArgumentException>(() => GameplayConfigurationCodec.Decode(obsolete));
    }

    [Test]
    public void BrakeReleaseTuningDoesNotMakeNormalDrivingSlippery()
    {
        var baseline = new VehicleConfiguration();
        var loose = baseline with { GrassBraking = new() { Deceleration = 0.1f, BrakeLateralGrip = 0, HandbrakeLateralGrip = 0 } };
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(2, 0, -18), Vector3.Zero);
        var input = new InputFrame(1, 5000, 40000, 0, 0, 0, 0);
        Assert.That(new VehicleMovement(loose, pose).Step(input, pose, Vector3.UnitY, surface: SurfaceType.Grass),
            Is.EqualTo(new VehicleMovement(baseline, pose).Step(input, pose, Vector3.UnitY, surface: SurfaceType.Grass)));
    }

    [Test]
    public void SplitSurfaceBrakingUsesWheelMaterialsRatherThanCenterIdentity()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -20), Vector3.Zero);
        VehicleState Step(bool leftGrass, SurfaceType center)
        {
            var left = leftGrass ? SurfaceType.Grass : SurfaceType.Asphalt;
            var right = leftGrass ? SurfaceType.Asphalt : SurfaceType.Grass;
            return new VehicleMovement(new(), pose).Step(new(1, 0, 0, ushort.MaxValue, InputButtons.Brake, 0, 0), pose, Vector3.UnitY,
                surface: center, wheels: new WheelSupport(new Vector4(.5f), left, right, left, right));
        }
        var left = Step(true, SurfaceType.Asphalt);
        var right = Step(false, SurfaceType.Asphalt);
        Assert.That(Math.Abs(left.Physics.AngularVelocity.Y), Is.GreaterThan(.001f));
        Assert.That(left.Physics.AngularVelocity.Y, Is.EqualTo(-right.Physics.AngularVelocity.Y).Within(.00001f));
        Assert.That(Step(true, SurfaceType.Grass).Physics, Is.EqualTo(left.Physics));
    }

    [Test]
    public void SurfaceTransitionsAndHoldRemainDeterministicAcrossRestoration()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(1, 0, -15), Vector3.Zero);
        var original = new VehicleMovement(new(), pose);
        var restored = new VehicleMovement(new(), pose);
        for (ulong tick = 1; tick <= 360; tick++)
        {
            var input = new InputFrame(tick, tick % 60 < 30 ? (short)9000 : (short)-9000, tick % 90 < 45 ? ushort.MaxValue : (ushort)0,
                tick % 90 >= 75 ? ushort.MaxValue : (ushort)0, tick % 90 is >= 45 and < 75 ? InputButtons.Drift : 0, 0, 0);
            var surface = Surfaces[(int)(tick / 30 % 3)];
            var state = original.Step(input, pose, Vector3.UnitY, surface: surface);
            Assert.That(restored.Step(input, pose, Vector3.UnitY, surface: surface), Is.EqualTo(state));
            restored.Restore(VehicleStateCodec.Decode(VehicleStateCodec.Encode(state)));
            var velocity = state.Physics.LinearVelocity with { Y = 0 };
            pose = new(pose.Position + velocity / 60, pose.Orientation, velocity, state.Physics.AngularVelocity);
        }
    }

    [Test]
    public void DirtCornerBudgetDoesNotLeakIntoWaterImmersion()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(2, 0, -10), Vector3.Zero);
        var input = new InputFrame(1, 20000, ushort.MaxValue, 0, 0, 0, 0);
        var wheels = new WheelSupport(new Vector4(.5f), SurfaceType.Dirt, SurfaceType.Dirt, SurfaceType.Dirt, SurfaceType.Dirt);
        var baseline = new VehicleMovement(new() { DirtCornerGrip = 0 }, pose).Step(input, pose, Vector3.UnitY, surface: SurfaceType.Dirt, wheels: wheels, waterDepth: 1);
        var boosted = new VehicleMovement(new() { DirtCornerGrip = 4 }, pose).Step(input, pose, Vector3.UnitY, surface: SurfaceType.Dirt, wheels: wheels, waterDepth: 1);
        Assert.That(boosted, Is.EqualTo(baseline));
    }

    [Test]
    public void CenteredGrassSteeringRestoresPassiveFrontLateralCorrection()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(6, 0, -30), Vector3.Zero);
        var input = new InputFrame(1, 0, ushort.MaxValue, 0, 0, 0, 0);
        var passive = new VehicleMovement(new() { GrassSteeringReserve = 0 }, pose).Step(input, pose, Vector3.UnitY, surface: SurfaceType.Grass);
        var assisted = new VehicleMovement(new() { GrassSteeringReserve = 1 }, pose).Step(input, pose, Vector3.UnitY, surface: SurfaceType.Grass);
        Assert.That(assisted, Is.EqualTo(passive));
        Assert.That(assisted.Physics.LinearVelocity.X, Is.LessThan(pose.LinearVelocity.X));
    }
}
