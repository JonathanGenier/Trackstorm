using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

[TestFixture]
internal sealed class AirControlTests
{
    [Test]
    public void EveryTakeoffAcceptsInputOnItsFirstTickAndTireContactClearsContinuation()
    {
        var movement = Create();
        for (int jump = 0; jump < 4; jump++)
        {
            for (int tick = 1; tick <= 9; tick++)
            {
                Step(movement, throttle: 65535);
                Assert.That(movement.State.Air.Seconds, Is.EqualTo(tick / 60f).Within(0.000001));
                Assert.That(movement.State.Air.Input.X, Is.GreaterThan(0));
                Assert.That(movement.State.Physics.AngularVelocity.X, Is.GreaterThan(0));
            }
            var observed = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
            movement.Step(new(movement.State.Tick + 1, 0, 0, 0, 0, 0, 0, 32767), observed, Vector3.UnitY,
                wheels: new WheelSupport(new Vector4(0.1f, 0, 0, 0)));
            Assert.That(movement.State.Air, Is.EqualTo(default(AirControlState)));
            // Isolate each new flight from physical landing torque.
            movement.Restore(new VehicleState(movement.State.Tick, observed, true, false, 0, 0));
        }
    }

    [Test]
    public void ShortGapsAcceptInputButDisabledDrivingCannotRotate()
    {
        var movement = Create();
        for (int gap = 0; gap < 20; gap++)
        {
            for (int tick = 0; tick < 8; tick++) { Step(movement, throttle: 65535); }
            Assert.That(movement.State.Air.Input.X, Is.GreaterThan(0));
            Step(movement, grounded: true);
            Assert.That(movement.State.Air, Is.EqualTo(default(AirControlState)));
        }
        for (int tick = 0; tick < 30; tick++)
        {
            movement.Step(new(movement.State.Tick + 1, 0, 0, 0, 0, 0, 0, 32767), movement.State.Physics, Vector3.Zero, driveEnabled: false);
            Assert.That(movement.State.Air.Input, Is.EqualTo(Vector3.Zero));
        }
    }

    [Test]
    public void AerialAxesAndRollModifierDoNotDisturbGroundDrivingOnAnySurface()
    {
        foreach (var surface in new[] { SurfaceType.Asphalt, SurfaceType.Dirt, SurfaceType.Grass })
        {
            var ordinary = Create(); var armed = Create();
            for (ulong tick = 1; tick <= 120; tick++)
            {
                ordinary.Step(new(tick, 10000, 40000, 0, 0, 0, 0), ordinary.State.Physics, Vector3.UnitY, surface: surface);
                armed.Step(new(tick, 10000, 40000, 0, InputButtons.AirRoll, 0, 0, 32767, 32767, 32767), armed.State.Physics, Vector3.UnitY, surface: surface);
                Assert.That(armed.State, Is.EqualTo(ordinary.State));
            }
        }
    }

    [TestCase(65535, 0, 0, false, 1, 0, 0)]
    [TestCase(0, 65535, 0, false, -1, 0, 0)]
    [TestCase(0, 0, -32767, false, 0, 1, 0)]
    [TestCase(0, 0, 32767, false, 0, -1, 0)]
    [TestCase(0, 0, -32767, true, 0, 0, 1)]
    [TestCase(65535, 0, 32767, true, 1, 0, -1)]
    public void CommandsUseChassisAxesAndSustainRotation(int throttle, int brake, int steering, bool roll, int x, int y, int z)
    {
        Quaternion pose = Quaternion.CreateFromYawPitchRoll(0.8f, 0.7f, 1.2f);
        var movement = Create(pose);
        for (int i = 0; i < 240; i++) { Step(movement, (ushort)throttle, (ushort)brake, (short)steering, roll); }
        Vector3 local = Vector3.Transform(movement.State.Physics.AngularVelocity, Quaternion.Conjugate(pose));
        Assert.That(local.X, Is.EqualTo(x * movement.Configuration.AirPitchRate).Within(0.001));
        Assert.That(local.Y, Is.EqualTo(y * movement.Configuration.AirYawRate).Within(0.001));
        Assert.That(local.Z, Is.EqualTo(z * movement.Configuration.AirRollRate).Within(0.001));
    }

