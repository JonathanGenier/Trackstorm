using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

[TestFixture]
internal sealed class LandingTests
{
    private Trackstorm.Core.Simulation.Simulation _world = null!;
    private static readonly VehiclePhysicsState Initial = new(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);

    [SetUp]
    public void Setup()
    {
        _world = new(new SimulationConfiguration(60));
        _world.AddVehicle(1, new(), new(), Initial);
        for (int i = 0; i < 3; i++) { Step(Quaternion.Identity); }
        Assert.That(_world.GetVehicle(1).Landing.Phase, Is.EqualTo(LandingPhase.Airborne));
    }

    [TestCase(0)]
    [TestCase(90)]
    [TestCase(180)]
    public void UprightYawAndBottomOutAreForgiven(float yaw)
    {
        Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw * MathF.PI / 180);
        var state = Step(rotation, Contact(Vector3.UnitY));
        Assert.That(state.Damage.CurrentHP, Is.EqualTo(100));
        Assert.That(state.Damage.LastCollisionTick, Is.Null);
        for (int i = 0; i < 20; i++) { state = Step(rotation, Contact(Vector3.UnitY), Vector3.UnitY); }
        Assert.That(state.Landing.Phase, Is.EqualTo(LandingPhase.Recovered));
        Assert.That(state.Damage.CurrentHP, Is.EqualTo(100));
    }

    [TestCase(90, 0)]
    [TestCase(180, 0)]
    [TestCase(0, 90)]
    [TestCase(0, -90)]
    public void BodyFirstCrashDamagesFirstImpact(float roll, float pitch)
    {
        var rotation = Quaternion.CreateFromYawPitchRoll(0, pitch * MathF.PI / 180, roll * MathF.PI / 180);
        var state = Step(rotation, Contact(Vector3.UnitY));
        Assert.That(state.Landing.Phase, Is.EqualTo(LandingPhase.Crash));
        Assert.That(state.Damage.CurrentHP, Is.LessThan(100));
    }

    [Test]
    public void RecoveryThenTumbleDamagesWithoutSpendingInitialCooldown()
    {
        var safe = Step(Quaternion.Identity, Contact(Vector3.UnitY));
        Assert.That(safe.Damage.LastCollisionTick, Is.Null);
        var crash = Step(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2), Contact(Vector3.UnitY));
        float hp = crash.Damage.CurrentHP;
        Assert.That(hp, Is.LessThan(100));
        Assert.That(Step(Quaternion.Identity, Contact(Vector3.UnitY)).Damage.CurrentHP, Is.EqualTo(hp));
        for (int i = 0; i < 12; i++) { Step(Quaternion.Identity); }
        Assert.That(Step(Quaternion.Identity, Contact(Vector3.UnitY)).Damage.CurrentHP, Is.LessThan(hp));
    }

    [TestCase(false, 0ul)]
    [TestCase(false, 2ul)]
    [TestCase(true, 2ul)]
    public void ObstacleAndVehicleContactsNeverReceiveForgiveness(bool terrain, ulong other)
    {
        var contact = new VehicleContact(-Vector3.UnitY * 20, Vector3.UnitY, 0, other, terrain, -Vector3.UnitY);
        var state = Step(Quaternion.Identity, contact);
        Assert.That(state.Damage.CurrentHP, Is.LessThan(100));
        Assert.That(state.Damage.LastDamage!.Attribution.InstigatorId, Is.EqualTo(other));
    }

    [Test]
    public void BankUsesVehicleLocalAttitudeAndMixedWallRemainsDamaging()
    {
        var rotation = Quaternion.CreateFromYawPitchRoll(1.4f, 0.4f, 0.6f);
        Vector3 normal = Vector3.Transform(Vector3.UnitY, rotation);
        var safe = Step(rotation, Contact(normal), normal);
        Assert.That(safe.Damage.CurrentHP, Is.EqualTo(100));
        var frame = Frame();
        var physics = new VehiclePhysicsState(Vector3.Zero, rotation, Vector3.Zero, Vector3.Zero);
        var wall = new VehicleContact(-Vector3.UnitX * 10, Vector3.UnitX, 0, 0);
        _world.Step(frame, [new VehicleStepRequest(1, frame, new VehicleObservation(physics, normal, [Contact(normal), wall], terrainSupport: normal))]);
        Assert.That(_world.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(82));
    }

    [Test]
    public void RestoredRecoveryAndCrashRetainTheirMeaningAndResetClearsEpisode()
    {
        var state = Step(Quaternion.Identity, Contact(Vector3.UnitY));
        var json = VehicleSnapshotCodec.Decode(VehicleSnapshotCodec.Encode(state));
        var network = VehicleNetworkCodec.DecodeSnapshot(VehicleNetworkCodec.EncodeSnapshot(new WorldSnapshot(1, state.Movement.Tick, [new ReplicatedVehicle(state, 0)]))).Vehicles[0].State;
        Assert.That(json.Landing, Is.EqualTo(state.Landing));
        Assert.That(network.Landing, Is.EqualTo(state.Landing));
        _world.Restore(new SimulationState(state.Movement.Tick, new InputFrame(state.Movement.Tick, 0, 0, 0, 0, 0, 0), [network]));
        Assert.That(Step(Quaternion.Identity, Contact(Vector3.UnitY)).Damage.CurrentHP, Is.EqualTo(100));
        var crash = Step(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI), Contact(Vector3.UnitY));
        Assert.That(VehicleSnapshotCodec.Decode(VehicleSnapshotCodec.Encode(crash)).Landing, Is.EqualTo(crash.Landing));
        var frame = Frame();
        _world.Step(frame, [new VehicleStepRequest(1, frame, new VehicleObservation(Initial, Vector3.Zero), reset: Initial)]);
        Assert.That(_world.GetVehicle(1).Landing, Is.EqualTo(default(LandingState)));
    }

    [Test]
    public void RecoveryExpiresAndDoesNotGrantPermanentTerrainImmunity()
    {
        Step(Quaternion.Identity, Contact(Vector3.UnitY));
        for (int i = 0; i < 60; i++) { Step(Quaternion.Identity, support: Vector3.UnitY); }
        Assert.That(_world.GetVehicle(1).Landing.Phase, Is.EqualTo(LandingPhase.Driving));
        Assert.That(Step(Quaternion.Identity, Contact(Vector3.UnitY), Vector3.UnitY).Damage.CurrentHP, Is.LessThan(100));
    }

    [Test]
    public void PredictionRetainsHostEpisodeWithoutPredictingDamage()
    {
        var initial = Step(Quaternion.Identity, Contact(Vector3.UnitY));
        var prediction = new PredictedVehicle(new ReplicatedVehicle(initial, 0));
        prediction.Predict(Frame(), _ => new VehicleObservation(Initial, Vector3.UnitY, [new VehicleContact(-Vector3.UnitX * 40, Vector3.UnitX, 0, 0)]));
        Assert.That(prediction.State.Damage, Is.EqualTo(initial.Damage));
        Assert.That(prediction.State.Landing, Is.EqualTo(initial.Landing));
    }

    [Test]
    public void InvalidLandingMemoryIsRejectedAtSnapshotBoundary()
    {
        var state = _world.GetVehicle(1);
        foreach (var invalid in new[] { new LandingState((LandingPhase)255, 0, 0, 0), new LandingState(LandingPhase.Recovery, 0, 0, 0), new LandingState(LandingPhase.Crash, 0, 60, 0), new LandingState(LandingPhase.Airborne, 4, 0, 0) })
        {
            Assert.Throws<ArgumentException>(() => new VehicleSnapshot(1, 1, state.Movement, state.Damage, state.ObservedPhysics, landing: invalid));
        }
    }

    private InputFrame Frame() => new(_world.State.Tick + 1, 0, 0, 0, 0, 0, 0);
    private VehicleSnapshot Step(Quaternion rotation, VehicleContact? contact = null, Vector3 support = default)
    {
        var frame = Frame();
        var physics = new VehiclePhysicsState(Vector3.Zero, rotation, Vector3.Zero, Vector3.Zero);
        _world.Step(frame, [new VehicleStepRequest(1, frame, new VehicleObservation(physics, support, contact.HasValue ? [contact.Value] : [], terrainSupport: support))]);
        return _world.GetVehicle(1);
    }
    private static VehicleContact Contact(Vector3 normal) => new(-normal * 20, normal, 0, 0, true, new Vector3(0, -0.6f, 0));
}
