using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

[TestFixture]
internal sealed class EnvironmentCollisionTests
{
    [Test]
    public void ManifoldDuplicatesDoNotMultiplyDragOrTorque()
    {
        var incoming = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(-2, 0, -45), Vector3.Zero);
        var contact = new VehicleContact(incoming.LinearVelocity, Vector3.UnitX, 50000, 0, localPosition: new(1, 0, -2), staticObstacle: true);
        var one = EnvironmentCollision.Resolve(incoming, Vector3.UnitY, [contact], new());
        var many = EnvironmentCollision.Resolve(incoming, Vector3.UnitY, Enumerable.Repeat(contact, 16).ToArray(), new());
        Assert.That(many, Is.EqualTo(one));
        Assert.That(one.LinearVelocity.X, Is.Zero);
        Assert.That(one.LinearVelocity.Z, Is.InRange(-45f, -44f));
        Assert.That(one.AngularVelocity, Is.EqualTo(Vector3.Zero));
        Assert.That(EnvironmentCollision.Severity(incoming.LinearVelocity, Vector3.UnitX), Is.Zero);
    }

    [Test]
    public void DirectCrashStopsWithoutReboundAndEccentricCrashRetainsBoundedTorque()
    {
        var incoming = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -45), Vector3.Zero);
        var centered = new VehicleContact(incoming.LinearVelocity, Vector3.UnitZ, 0, 0, localPosition: new(0, 0, -2), staticObstacle: true);
        var resolved = EnvironmentCollision.Resolve(incoming, Vector3.UnitY, [centered], new());
        Assert.That(resolved.LinearVelocity, Is.EqualTo(Vector3.Zero));
        Assert.That(resolved.AngularVelocity, Is.EqualTo(Vector3.Zero));
        var eccentric = new VehicleContact(incoming.LinearVelocity, Vector3.UnitZ, 0, 0, localPosition: new(1, 0, -2), staticObstacle: true);
        resolved = EnvironmentCollision.Resolve(incoming, Vector3.UnitY, [eccentric], new());
        Assert.That(resolved.AngularVelocity.Length(), Is.InRange(0.1f, 1.2f));
    }

    [Test]
    public void RepeatedScrapeDissipatesWithoutLiftAndPreservesBankSupportVelocity()
    {
        Vector3 support = Vector3.Normalize(new Vector3(0, 1, 0.25f));
        Vector3 velocity = Vector3.Cross(Vector3.UnitX, support) * 45 - Vector3.UnitX;
        var state = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, velocity, Vector3.Zero);
        float previous = velocity.Length();
        for (int tick = 0; tick < 240; tick++)
        {
            var contact = new VehicleContact(state.LinearVelocity, Vector3.Normalize(new(1, 0.15f, 0)), 90000, 0, staticObstacle: true);
            state = EnvironmentCollision.Resolve(state, support, [contact], new());
            Assert.That(state.LinearVelocity.Length(), Is.LessThanOrEqualTo(previous + 0.00001f));
            Assert.That(Vector3.Dot(state.LinearVelocity, support), Is.EqualTo(0).Within(0.0001));
            Assert.That(state.AngularVelocity, Is.EqualTo(Vector3.Zero));
            previous = state.LinearVelocity.Length();
        }
    }

    [Test]
    public void DirectFaceAfterBevelCannotCreateLargeSidewaysExit()
    {
        var incoming = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(35, 0, 0), Vector3.Zero);
        var bevel = new VehicleContact(incoming.LinearVelocity, Vector3.Normalize(new(-1, 0, 1)), 0, 0, staticObstacle: true);
        var face = new VehicleContact(incoming.LinearVelocity, -Vector3.UnitX, 0, 0, staticObstacle: true);
        Assert.That(EnvironmentCollision.Resolve(incoming, Vector3.UnitY, [bevel, face], new()).LinearVelocity.Length(), Is.LessThan(1));
    }

    [Test]
    public void ObliqueSeverityIsContinuousAndUsesExistingAuthoritativeDamageGate()
    {
        float previous = 0;
        for (int angle = 0; angle <= 90; angle++)
        {
            float radians = angle * MathF.PI / 180;
            Vector3 velocity = new(-45 * MathF.Sin(radians), 0, -45 * MathF.Cos(radians));
            float severity = EnvironmentCollision.Severity(velocity, Vector3.UnitX);
            Assert.That(severity, Is.InRange(previous, previous + 2));
            previous = severity;
        }
        var world = new Core.Simulation.Simulation(new Core.Simulation.SimulationConfiguration(60));
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        world.AddVehicle(1, new(), new(), pose);
        for (ulong tick = 1; tick <= 30; tick++)
        {
            Vector3 velocity = tick < 20 ? new(-2, 0, -60) : new(-30, 0, 0);
            var contact = new VehicleContact(velocity, Vector3.UnitX, 100000, 0, staticObstacle: true);
            var input = new InputFrame(tick, 0, 0, 0, 0, 0, 0);
            world.Step(input, [new VehicleStepRequest(1, input, new VehicleObservation(pose, Vector3.UnitY, [contact]))]);
            if (tick < 20) { Assert.That(world.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(100)); }
        }
        Assert.That(world.GetVehicle(1).Damage.LastDamage!.Tick, Is.EqualTo(20));
        Assert.That(world.GetVehicle(1).Damage.CurrentHP, Is.LessThan(100));
    }
}
