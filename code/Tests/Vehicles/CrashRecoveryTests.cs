using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

[TestFixture]
internal sealed class CrashRecoveryTests
{
    [Test]
    public void SeparateRoofImpactsRearmInsideCooldownAndSurviveRestoration()
    {
        var world = new Trackstorm.Core.Simulation.Simulation(new(60));
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI), Vector3.Zero, Vector3.Zero);
        world.AddVehicle(1, new(), new() { MaxHP = 1000, CollisionScale = 5 }, pose);
        var contact = new VehicleContact(-Vector3.UnitY * 12, Vector3.UnitY, 0, 0, true, Vector3.UnitY);
        VehicleSnapshot Step(params VehicleContact[] contacts)
        {
            var input = new InputFrame(world.State.Tick + 1, 0, 0, 0, 0, 0, 0);
            world.Step(input, [new VehicleStepRequest(1, input, new VehicleObservation(pose, Vector3.UnitY, contacts, wheels: default(WheelSupport)))]);
            return world.GetVehicle(1);
        }
        var first = Step(contact, contact, contact);
        Assert.That(first.Damage.CurrentHP, Is.EqualTo(960));
        var restored = VehicleNetworkCodec.DecodeSnapshot(VehicleNetworkCodec.EncodeSnapshot(new WorldSnapshot(1, first.Movement.Tick, [new ReplicatedVehicle(first, 0)]))).Vehicles[0].State;
        restored = VehicleSnapshotCodec.Decode(VehicleSnapshotCodec.Encode(restored));
        world.Restore(new SimulationState(restored.Movement.Tick, new(restored.Movement.Tick, 0, 0, 0, 0, 0, 0), [restored]));
        Assert.That(Step(contact).Damage.CurrentHP, Is.EqualTo(960), "same manifold after restore is not a new hit");
        Step();
        Assert.That(Step(contact).Damage.CurrentHP, Is.EqualTo(920));
        Step();
        var third = Step(contact);
        Assert.That(third.Damage.CurrentHP, Is.EqualTo(880));
        Assert.That(third.Damage.LastDamage!.Sequence, Is.EqualTo(3));
        Assert.That(third.Movement.Tick, Is.LessThan(12));
    }

    [TestCase(20)]
    [TestCase(40)]
    [TestCase(65)]
    public void ExtremeTireBottomOutNeverConsumesHealthOrImpactCooldown(float speed)
    {
        var world = new Trackstorm.Core.Simulation.Simulation(new(60));
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, -speed, 0), Vector3.Zero);
        world.AddVehicle(1, new(), new(), pose);
        for (ulong tick = 1; tick <= 180; tick++)
        {
            var input = new InputFrame(tick, 0, 0, 0, 0, 0, 0);
            var contact = new VehicleContact(pose.LinearVelocity, Vector3.UnitY, speed * 3000, 0, true, -Vector3.UnitY);
            world.Step(input, [new VehicleStepRequest(1, input, new VehicleObservation(pose, Vector3.UnitY, [contact], wheels: new(new Vector4(1)), terrainSupport: Vector3.UnitY))]);
        }
        Assert.That(world.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(100));
        Assert.That(world.GetVehicle(1).Damage.LastCollisionTick, Is.Null);
        Assert.That(world.GetVehicle(1).Movement.CrashSeconds, Is.Zero);
    }

    [Test]
    public void SignificantImpactBlocksControlsThroughBouncesButOrdinaryFlightDoesNot()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitX, -1.5f), new(0, 0, -18), new(-3, 0, 0));
        var movement = new VehicleMovement(new(), pose);
        var fresh = new VehicleMovement(new(), pose);
        var contact = new VehicleContact(new(0, -12, -18), Vector3.UnitY, 0, 0, true, new(0, 0, -2));
        var input = new InputFrame(1, short.MaxValue, ushort.MaxValue, 0, InputButtons.AirControl, 0, 0, -32767, 0, 32767);
        var impact = movement.Step(input, pose, Vector3.UnitY, wheels: default(WheelSupport), contacts: [contact]);
        Assert.That(impact.CrashSeconds, Is.GreaterThan(0));
        for (ulong tick = 2; tick <= 90; tick++)
        {
            input = new(tick, short.MaxValue, ushort.MaxValue, 0, InputButtons.AirControl, 0, 0, -32767, 0, 32767);
            var state = movement.Step(input, pose, Vector3.Zero);
            Assert.That(state.Air.Input, Is.EqualTo(Vector3.Zero));
            Assert.That(state.Throttle, Is.Zero);
            Assert.That(state.Physics.AngularVelocity.X, Is.LessThan(0), "recovery preserves incoming flip direction");
        }
        for (ulong tick = 1; tick <= 90; tick++) { fresh.Step(new(tick, short.MaxValue, ushort.MaxValue, 0, InputButtons.AirControl, 0, 0, -32767, 0, 32767), pose, Vector3.Zero); }
        Assert.That(fresh.State.Air.Input.Length(), Is.GreaterThan(1));
        var supported = movement.Step(new(91, short.MaxValue, ushort.MaxValue, 0, 0, 0, 0), pose, Vector3.UnitY, wheels: new(new(0, 0, 0.1f, 0)));
        Assert.That(supported.CrashSeconds, Is.Zero);
        Assert.That(supported.Throttle, Is.GreaterThan(0));
    }

    [Test]
    public void PartialWheelBottomOutIsSafeOutsideOrdinaryAttitudeEnvelope()
    {
        var world = new Trackstorm.Core.Simulation.Simulation(new(60));
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.1f), new(0, -30, 0), Vector3.Zero);
        world.AddVehicle(1, new(), new(), pose);
        var contact = new VehicleContact(pose.LinearVelocity, Vector3.UnitY, 90000, 0, true, new(-0.8f, -0.6f, 0));
        var input = new InputFrame(1, 0, 0, 0, 0, 0, 0);
        world.Step(input, [new VehicleStepRequest(1, input, new VehicleObservation(pose, Vector3.UnitY, [contact], wheels: new(new(0.5f, 0, 0, 0))))]);
        Assert.That(world.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(100));
        Assert.That(world.GetVehicle(1).Movement.CrashSeconds, Is.Zero);
        // A wheel touching behind the truck does not forgive an outboard bumper strike.
        pose = new(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitX, -1.1f), new(0, -15, 0), Vector3.Zero);
        contact = new(pose.LinearVelocity, Vector3.UnitY, 45000, 0, true, new(0, -0.3f, -2.5f));
        input = new(2, 0, 0, 0, 0, 0, 0);
        world.Step(input, [new VehicleStepRequest(1, input, new VehicleObservation(pose, Vector3.UnitY, [contact], wheels: new(new(0, 0, 0.1f, 0))))]);
        Assert.That(world.GetVehicle(1).Damage.CurrentHP, Is.LessThan(100));
    }

    [Test]
    public void TerrainImpulseRetainsForwardRotationWithoutAddingEnergyOrChangingPose()
    {
        var c = new VehicleConfiguration();
        var pose = new VehiclePhysicsState(new(0, 2, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitX, -1.3f), new(0, -10, -32), Vector3.Zero);
        var contact = new VehicleContact(pose.LinearVelocity, Vector3.UnitY, 0, 0, true, new(0, -0.5f, -2));
        var result = TerrainCollision.Resolve(pose, contact, c);
        float inertia = c.Wheelbase * c.Wheelbase / 3;
        Assert.That(result.LinearVelocity.LengthSquared() + inertia * result.AngularVelocity.LengthSquared(), Is.LessThan(pose.LinearVelocity.LengthSquared()));
        Assert.That(result.AngularVelocity.X, Is.LessThan(0));
        Assert.That(result.LinearVelocity.Y, Is.LessThanOrEqualTo(0));
        Assert.That(result.Position, Is.EqualTo(pose.Position));
        Assert.That(result.Orientation, Is.EqualTo(pose.Orientation));
    }

    [Test]
    public void ObliqueBodyImpulsesNeverCreateKineticEnergy()
    {
        var c = new VehicleConfiguration();
        float inertia = c.Wheelbase * c.Wheelbase / 3;
        foreach (float pitch in new[] { -2.6f, -1.3f, 1.3f, 2.6f })
        foreach (float lateral in new[] { -30f, 0f, 30f })
        foreach (float spin in new[] { -6f, 0f, 6f })
        {
            var incoming = new VehiclePhysicsState(Vector3.Zero, Quaternion.CreateFromYawPitchRoll(0.3f, pitch, 0.4f), new(lateral, -12, -30), new(spin, 1, -spin));
            var contact = new VehicleContact(incoming.LinearVelocity, Vector3.UnitY, 0, 0, true, new(1, 0.5f, -2));
            var result = TerrainCollision.Resolve(incoming, contact, c);
            float before = incoming.LinearVelocity.LengthSquared() + inertia * incoming.AngularVelocity.LengthSquared();
            float after = result.LinearVelocity.LengthSquared() + inertia * result.AngularVelocity.LengthSquared();
            Assert.That(after, Is.LessThanOrEqualTo(before + 0.001f));
        }
    }
}
