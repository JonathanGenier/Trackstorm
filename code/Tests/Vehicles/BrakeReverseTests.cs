using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

[TestFixture]
internal sealed class BrakeReverseTests
{
    [TestCase(0.5f)]
    [TestCase(27.78f)]
    [TestCase(44.44f)]
    public void ContinuousBrakeCrossesZeroAndReverses(float speed)
    {
        var body = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -speed), Vector3.Zero);
        var movement = new VehicleMovement(new(), body);
        for (int i = 0; i < 360; i++) { Step(movement, 65535); }
        Assert.That(movement.State.BrakeMode, Is.EqualTo(BrakeMode.Reversing));
        Assert.That(movement.State.Physics.LinearVelocity.Z, Is.GreaterThan(1));
    }

    [Test]
    public void ReleaseTailCannotReverseAndQuickRepressSurvivesSnapshot()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        var movement = new VehicleMovement(new(), pose);
        movement.Restore(new(0, pose, true, false, 0, 0, brakeMode: BrakeMode.Stopping));
        Step(movement, 50000, released: InputButtons.Brake);
        for (int i = 0; i < 3; i++) { Step(movement, 40000); }
        Assert.That(movement.State.Physics.LinearVelocity.Z, Is.Zero);
        Assert.That(movement.State.BrakeMode, Is.EqualTo(BrakeMode.ReleaseTail));
        var replay = new VehicleMovement(new(), pose);
        replay.Restore(VehicleStateCodec.Decode(VehicleStateCodec.Encode(movement.State)));
        Step(movement, 50000, InputButtons.Brake, InputButtons.Brake);
        Step(replay, 50000, InputButtons.Brake, InputButtons.Brake);
        Assert.That(replay.State, Is.EqualTo(movement.State));
        Assert.That(movement.State.Physics.LinearVelocity.Z, Is.GreaterThan(0));
    }

    [TestCase(-0.2f)]
    [TestCase(0.2f)]
    public void ReleasedStopAllowsNewReversePressDespiteSlopeCreep(float creep)
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -creep), Vector3.Zero);
        var movement = new VehicleMovement(new(), pose);
        movement.Restore(new(0, pose, true, false, 0, 0, brakeMode: BrakeMode.Stopping));
        Step(movement, 65535);
        Assert.That(movement.State.BrakeMode, Is.EqualTo(BrakeMode.Stopping).Or.EqualTo(BrakeMode.Reversing));
        Step(movement, 0);
        // Gravity reintroduces creep between release and the next press.
        var afterRelease = movement.State;
        movement.Restore(new(afterRelease.Tick, pose, true, false, 0, 0, brakeMode: afterRelease.BrakeMode));
        Step(movement, 65535, InputButtons.Brake, InputButtons.Brake);
        Assert.That(movement.State.BrakeMode, Is.EqualTo(BrakeMode.Reversing));
        for (int i = 0; i < 60; i++) { Step(movement, 65535); }
        Assert.That(movement.State.Physics.LinearVelocity.Z, Is.GreaterThan(1));
    }

    [Test]
    public void EarlyReleaseAndRepressWhileForwardStillBrakes()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -10), Vector3.Zero);
        var movement = new VehicleMovement(new(), pose);
        Step(movement, 65535);
        Step(movement, 0);
        Step(movement, 65535, InputButtons.Brake, InputButtons.Brake);
        Assert.That(movement.State.BrakeMode, Is.EqualTo(BrakeMode.Stopping));
        Assert.That(movement.State.Physics.LinearVelocity.Z, Is.LessThan(0));
    }

    private static void Step(VehicleMovement movement, ushort brake, InputButtons held = 0, InputButtons pressed = 0, InputButtons released = 0)
    {
        var state = movement.State.Physics;
        var observed = new VehiclePhysicsState(state.Position, Quaternion.Identity, new(state.LinearVelocity.X, 0, state.LinearVelocity.Z), Vector3.Zero);
        movement.Step(new(movement.State.Tick + 1, 0, 0, brake, held, pressed, released), observed, Vector3.UnitY, surface: SurfaceType.Asphalt);
    }

    [Test]
    public void NativeGravityCreepCannotLatchContinuousForwardBrakeAtRest()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -0.063f), Vector3.Zero);
        var movement = new VehicleMovement(new(), pose);
        movement.Restore(new(0, pose, true, false, 0, 0, brakeMode: BrakeMode.Stopping));
        var replay = new VehicleMovement(new(), pose);
        replay.Restore(VehicleStateCodec.Decode(VehicleStateCodec.Encode(movement.State)));
        var input = new InputFrame(1, 0, 0, 65535, InputButtons.Brake, 0, 0);
        movement.Step(input, pose, Vector3.UnitY);
        replay.Step(input, pose, Vector3.UnitY);
        Assert.That(movement.State.BrakeMode, Is.EqualTo(BrakeMode.Reversing));
        Assert.That(movement.State.Physics.LinearVelocity.Z, Is.GreaterThan(0));
        Assert.That(replay.State, Is.EqualTo(movement.State));
    }
}
