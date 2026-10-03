using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

[TestFixture]
internal sealed class RockPressureTests
{
    [Test]
    public void InclinedRockContactDoesNotTurnGravityIntoAnArtificialSupportForce()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        var support = Vector3.Normalize(new Vector3(0, 1, -0.6f));
        var side = new VehicleContact(Vector3.Zero, Vector3.Normalize(new(0, 0.4f, 1)), 0, 0, staticObstacle: true, environmentRock: 1);
        var state = new VehicleMovement(new(), pose).Step(new(1, 0, 0, 0, 0, 0, 0), pose, support, contacts: [side]);
        Assert.That(state.Physics.LinearVelocity, Is.EqualTo(-Vector3.UnitY * (new VehicleConfiguration().Gravity / 60)));
    }

    [Test]
    public void AlreadySolvedRockMomentumIsPreservedWhenNoNewDrivePushesInward()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 1, -3), Vector3.Zero);
        var side = new VehicleContact(Vector3.Zero, Vector3.Normalize(new(0, 0.4f, 1)), 0, 0, staticObstacle: true, environmentRock: 1);
        var input = new InputFrame(1, 0, 0, 0, 0, 0, 0);
        var actual = new VehicleMovement(new(), pose).Step(input, pose, Vector3.UnitY, contacts: [side]);
        var ordinary = new VehicleMovement(new(), pose).Step(input, pose, Vector3.UnitY);
        Assert.That(actual.Physics, Is.EqualTo(ordinary.Physics));
    }

    [Test]
    public void SustainedPressureKeepsContactWithoutResettingThrottleOrInventingSlip()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        var blocked = new VehicleMovement(new(), pose);
        var free = new VehicleMovement(new(), pose);
        var side = new VehicleContact(Vector3.Zero, Vector3.Normalize(new(0, 0.4f, 1)), 0, 0, staticObstacle: true, environmentRock: 1);
        for (ulong tick = 1; tick <= 600; tick++)
        {
            var input = new InputFrame(tick, 0, ushort.MaxValue, 0, 0, 0, 0);
            var actual = blocked.Step(input, pose, Vector3.UnitY, contacts: [side]);
            var ordinary = free.Step(input, pose, Vector3.UnitY);
            Assert.That(actual.Physics.LinearVelocity.Z, Is.EqualTo(0).Within(0.000001));
            Assert.That(actual.Physics.LinearVelocity.Y, Is.EqualTo(ordinary.Physics.LinearVelocity.Y));
            Assert.That(actual.Throttle, Is.EqualTo(ordinary.Throttle));
            Assert.That(actual.RearSlip, Is.EqualTo(ordinary.RearSlip));
            Assert.That(actual.FrontSlip, Is.EqualTo(ordinary.FrontSlip));
            Assert.That(actual.PowerSlip, Is.EqualTo(ordinary.PowerSlip));
        }
        Assert.That(blocked.State.Throttle, Is.GreaterThan(0.99f));
        var resume = new InputFrame(601, 0, ushort.MaxValue, 0, 0, 0, 0);
        Assert.That(blocked.Step(resume, pose, Vector3.UnitY).Physics.LinearVelocity.Z, Is.LessThan(-0.1f),
            "Separation or destruction releases the constraint on the very next tick.");
    }

    [Test]
    public void ReverseAndTangentialEscapeAreImmediate()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(2, 0, 0), Vector3.Zero);
        var side = new VehicleContact(Vector3.Zero, Vector3.Normalize(new(0, 0.4f, 1)), 0, 0, staticObstacle: true, environmentRock: 1);
        var input = new InputFrame(1, short.MaxValue, 0, ushort.MaxValue, InputButtons.Brake, InputButtons.Brake, 0);
        var actual = new VehicleMovement(new(), pose).Step(input, pose, Vector3.UnitY, contacts: [side]);
        var ordinary = new VehicleMovement(new(), pose).Step(input, pose, Vector3.UnitY);
        Assert.That(actual.Physics, Is.EqualTo(ordinary.Physics));
        Assert.That(actual.Physics.LinearVelocity.Z, Is.GreaterThan(0));
        Assert.That(actual.Physics.LinearVelocity.X, Is.GreaterThan(0));
        Assert.That(actual.SteeringAngle, Is.GreaterThan(0));
    }

    [TestCase(0f)]
    [TestCase(15f)]
    public void SupportedRockClimbRetainsMomentumAndNormalSuspension(float speed)
    {
        var support = Vector3.Normalize(new Vector3(0, 1, 0.3f));
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, speed * 0.3f, -speed), Vector3.Zero);
        var top = new VehicleContact(Vector3.Zero, support, 0, 0, environmentRock: 1);
        var input = new InputFrame(1, 0, ushort.MaxValue, 0, 0, 0, 0);
        var wheels = new WheelSupport(new Vector4(0.3f));
        var actual = new VehicleMovement(new(), pose).Step(input, pose, support, wheels: wheels, contacts: [top]);
        var ordinary = new VehicleMovement(new(), pose).Step(input, pose, support, wheels: wheels);
        Assert.That(actual.Physics, Is.EqualTo(ordinary.Physics));
    }

    [Test]
    public void OtherObstacleAndVehicleContactsRetainExistingBehavior()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        var input = new InputFrame(1, 0, ushort.MaxValue, 0, 0, 0, 0);
        var contacts = new[] {
            new VehicleContact(Vector3.Zero, Vector3.UnitZ, 0, 0, staticObstacle: true),
            new VehicleContact(Vector3.Zero, Vector3.UnitZ, 0, 2)
        };
        var actual = new VehicleMovement(new(), pose).Step(input, pose, Vector3.UnitY, contacts: contacts);
        var ordinary = new VehicleMovement(new(), pose).Step(input, pose, Vector3.UnitY);
        Assert.That(actual.Physics, Is.EqualTo(ordinary.Physics));
    }
}
