using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Trackstorm.Core.Input;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;
using CoreSimulation = Trackstorm.Core.Simulation.Simulation;

namespace Trackstorm.Core.Tests.Vehicles;

/// <summary>Exercises the real Core simulation owner without a scene tree or native gameplay memory.</summary>
[TestFixture]
internal sealed class VehicleAuthorityTests
{
    /// <summary>One batch owns both vehicles' movement, health and clock regardless of submission order.</summary>
    [Test]
    public void Step_CommitsMovementAndHealthTogether()
    {
        CoreSimulation simulation = Create();
        InputFrame input = Frame(1, throttle: 65535);
        IReadOnlyList<VehicleStepResult> results = simulation.Step(input, [Request(2, input), Request(1, input, effects: [Effect(25)])]);
        Assert.That(results.Select(result => result.Snapshot.VehicleId), Is.EqualTo(new ulong[] { 1, 2 }));
        Assert.That(simulation.State.Tick, Is.EqualTo(1));
        Assert.That(simulation.State.LastInput, Is.EqualTo(input));
        Assert.That(simulation.State.Vehicles.All(vehicle => vehicle.Movement.Tick == 1), Is.True);
        Assert.That(simulation.GetVehicle(1).Movement.Physics.LinearVelocity.Z, Is.LessThan(0));
        Assert.That(simulation.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(75));
        Assert.That(simulation.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(100));
        Assert.That(results[0].DamageEvents.Single().Tick, Is.EqualTo(1));
        Assert.That(simulation.State.Vehicles[0], Is.SameAs(simulation.GetVehicle(1)));
    }

    /// <summary>Missing, duplicate and unordered observations cannot advance any part of the world.</summary>
    [Test]
    public void Step_RejectsIncompleteOrMisorderedBatchesAtomically()
    {
        CoreSimulation simulation = Create();
        SimulationState before = simulation.State;
        Assert.Throws<ArgumentException>(() => simulation.Step(Frame(1)));
        Assert.Throws<ArgumentException>(() => simulation.Step(Frame(1), [Request(1, Frame(1)), Request(1, Frame(1))]));
        Assert.Throws<ArgumentException>(() => simulation.Step(Frame(1), [Request(1, Frame(1)), Request(2, Frame(2))]));
        Assert.Throws<ArgumentException>(() => simulation.Step(Frame(2), [Request(1, Frame(2)), Request(2, Frame(2))]));
        Assert.That(simulation.State, Is.EqualTo(before));
        Assert.That(simulation.GetVehicle(1), Is.SameAs(before.Vehicles[0]));
    }

    /// <summary>A late candidate failure rolls back even an earlier otherwise valid damage/movement candidate.</summary>
    [Test]
    public void Step_LateLifeOverflowDoesNotPartiallyCommit()
    {
        CoreSimulation simulation = Create();
        VehicleSnapshot second = simulation.GetVehicle(2);
        simulation.Restore(new SimulationState(0, default, [simulation.GetVehicle(1), new VehicleSnapshot(2, ulong.MaxValue, second.Movement, second.Damage, second.ObservedPhysics)]));
        SimulationState before = simulation.State;
        Assert.Throws<OverflowException>(() => simulation.Step(Frame(1), [Request(1, Frame(1, throttle: 65535), effects: [Effect(100)]), new VehicleStepRequest(2, Frame(1), Observation(), reset: Physics())]));
        Assert.That(simulation.State, Is.EqualTo(before));
        Assert.That(simulation.GetVehicle(1), Is.SameAs(before.Vehicles[0]));
        Assert.That(simulation.GetVehicle(2), Is.SameAs(before.Vehicles[1]));
    }

