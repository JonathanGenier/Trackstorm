using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

/// <summary>Player-controlled slip, split contact and correction continuity.</summary>
[TestFixture]
internal sealed class HandlingRecoveryTests
{
    [Test]
    public void DirtPowerBuildsAndLiftRestoresGripProgressivelyAcrossRestore()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(3, 0, -15), Vector3.Zero);
        var movement = new VehicleMovement(new(), pose);
        for (ulong tick = 1; tick <= 60; tick++)
        {
            movement.Step(new(tick, 0, ushort.MaxValue, 0, 0, 0, 0), pose, Vector3.UnitY, surface: SurfaceType.Dirt);
        }
        float powered = movement.State.PowerSlip;
        Assert.That(powered, Is.InRange(0.15f, 0.22f));
        var restored = new VehicleMovement(new(), pose);
        restored.Restore(VehicleStateCodec.Decode(VehicleStateCodec.Encode(movement.State)));
        for (ulong tick = 61; tick <= 121; tick++)
        {
            float before = movement.State.PowerSlip;
            var input = new InputFrame(tick, 2000, 0, 0, 0, 0, 0);
            movement.Step(input, pose, Vector3.UnitY, surface: SurfaceType.Dirt);
            restored.Step(input, pose, Vector3.UnitY, surface: SurfaceType.Dirt);
            Assert.That(movement.State, Is.EqualTo(restored.State));
            Assert.That(movement.State.PowerSlip, Is.InRange(before * 0.95f, before));
        }
        Assert.That(movement.State.PowerSlip, Is.LessThan(powered * 0.1f));
    }

    [Test]
    public void SplitGrassContactReducesDriveAndProducesMirroredPhysicalTorque()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -10), Vector3.Zero);
        VehicleState Run(SurfaceType left, SurfaceType right)
        {
            var movement = new VehicleMovement(new(), pose);
            var wheels = new WheelSupport(new Vector4(9.81f / 30), left, right, left, right);
            return movement.Step(new(1, 0, ushort.MaxValue, 0, 0, 0, 0), pose, Vector3.UnitY, surface: SurfaceType.Asphalt, wheels: wheels);
        }
        var asphalt = Run(SurfaceType.Asphalt, SurfaceType.Asphalt);
        var leftGrass = Run(SurfaceType.Grass, SurfaceType.Asphalt);
        var rightGrass = Run(SurfaceType.Asphalt, SurfaceType.Grass);
        var grass = Run(SurfaceType.Grass, SurfaceType.Grass);
        Assert.That(leftGrass.LongitudinalAcceleration, Is.LessThan(asphalt.LongitudinalAcceleration).And.GreaterThan(grass.LongitudinalAcceleration));
        Assert.That(leftGrass.Physics.AngularVelocity.Y, Is.GreaterThan(0));
        Assert.That(rightGrass.Physics.AngularVelocity.Y, Is.EqualTo(-leftGrass.Physics.AngularVelocity.Y).Within(0.00001));
        Assert.That(asphalt.Physics.AngularVelocity.Y, Is.Zero);
    }

    [Test]
    public void SmallHighSpeedCorrectionsRemainProportionalAndDoNotReverseYawInstantly()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(3, 0, -30), new(0, 0.3f, 0));
        var movement = new VehicleMovement(new(), pose);
        var first = movement.Step(new(1, 2000, 20000, 0, 0, 0, 0), pose, Vector3.UnitY, surface: SurfaceType.Dirt);
        var second = movement.Step(new(2, -2000, 20000, 0, 0, 0, 0), pose, Vector3.UnitY, surface: SurfaceType.Dirt);
        Assert.That(Math.Abs(first.SteeringAngle), Is.LessThan(0.001f));
        Assert.That(Math.Abs(second.SteeringAngle - first.SteeringAngle), Is.LessThan(0.002f));
        Assert.That(first.Physics.AngularVelocity.Y, Is.GreaterThan(0));
        Assert.That(second.Physics.AngularVelocity.Y, Is.GreaterThan(0));
    }
}