    [Test]
    public void ReleasePreservesSpinWithoutSeekingWorldUp()
    {
        Quaternion inverted = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 2.4f);
        var movement = Create(inverted);
        for (int i = 0; i < 90; i++) { Step(movement, steering: 32767, roll: true); }
        Vector3 spin = movement.State.Physics.AngularVelocity;
        for (int i = 0; i < 90; i++) { Step(movement); }
        Assert.That(movement.State.Physics.AngularVelocity, Is.EqualTo(spin));
        Assert.That(movement.State.Physics.Orientation, Is.EqualTo(inverted));
    }

    [TestCase(4)]
    [TestCase(8)]
    [TestCase(9)]
    public void RestoreAndRetunePreserveImmediateInputContinuation(int savedTicks)
    {
        var source = Create();
        for (int i = 0; i < savedTicks; i++) { Step(source, throttle: 65535); }
        var restored = new VehicleMovement(source.Configuration with { Mass = 3200 }, source.State.Physics);
        restored.Restore(VehicleStateCodec.Decode(VehicleStateCodec.Encode(source.State)));
        for (int i = savedTicks; i < 12; i++)
        {
            Step(source, throttle: 65535); Step(restored, throttle: 65535);
            Assert.That(restored.State.Air, Is.EqualTo(source.State.Air));
            Assert.That(restored.State.Physics.AngularVelocity, Is.EqualTo(source.State.Physics.AngularVelocity));
        }
    }

    [Test]
    public void SnapshotRestoreContinuesInputAndInertialReleaseExactly()
    {
        var source = Create();
        for (int i = 0; i < 80; i++) { Step(source, throttle: 65535, steering: -32767, roll: true); }
        var restored = Create();
        restored.Restore(VehicleStateCodec.Decode(VehicleStateCodec.Encode(source.State)));
        for (int i = 0; i < 60; i++)
        {
            Step(source); Step(restored);
            Assert.That(restored.State, Is.EqualTo(source.State));
        }
    }

    [Test]
    public void ConfigsRoundTripAndAirRollFramePreserveNewValues()
    {
        Assert.That(GameplayOptions.TryApply(new(), new Dictionary<string, double> { ["vehicle.air_roll_rate"] = 5 }, out var config, out _), Is.True);
        var restored = GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(1, new GameplayConfigurationState(1, config)));
        Assert.That(restored.State.Configuration, Is.EqualTo(config));
        var frame = new InputFrame(1, 123, 456, 789, InputButtons.AirRoll, InputButtons.AirRoll, 0);
        byte[] bytes = new byte[InputFrame.SerializedSize]; frame.Write(bytes);
        Assert.That(InputFrame.Read(bytes), Is.EqualTo(frame));
        Assert.Throws<ArgumentException>(() => new VehicleConfiguration { AirPitchRate = float.NaN }.Validate());
        Assert.Throws<ArgumentException>(() => new AirControlState(0, new Vector3(2), default).Validate());
    }

    [Test]
    public void HostPredictionAndCompleteAggregateRestoreRetainFlightContinuation()
    {
        var host = new HostVehicleSession(99);
        VehicleObservation Flight(VehicleSnapshot state) => new(state.Movement.Physics, Vector3.Zero);
        host.Step(default, Flight);
        var prediction = new PredictedVehicle(host.Snapshot().Vehicles.Single());
        for (int i = 0; i < 80; i++)
        {
            var frame = new InputFrame(0, 32767, 65535, 0, InputButtons.None, 0, 0, -32767, 0, 32767);
            prediction.Predict(frame, Flight);
            host.Step(frame, Flight);
            Assert.That(prediction.State.Movement, Is.EqualTo(host.World.GetVehicle(1).Movement));
            if (i % HostVehicleSession.SnapshotInterval == 0)
            {
                // Model ordinary acknowledgements while comparing each prediction before correction.
                prediction.Reconcile(new(host.World.GetVehicle(1), unchecked(prediction.History.NextSequence - 1)), Flight);
            }
            if (i == 7 || i == 30)
            {
                var snapshot = VehicleSnapshotCodec.Decode(VehicleSnapshotCodec.Encode(host.World.GetVehicle(1)));
                Assert.That(snapshot.Movement.Air, Is.EqualTo(host.World.GetVehicle(1).Movement.Air));
                var wire = VehicleNetworkCodec.DecodeSnapshot(VehicleNetworkCodec.EncodeSnapshot(host.Snapshot()));
                prediction = new PredictedVehicle(wire.Vehicles.Single());
            }
        }
    }
    [TestCase(32767, 0, 0)]
    [TestCase(0, 32767, 0)]
    [TestCase(0, 0, 32767)]
    public void ReleasePreservesEachAxisAfterSnapshotRestoreWithoutChangingTravel(int pitch, int yaw, int roll)
    {
        var movement = Create();
        for (ulong tick = 1; tick <= 30; tick++)
            movement.Step(new(tick, 0, 0, 0, InputButtons.None, 0, 0, (short)pitch, (short)yaw, (short)roll), movement.State.Physics, Vector3.Zero);
        var saved = VehicleStateCodec.Decode(VehicleStateCodec.Encode(movement.State));
        var restored = Create(); restored.Restore(saved);
        Assert.That(saved.Physics.AngularVelocity.Length(), Is.GreaterThan(0.5));
        foreach (var subject in new[] { movement, restored })
        {
            subject.Step(new(31, 0, 0, 0, 0, 0, 0), saved.Physics, Vector3.Zero);
            Assert.That(subject.State.Physics.AngularVelocity, Is.EqualTo(saved.Physics.AngularVelocity));
            Assert.That(subject.State.Physics.Orientation, Is.EqualTo(saved.Physics.Orientation));
            Assert.That(subject.State.Physics.LinearVelocity, Is.EqualTo(saved.Physics.LinearVelocity - Vector3.UnitY * subject.Configuration.Gravity / 60));
        }
        Assert.That(restored.State, Is.EqualTo(movement.State));
        var passive = Create();
        var observation = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, new Vector3(1, 2, 3));
        passive.Step(new(1, 0, 0, 0, 0, 0, 0), observation, Vector3.Zero);
        Assert.That(passive.State.Physics.AngularVelocity, Is.EqualTo(observation.AngularVelocity), "passive airborne rotation remains physical");
    }

    [Test]
    public void NeutralTakeoffAndSustainedFlightPreserveAllInertiaExceptGravity()
    {
        var pose = new VehiclePhysicsState(new(0, 50, 0), Quaternion.CreateFromYawPitchRoll(0.4f, 1.2f, 2.1f), new(4, 12, -25), new(2, -3, 4));
        var movement = new VehicleMovement(new(), pose);
        movement.Restore(new(0, pose, true, false, 0, 0));
        for (int tick = 0; tick < 120; tick++)
        {
            var before = movement.State.Physics;
            Step(movement);
            Assert.That(movement.State.Physics.AngularVelocity, Is.EqualTo(pose.AngularVelocity));
            Assert.That(movement.State.Physics.LinearVelocity, Is.EqualTo(before.LinearVelocity - Vector3.UnitY * movement.Configuration.Gravity / 60));
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void SameDirectionInputPreservesFasterSpinAndCounterInputBrakesThenReverses(int axis)
    {
        Vector3 basis = axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, basis * 6);
        var movement = new VehicleMovement(new(), pose);
        void Command(int direction)
        {
            short value = (short)(32767 * direction);
            var frame = new InputFrame(movement.State.Tick + 1, 0, 0, 0, 0, 0, 0,
                axis == 0 ? value : (short)0, axis == 1 ? (short)-value : (short)0, axis == 2 ? (short)-value : (short)0);
            movement.Step(frame, movement.State.Physics, Vector3.Zero);
        }
        for (int tick = 0; tick < 30; tick++) { Command(1); }
        Assert.That(movement.State.Physics.AngularVelocity, Is.EqualTo(pose.AngularVelocity));
        Command(-1);
        Assert.That(Vector3.Dot(movement.State.Physics.AngularVelocity, basis), Is.LessThan(6), "counter-input acts on its first tick");
        for (int tick = 0; tick < 60; tick++) { Command(-1); }
        Assert.That(Vector3.Dot(movement.State.Physics.AngularVelocity, basis), Is.LessThan(-1));
        var spin = movement.State.Physics.AngularVelocity;
        for (int tick = 0; tick < 60; tick++) { Step(movement); }
        Assert.That(movement.State.Physics.AngularVelocity, Is.EqualTo(spin));
    }

    [Test]
    public void ChangingToRollDoesNotEraseYawAndChassisSupportDoesNotDelayInput()
    {
        var movement = Create();
        for (int tick = 0; tick < 60; tick++) { Step(movement, steering: 32767); }
        float yaw = movement.State.Physics.AngularVelocity.Y;
        Step(movement, steering: 32767, roll: true);
        Assert.That(movement.State.Physics.AngularVelocity.Y, Is.EqualTo(yaw));
        Assert.That(movement.State.Physics.AngularVelocity.Z, Is.LessThan(0));
        var fresh = Create();
        fresh.Step(new(1, 0, 0, 0, 0, 0, 0, 32767), fresh.State.Physics, Vector3.UnitY, wheels: default(WheelSupport));
        Assert.That(fresh.State.Air.Input.X, Is.GreaterThan(0));
    }

    [Test]
    public void CrashBouncePreservesFastRotationAcrossRestoreAndRecoveryDelay()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI), new(0, 5, -20), new(-5, 1, 2));
        var movement = new VehicleMovement(new(), pose);
        movement.Restore(new VehicleState(0, pose, false, false, 0, 0, crashSeconds: 1));
        for (int tick = 0; tick < 60; tick++)
        {
            Step(movement);
            Assert.That(Vector3.Distance(movement.State.Physics.AngularVelocity, pose.AngularVelocity), Is.LessThan(0.00001));
        }
        var restored = Create(); restored.Restore(VehicleStateCodec.Decode(VehicleStateCodec.Encode(movement.State)));
        Step(movement); Step(restored);
        Assert.That(restored.State, Is.EqualTo(movement.State));
    }

    private static VehicleMovement Create(Quaternion? orientation = null) => new(new VehicleConfiguration(), new VehiclePhysicsState(Vector3.Zero, orientation ?? Quaternion.Identity, Vector3.Zero, Vector3.Zero));

    private static void Step(VehicleMovement movement, ushort throttle = 0, ushort brake = 0, short steering = 0, bool roll = false, bool grounded = false)
    {
        var input = new InputFrame(movement.State.Tick + 1, steering, throttle, brake, InputButtons.None, 0, 0, (short)((throttle - brake) / 65535f * 32767), roll ? (short)0 : steering, roll ? steering : (short)0);
        movement.Step(input, movement.State.Physics, grounded ? Vector3.UnitY : Vector3.Zero);
    }
}