    /// <summary>Registration and plain observations reject invalid identity, rates, vectors and contact data.</summary>
    [Test]
    public void Boundary_RejectsMalformedObservationsAndRegistration()
    {
        CoreSimulation simulation = Create();
        Assert.Throws<ArgumentException>(() => simulation.AddVehicle(1, new(), new(), Physics()));
        Assert.Throws<ArgumentException>(() => simulation.AddVehicle(3, new() { TicksPerSecond = 120 }, new(), Physics()));
        Assert.Throws<ArgumentException>(() => new VehicleObservation(default, Vector3.UnitY));
        Assert.Throws<ArgumentException>(() => new VehicleObservation(Physics(), new Vector3(float.NaN, 0, 0)));
        Assert.Throws<ArgumentException>(() => new VehicleObservation(Physics(), Vector3.UnitY * 3));
        Assert.Throws<ArgumentException>(() => new VehicleObservation(Physics(), Vector3.UnitY, [default]));
        Assert.Throws<ArgumentException>(() => new VehicleContact(Vector3.Zero, Vector3.UnitY, float.PositiveInfinity, 0));
        Advance(simulation, Frame(1));
        Assert.Throws<ArgumentException>(() => simulation.AddVehicle(3, new(), new(), Physics()));
    }

    /// <summary>Core selects damaging contacts, assigns attribution, and preserves harmless/cooldown semantics.</summary>
    [Test]
    public void Collision_SelectionAttributionAndCooldownBelongToCore()
    {
        CoreSimulation simulation = Create();
        VehicleContact brush = Contact(1, 0);
        VehicleContact hit = Contact(15, 2);
        InputFrame input = Frame(1);
        simulation.Step(input, [Request(1, input, contacts: [brush, hit]), Request(2, input, contacts: [Contact(15, 1)])]);
        VehicleDamageState first = simulation.GetVehicle(1).Damage;
        Assert.That(first.CurrentHP, Is.LessThan(100));
        Assert.That(first.CurrentHP, Is.EqualTo(simulation.GetVehicle(2).Damage.CurrentHP));
        Assert.That(first.LastDamage!.Attribution, Is.EqualTo(new DamageContext("collision", 2, "vehicle")));
        Assert.That(first.LastCollisionTick, Is.EqualTo(1));
        simulation.Step(Frame(2), [Request(1, Frame(2), contacts: [hit]), Request(2, Frame(2), contacts: [brush])]);
        Assert.That(simulation.GetVehicle(1).Damage, Is.EqualTo(first));
        Assert.That(simulation.GetVehicle(2).Damage.LastCollisionTick, Is.EqualTo(1));
        for (ulong tick = 3; tick <= 30; tick++)
        {
            Advance(simulation, Frame(tick));
        }

        simulation.Step(Frame(31), [Request(1, Frame(31), contacts: [Contact(15, 0)]), Request(2, Frame(31))]);
        Assert.That(simulation.GetVehicle(1).Damage.LastDamage!.Attribution, Is.EqualTo(new DamageContext("collision", 0, "world-or-prop")));
        Assert.That(simulation.GetVehicle(1).Damage.CurrentHP, Is.LessThan(first.CurrentHP));
    }

