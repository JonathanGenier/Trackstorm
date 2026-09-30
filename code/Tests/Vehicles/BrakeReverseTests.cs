using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

[TestFixture]
internal sealed class BrakeReverseTests
{
    [TestCase(0.001f)]
    [TestCase(0.04f)]
    [TestCase(0.5f)]
    [TestCase(27.78f)]
    [TestCase(44.44f)]
    public void ContinuousBrakeStopsAndHoldsThenNewPressReverses(float speed)
    {
        var body = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -speed), Vector3.Zero);
        var movement = new VehicleMovement(new(), body);
        for (int i = 0; i < 360; i++) { Step(movement, 65535); }
        Assert.That(movement.State.BrakeMode, Is.EqualTo(BrakeMode.Stopping));
        Assert.That(movement.State.Physics.LinearVelocity.Z, Is.EqualTo(0).Within(0.001));
        Step(movement, 0);
        for (int i = 0; i < 60; i++) { Step(movement, 32767); }
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
}
