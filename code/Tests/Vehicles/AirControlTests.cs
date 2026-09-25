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
    public void DelayRequiresContinuousFlightAndLandingClearsContinuation()
    {
        var movement = Create();
        for (int i = 0; i < 8; i++) { Step(movement, throttle: 65535); }
        Assert.That(movement.State.Physics.AngularVelocity, Is.EqualTo(Vector3.Zero));
        Step(movement, grounded: true);
        Assert.That(movement.State.Air, Is.EqualTo(default(AirControlState)));
        for (int i = 0; i < 8; i++) { Step(movement, throttle: 65535); }
        Assert.That(movement.State.Physics.AngularVelocity, Is.EqualTo(Vector3.Zero));
        Step(movement, throttle: 65535);
        Assert.That(movement.State.Physics.AngularVelocity.X, Is.LessThan(0));
    }

    [TestCase(65535, 0, 0, false, -1, 0, 0)]
    [TestCase(0, 65535, 0, false, 1, 0, 0)]
    [TestCase(0, 0, -32767, false, 0, 1, 0)]
    [TestCase(0, 0, 32767, false, 0, -1, 0)]
    [TestCase(0, 0, -32767, true, 0, 0, 1)]
    [TestCase(65535, 0, 32767, true, -1, 0, -1)]
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
    public void ReleaseDampsSpinWithoutSeekingWorldUpAndZeroDampingPreservesSpin()
    {
        Quaternion inverted = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 2.4f);
        var movement = Create(inverted);
        for (int i = 0; i < 90; i++) { Step(movement, steering: 32767, roll: true); }
        for (int i = 0; i < 90; i++) { Step(movement); }
        Assert.That(movement.State.Physics.AngularVelocity.Length(), Is.LessThan(0.001));
        Assert.That(movement.State.Physics.Orientation, Is.EqualTo(inverted));
        var free = new VehicleMovement(new VehicleConfiguration { AirStabilization = 0 }, movement.State.Physics);
        free.Restore(movement.State);
        for (int i = 0; i < 90; i++) { Step(free, steering: 32767, roll: true); }
        Vector3 spin = free.State.Physics.AngularVelocity;
        for (int i = 0; i < 90; i++) { Step(free); }
        Assert.That(Vector3.Distance(free.State.Physics.AngularVelocity, spin), Is.LessThan(0.001));
    }

    [Test]
    public void SnapshotRestoreContinuesDelayInputAndReleaseExactly()
    {
        var source = Create();
        for (int i = 0; i < 16; i++) { Step(source, throttle: 65535, steering: -32767, roll: true); }
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
        Assert.That(GameplayOptions.TryApply(new(), new Dictionary<string, double> { ["vehicle.air_delay"] = 0.3, ["vehicle.air_roll_rate"] = 5 }, out var config, out _), Is.True);
        var restored = GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(1, new GameplayConfigurationState(1, config)));
        Assert.That(restored.State.Configuration, Is.EqualTo(config));
        var frame = new InputFrame(1, 123, 456, 789, InputButtons.AirRoll, InputButtons.AirRoll, 0);
        byte[] bytes = new byte[InputFrame.SerializedSize]; frame.Write(bytes);
        Assert.That(InputFrame.Read(bytes), Is.EqualTo(frame));
        Assert.Throws<ArgumentException>(() => new VehicleConfiguration { AirDelay = float.NaN }.Validate());
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
            var frame = new InputFrame(0, 32767, 65535, 0, InputButtons.AirRoll, 0, 0);
            prediction.Predict(frame, Flight);
            host.Step(frame, Flight);
            Assert.That(prediction.State.Movement, Is.EqualTo(host.World.GetVehicle(1).Movement));
            if (i == 7 || i == 30)
            {
                var snapshot = VehicleSnapshotCodec.Decode(VehicleSnapshotCodec.Encode(host.World.GetVehicle(1)));
                Assert.That(snapshot.Movement.Air, Is.EqualTo(host.World.GetVehicle(1).Movement.Air));
                var wire = VehicleNetworkCodec.DecodeSnapshot(VehicleNetworkCodec.EncodeSnapshot(host.Snapshot()));
                prediction = new PredictedVehicle(wire.Vehicles.Single());
            }
        }
    }
    private static VehicleMovement Create(Quaternion? orientation = null) => new(new VehicleConfiguration(), new VehiclePhysicsState(Vector3.Zero, orientation ?? Quaternion.Identity, Vector3.Zero, Vector3.Zero));

    private static void Step(VehicleMovement movement, ushort throttle = 0, ushort brake = 0, short steering = 0, bool roll = false, bool grounded = false)
    {
        var input = new InputFrame(movement.State.Tick + 1, steering, throttle, brake, roll ? InputButtons.AirRoll : 0, 0, 0);
        movement.Step(input, movement.State.Physics, grounded ? Vector3.UnitY : Vector3.Zero);
    }
}