    /// <summary>Death disables drive once; repair cannot revive; reset creates a fresh life on the same global clock.</summary>
    [Test]
    public void DeathAndReset_AreCoherentAcrossTheAggregate()
    {
        CoreSimulation simulation = Create();
        InputFrame drive = Frame(1, throttle: 65535, drift: true);
        IReadOnlyList<VehicleStepResult> results = simulation.Step(drive, [Request(1, drive, effects: [Effect(1000)]), Request(2, drive)]);
        VehicleSnapshot dead = simulation.GetVehicle(1);
        Assert.That(results[0].DamageEvents.Single().DestroyedTransition, Is.True);
        Assert.That(dead.Damage.Destroyed, Is.True);
        Assert.That(dead.Movement.Physics.LinearVelocity.Z, Is.Zero);
        Assert.That(dead.Movement.Drifting, Is.False);
        CoreSimulation restored = Create();
        restored.Restore(new SimulationState(simulation.State.Tick, simulation.State.LastInput, simulation.State.Vehicles.Select(vehicle => VehicleSnapshotCodec.Decode(VehicleSnapshotCodec.Encode(vehicle)))));
        IReadOnlyList<VehicleStepResult> restoredResults = restored.Step(Frame(2), [Request(1, Frame(2, throttle: 65535, drift: true), effects: [Effect(1000)]), Request(2, Frame(2))]);
        Assert.That(restoredResults[0].DamageEvents, Is.Empty);
        Assert.That(restored.GetVehicle(1).Damage, Is.EqualTo(dead.Damage));
        Assert.That(restored.GetVehicle(1).Movement.Physics.LinearVelocity.Z, Is.Zero);
        results = simulation.Step(Frame(2), [new VehicleStepRequest(1, Frame(2, throttle: 65535, drift: true), Observation(), [Effect(1000)], repair: 100), Request(2, Frame(2))]);
        Assert.That(results[0].DamageEvents, Is.Empty);
        Assert.That(simulation.GetVehicle(1).Damage, Is.EqualTo(dead.Damage));
        results = simulation.Step(Frame(3), [new VehicleStepRequest(1, Frame(3), Observation(), reset: Physics(new Vector3(5, 0, 0))), Request(2, Frame(3))]);
        VehicleSnapshot fresh = simulation.GetVehicle(1);
        Assert.That(results[0].Reset, Is.True);
        Assert.That(fresh.LifeId, Is.EqualTo(dead.LifeId + 1));
        Assert.That(fresh.Movement.Tick, Is.EqualTo(3));
        Assert.That(fresh.Movement.SteeringAngle, Is.EqualTo(new VehicleConfiguration().SteeringResponse / 60).Within(0.000001f));
        Assert.That(fresh.Movement.Handbrake, Is.Zero);
        Assert.That(fresh.Damage.CurrentHP, Is.EqualTo(100));
        Assert.That(fresh.Damage.LastDamage, Is.Null);
        Assert.That(fresh.Damage.LastCollisionTick, Is.Null);
        Assert.That(simulation.State.Vehicles.All(vehicle => vehicle.Movement.Tick == 3), Is.True);
    }

    /// <summary>Serialized restoration reproduces handbrake recovery, collision cooldown and accepted effects.</summary>
    [Test]
    public void RestoredAggregate_ReplaysMovementDamageAndAcceptedImpulses()
    {
        CoreSimulation original = Create();
        VehiclePhysicsState physics = Physics(velocity: new Vector3(0, 0, -16));
        for (ulong tick = 1; tick <= 45; tick++)
        {
            InputFrame input = Frame(tick, drift: true);
            original.Step(input, [Request(1, input, physics, contacts: tick == 44 ? [Contact(9, 2)] : null, effects: tick == 45 ? [Effect(5)] : null), Request(2, input, physics)]);
        }

        Assert.That(original.GetVehicle(1).Movement.Handbrake, Is.EqualTo(1));
        VehicleSnapshot[] decoded = original.State.Vehicles.Select(vehicle => VehicleSnapshotCodec.Decode(VehicleSnapshotCodec.Encode(vehicle))).ToArray();
        Assert.That(decoded[0].Effects.Single().Effect, Is.EqualTo(Effect(5).Effect));
        Assert.That(decoded[0].Effects.Single().Attribution, Is.EqualTo(Effect(5).Attribution));
        Assert.That(decoded[0].Damage, Is.EqualTo(original.GetVehicle(1).Damage));
        CoreSimulation restored = Create();
        restored.Restore(new SimulationState(original.State.Tick, original.State.LastInput, decoded));
        for (ulong tick = 46; tick <= 110; tick++)
        {
            InputFrame input = Frame(tick);
            VehicleStepRequest[] requests = [Request(1, input, physics, contacts: [Contact(9, 2)]), Request(2, input, physics)];
            original.Step(input, requests);
            restored.Step(input, requests);
            if (tick == 46)
            {
                Assert.That(restored.GetVehicle(1).Movement.Handbrake, Is.GreaterThan(0));
                Assert.That(restored.GetVehicle(1).Damage, Is.EqualTo(decoded[0].Damage), "restoration must not reapply previous damage or bypass cooldown");
                Assert.That(restored.GetVehicle(1).Effects, Is.Empty);
            }

            Assert.That(restored.State.LastInput, Is.EqualTo(original.State.LastInput));
            foreach (VehicleSnapshot expected in original.State.Vehicles)
            {
                Assert.That(VehicleSnapshotCodec.Encode(restored.GetVehicle(expected.VehicleId)), Is.EqualTo(VehicleSnapshotCodec.Encode(expected)));
            }
        }

        Assert.That(restored.GetVehicle(1).Movement.Handbrake, Is.Zero);
    }

