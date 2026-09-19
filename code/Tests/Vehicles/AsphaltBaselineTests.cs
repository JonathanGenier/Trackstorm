using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

/// <summary>Production powered limits, bank-relative contact forces and progressive handbrake regression checks.</summary>
[TestFixture]
internal sealed class AsphaltBaselineTests
{
    /// <summary>Ordinary defaults reach the powered cap smoothly; external overspeed remains physical.</summary>
    [Test]
    public void ProductionDriveApproaches160WithoutClampingExternalSpeed()
    {
        var tuning = GameplayConfiguration.HostedDefaults.Vehicle;
        Assert.That(tuning, Is.EqualTo(new VehicleConfiguration()));
        Assert.That(tuning.ForwardSpeed, Is.EqualTo(44.44f));
        var body = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        var movement = new VehicleMovement(tuning, body);
        float previous = 0;
        float accelerationAtThirty = 0;
        float accelerationAtFortyThree = 0;
        for (ulong tick = 1; tick <= 2400; tick++)
        {
            body = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(0, 0, -previous), Vector3.Zero);
            var state = movement.Step(new InputFrame(tick, 0, ushort.MaxValue, 0, 0, 0, 0), body, Vector3.UnitY);
            float speed = -state.Physics.LinearVelocity.Z;
            Assert.That(speed, Is.InRange(previous, tuning.ForwardSpeed));
            Assert.That(speed - previous, Is.LessThanOrEqualTo(tuning.Acceleration / 60));
            if (previous < 30)
            {
                accelerationAtThirty = state.LongitudinalAcceleration;
            }

            if (previous < 43)
            {
                accelerationAtFortyThree = state.LongitudinalAcceleration;
            }

            previous = speed;
        }

        Assert.That(previous, Is.EqualTo(tuning.ForwardSpeed).Within(0.001f));
        Assert.That(accelerationAtFortyThree, Is.LessThan(accelerationAtThirty / 2));
        body = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(0, 0, -50), Vector3.Zero);
        var overspeed = movement.Step(new InputFrame(2401, 0, ushort.MaxValue, 0, 0, 0, 0), body, Vector3.UnitY);
        Assert.That(overspeed.Physics.LinearVelocity.Z, Is.EqualTo(-50));
        Assert.That(overspeed.LongitudinalAcceleration, Is.Zero);
    }

    /// <summary>Banked spring equilibrium cancels only normal gravity, retaining its downhill component.</summary>
    [Test]
    public void BankSupportDoesNotCancelDownhillGravity()
    {
        var tuning = new VehicleConfiguration();
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 35 * MathF.PI / 180);
        var normal = Vector3.Transform(Vector3.UnitY, rotation);
        var body = new VehiclePhysicsState(Vector3.Zero, rotation, Vector3.Zero, Vector3.Zero);
        var wheels = new WheelSupport(new Vector4(tuning.Gravity * normal.Y / tuning.WheelSpring));
        var state = new VehicleMovement(tuning, body).Step(new InputFrame(1, 0, 0, 0, 0, 0, 0), body, normal, wheels: wheels);
        Vector3 gravity = -Vector3.UnitY * tuning.Gravity;
        Vector3 downhill = gravity - (normal * Vector3.Dot(gravity, normal));
        Assert.That(Vector3.Distance(state.Physics.LinearVelocity, downhill / 60), Is.LessThan(0.00001f));
        Assert.That(state.Physics.AngularVelocity.Length(), Is.LessThan(0.00001f));
        Assert.That(state.Physics.LinearVelocity.Length(), Is.GreaterThan(0.09f));
    }

    /// <summary>Yaw, tire forces and damping rotate with the road instead of changing with world axes.</summary>
    [Test]
    public void TireAndSuspensionResponseRotateWithTheBank()
    {
        var tuning = new VehicleConfiguration();
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 35 * MathF.PI / 180);
        var flat = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(2, 0, -30), new Vector3(0, 0.3f, 0));
        var bank = new VehiclePhysicsState(Vector3.Zero, rotation, Vector3.Transform(flat.LinearVelocity, rotation), Vector3.Transform(flat.AngularVelocity, rotation));
        var input = new InputFrame(1, 8000, ushort.MaxValue, 0, InputButtons.Drift, 0, 0);
        var wheels = new WheelSupport(new Vector4(0.1f));
        var a = new VehicleMovement(tuning, flat).Step(input, flat, Vector3.UnitY, wheels: wheels);
        var b = new VehicleMovement(tuning, bank).Step(input, bank, Vector3.Transform(Vector3.UnitY, rotation), wheels: wheels);
        var gravityStep = -Vector3.UnitY * tuning.Gravity / 60;
        Assert.That(Vector3.Distance(b.Physics.LinearVelocity - gravityStep, Vector3.Transform(a.Physics.LinearVelocity - gravityStep, rotation)), Is.LessThan(0.00001f));
        Assert.That(Vector3.Distance(b.Physics.AngularVelocity, Vector3.Transform(a.Physics.AngularVelocity, rotation)), Is.LessThan(0.00001f));
    }

    /// <summary>A short pulse consumes progressively more rear grip; release restores it over multiple ticks.</summary>
    [Test]
    public void ShortHandbrakePulseAndReleaseRemainProgressive()
    {
        var tuning = GameplayConfiguration.HostedDefaults.Vehicle;
        var body = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(4, 0, -20), Vector3.Zero);
        var movement = new VehicleMovement(tuning, body);
        float previous = 0;
        for (ulong tick = 1; tick <= 6; tick++)
        {
            var state = movement.Step(new InputFrame(tick, 0, 0, 0, InputButtons.Drift, 0, 0), body, Vector3.UnitY);
            Assert.That(state.Handbrake, Is.GreaterThan(previous).And.LessThan(0.5f));
            previous = state.Handbrake;
        }

        float heldSlip = movement.State.RearSlip;
        var released = movement.Step(new InputFrame(7, 0, ushort.MaxValue, 0, 0, 0, 0), body, Vector3.UnitY);
        Assert.That(released.Handbrake, Is.GreaterThan(0).And.LessThan(previous));
        Assert.That(released.LongitudinalAcceleration, Is.GreaterThan(0));
        for (ulong tick = 8; tick <= 30; tick++)
        {
            movement.Step(new InputFrame(tick, 0, 0, 0, 0, 0, 0), body, Vector3.UnitY);
        }

        Assert.That(movement.State.Handbrake, Is.Zero);
        Assert.That(movement.State.RearSlip, Is.LessThan(heldSlip));
    }
}
