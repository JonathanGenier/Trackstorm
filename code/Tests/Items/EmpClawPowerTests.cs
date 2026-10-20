using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class EmpClawPowerTests
{
    [TestCase(0f, false)]
    [TestCase(5f, false)]
    [TestCase(14f, false)]
    [TestCase(25f, false)]
    [TestCase(0f, true)]
    [TestCase(-5f, true)]
    public void ProductionTiresDeliverTwentyPercentPropulsion(float speed, bool reverse)
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -speed), Vector3.Zero);
        VehicleState Sample(bool emp)
        {
            var movement = new VehicleMovement(new(), pose);
            movement.Restore(new(0, pose, true, false, 0, 0, throttle: 1));
            return movement.Step(new(1, 0, reverse ? (ushort)0 : ushort.MaxValue,
                reverse ? ushort.MaxValue : (ushort)0, 0, 0, 0), pose, Vector3.UnitY, empUntilTick: emp ? 1800UL : 0);
        }
        var plain = Sample(false); var emp = Sample(true);
        float normal = plain.LongitudinalAcceleration, affected = emp.LongitudinalAcceleration;
        TestContext.Out.WriteLine($"Production propulsion at {speed} m/s, reverse={reverse}: normal={normal}, EMP={affected}, ratio={affected / normal}");
        Assert.That(affected, Is.EqualTo(normal * .2f).Within(.0002));
        Assert.That(emp.Physics.LinearVelocity.Z - pose.LinearVelocity.Z,
            Is.EqualTo((plain.Physics.LinearVelocity.Z - pose.LinearVelocity.Z) * .2f).Within(.00001));
    }

    private static VehicleState Drive(bool emp, ushort throttle, ushort brake, NitroState nitro, float speed = 0, bool grounded = true)
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -speed), Vector3.Zero);
        var movement = new VehicleMovement(new() { Acceleration = 1, ReverseAcceleration = 1 }, pose);
        return movement.Step(new InputFrame(1, 0, throttle, brake, InputButtons.UseItem, 0, 0), pose,
            grounded ? Vector3.UnitY : Vector3.Zero, nitro: nitro, empUntilTick: emp ? 1800UL : 0);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EngineAndReverseKeepTwentyPercentPower(bool reverse)
    {
        var plain = Drive(false, reverse ? (ushort)0 : ushort.MaxValue, reverse ? ushort.MaxValue : (ushort)0, default);
        var emp = Drive(true, reverse ? (ushort)0 : ushort.MaxValue, reverse ? ushort.MaxValue : (ushort)0, default);
        Assert.That(emp.Physics.LinearVelocity.Z / plain.Physics.LinearVelocity.Z, Is.EqualTo(.2f).Within(.002));
        Assert.That(emp.Throttle, Is.EqualTo(plain.Throttle));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NitroCannotBypassSuppressionOnGroundOrInAir(bool grounded)
    {
        var nitro = new NitroState(60, 18000, 1.4f, 1);
        var plain = Drive(false, 0, 0, nitro, grounded: grounded);
        var emp = Drive(true, 0, 0, nitro, grounded: grounded);
        Assert.That(emp.Physics.LinearVelocity.Z, Is.EqualTo(plain.Physics.LinearVelocity.Z * .2f).Within(.00001));
        Assert.That(emp.Nitro, Is.EqualTo(plain.Nitro));
        TestContext.Out.WriteLine($"NOS grounded={grounded}: normal={-plain.Physics.LinearVelocity.Z * 60} m/s^2, EMP={-emp.Physics.LinearVelocity.Z * 60} m/s^2");
    }

    [TestCase(false, 10f)]
    [TestCase(true, 10f)]
    [TestCase(false, 50f)]
    [TestCase(true, 50f)]
    public void BrakingAndCoastingAreUnchangedAndPredictionExpiresAtTheExactTick(bool braking, float speed)
    {
        var plain = Drive(false, 0, braking ? ushort.MaxValue : (ushort)0, default, speed);
        var emp = Drive(true, 0, braking ? ushort.MaxValue : (ushort)0, default, speed);
        Assert.That(emp.Physics, Is.EqualTo(plain.Physics));
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        var movement = new VehicleMovement(new(), pose);
        movement.Restore(VehicleStateCodec.Decode(VehicleStateCodec.Encode(new(100, pose, false, false, 0, 0, empUntilTick: 102))));
        Assert.That(movement.Step(new(101, 0, 0, 0, 0, 0, 0), pose, Vector3.UnitY).EnginePowerScale, Is.EqualTo(.2f));
        Assert.That(movement.Step(new(102, 0, 0, 0, 0, 0, 0), pose, Vector3.UnitY).EnginePowerScale, Is.EqualTo(1));
    }
}