    /// <summary>Invalid configuration-specific memory on the final vehicle leaves the whole world untouched.</summary>
    [Test]
    public void Restore_InvalidSteeringDoesNotPartiallyRestore()
    {
        CoreSimulation simulation = Create();
        SimulationState before = simulation.State;
        VehicleSnapshot first = simulation.GetVehicle(1);
        VehicleSnapshot second = simulation.GetVehicle(2);
        var invalid = new VehicleSnapshot(2, 1, new VehicleState(0, second.Movement.Physics, true, true, 0.9f, 0), second.Damage, second.ObservedPhysics);
        var changed = new VehicleSnapshot(1, 2, first.Movement, first.Damage, first.ObservedPhysics);
        Assert.Throws<ArgumentException>(() => simulation.Restore(new SimulationState(0, default, [changed, invalid])));
        Assert.That(simulation.State, Is.EqualTo(before));
        Assert.That(simulation.GetVehicle(1), Is.SameAs(first));
    }

    /// <summary>Version, payload validation and the existing nested binary format cannot silently drift.</summary>
    [Test]
    public void Codec_RejectsCorruptAndIncoherentAggregates()
    {
        VehicleSnapshot snapshot = Create().GetVehicle(1);
        byte[] bytes = VehicleSnapshotCodec.Encode(snapshot);
        Assert.That(bytes[0], Is.EqualTo(3));
        JsonObject json = JsonNode.Parse(Encoding.UTF8.GetString(bytes[1..]))!.AsObject();
        byte[] movement = Convert.FromBase64String(json["Movement"]!.GetValue<string>());
        Assert.That(movement.Length, Is.EqualTo(VehicleStateCodec.SerializedSize));
        Assert.That(movement[0], Is.EqualTo(6));
        Assert.That(VehicleSnapshotCodec.Encode(VehicleSnapshotCodec.Decode(bytes)), Is.EqualTo(bytes));
        byte[] wrongVersion = (byte[])bytes.Clone();
        wrongVersion[0] = 1;
        Assert.Throws<ArgumentException>(() => VehicleSnapshotCodec.Decode(wrongVersion));
        Assert.Throws<ArgumentException>(() => VehicleSnapshotCodec.Decode(bytes[..^1]));
        Assert.Throws<ArgumentException>(() => VehicleSnapshotCodec.Decode(new byte[65537]));
        json["LifeId"] = 0;
        Assert.Throws<ArgumentException>(() => DecodeJson(json));
        json["LifeId"] = 1;
        json["Effects"] = null;
        Assert.Throws<ArgumentException>(() => DecodeJson(json));
        json["Effects"] = new JsonArray();
        json["Damage"]!["CurrentHP"] = -1;
        Assert.Throws<ArgumentException>(() => DecodeJson(json));
        json["Damage"]!["CurrentHP"] = 100;
        movement[61] = 128;
        json["Movement"] = Convert.ToBase64String(movement);
        Assert.Throws<ArgumentException>(() => DecodeJson(json));
        movement[61] = 0;
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(movement.AsSpan(9), float.PositiveInfinity);
        json["Movement"] = Convert.ToBase64String(movement);
        Assert.Throws<ArgumentException>(() => DecodeJson(json));
    }

    /// <summary>The old tick/input-only message must reject full vehicle state rather than silently losing it.</summary>
    [Test]
    public void LegacyReplication_RejectsVehicleAggregates()
    {
        Assert.Throws<ArgumentException>(() => new SimulationStateMessage(Create().State));
        Assert.That(SimulationStateMessage.SerializedSize, Is.EqualTo(22));
    }

    /// <summary>HUD speed measures solved horizontal motion, including reverse and external sideways velocity.</summary>
    /// <param name="x">Observed lateral velocity.</param>
    /// <param name="y">Observed vertical velocity.</param>
    /// <param name="z">Observed longitudinal velocity.</param>
    /// <param name="expected">Expected nonnegative road speed.</param>
    [TestCase(0, 0, 0, 0)]
    [TestCase(0, 0, -10, 10)]
    [TestCase(0, 0, 5, 5)]
    [TestCase(0, 40, 0, 0)]
    [TestCase(4, 25, 3, 5)]
    [TestCase(12, 0, 0, 12)]
    public void Speed_UsesSolvedHorizontalMagnitude(float x, float y, float z, float expected)
    {
        CoreSimulation simulation = Create();
        InputFrame input = Frame(1, throttle: 65535);
        simulation.Step(input, [Request(1, input, Physics(velocity: new Vector3(x, y, z))), Request(2, input)]);
        VehicleSnapshot snapshot = simulation.GetVehicle(1);
        Assert.That(snapshot.Speed, Is.EqualTo(expected).Within(0.00001));
        Assert.That(snapshot.Movement.Physics.LinearVelocity.Y, Is.Not.EqualTo(y), "gravity is commanded independently of observed HUD speed");
        if (expected == 0)
        {
            Assert.That(snapshot.Movement.CommandSpeed, Is.GreaterThan(0));
        }
    }

    private static CoreSimulation Create()
    {
        var simulation = new CoreSimulation(new SimulationConfiguration(60));
        simulation.AddVehicle(1, new(), new(), Physics());
        simulation.AddVehicle(2, new(), new(), Physics());
        return simulation;
    }

    private static VehiclePhysicsState Physics(Vector3 position = default, Vector3 velocity = default) => new(position, Quaternion.Identity, velocity, Vector3.Zero);

    private static VehicleObservation Observation(VehiclePhysicsState? physics = null, IEnumerable<VehicleContact>? contacts = null) => new(physics ?? Physics(), Vector3.UnitY, contacts);

    private static VehicleContact Contact(float speed, ulong other) => new(new Vector3(-speed, 0, 0), Vector3.UnitX, 0, other);

    private static VehicleEffectRequest Effect(float amount) => new(new DamageEffect(amount, new Vector3(900, 300, 0), new Vector3(0, 0, 1)), new DamageContext("explosion", 99, "test-effect"));

    private static InputFrame Frame(ulong tick, ushort throttle = 0, bool drift = false) => new(tick, 20000, throttle, 0, drift ? InputButtons.Drift : InputButtons.None, InputButtons.None, InputButtons.None);

    private static VehicleStepRequest Request(ulong id, InputFrame input, VehiclePhysicsState? physics = null, IEnumerable<VehicleContact>? contacts = null, IEnumerable<VehicleEffectRequest>? effects = null) => new(id, input, Observation(physics, contacts), effects);

    private static void Advance(CoreSimulation simulation, InputFrame input) => simulation.Step(input, [Request(1, input), Request(2, input)]);

    private static VehicleSnapshot DecodeJson(JsonObject json) => VehicleSnapshotCodec.Decode(new byte[] { 2 }.Concat(Encoding.UTF8.GetBytes(json.ToJsonString())).ToArray());
}
