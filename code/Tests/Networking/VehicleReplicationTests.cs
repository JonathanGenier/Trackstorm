using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Networking;

/// <summary>Deterministic protocol, ownership, sequencing and replay regression coverage.</summary>
[TestFixture]
internal sealed class VehicleReplicationTests
{
    /// <summary>Serial arithmetic distinguishes wrap, equality, stale values and ambiguity.</summary>
    /// <param name="candidate">Potentially newer identity.</param>
    /// <param name="previous">Reference identity.</param>
    /// <param name="expected">Whether the candidate follows it.</param>
    [TestCase(1u, 0u, true)]
    [TestCase(0u, uint.MaxValue, true)]
    [TestCase(uint.MaxValue, 0u, false)]
    [TestCase(5u, 5u, false)]
    [TestCase(0x80000000u, 0u, false)]
    public void SequenceOrdering(uint candidate, uint previous, bool expected) => Assert.That(NetworkSequence.IsNewer(candidate, previous), Is.EqualTo(expected));

    /// <summary>Redundancy is bounded and acknowledgement preserves precisely the newer commands through wrap.</summary>
    [Test]
    public void RedundancyAndAcknowledgementWrap()
    {
        var history = new InputHistory(uint.MaxValue - 2);
        for (int i = 0; i < 6; i++)
        {
            history.Add(Drive());
        }

        Assert.That(history.GetRedundancy().Select(input => input.Sequence), Is.EqualTo(new uint[] { 0, 1, 2, 3 }));
        Assert.That(history.Acknowledge(1), Is.True);
        Assert.That(history.Pending.Select(input => input.Sequence), Is.EqualTo(new uint[] { 2, 3 }));
        Assert.That(history.Acknowledge(4), Is.False);
        Assert.That(history.Acknowledge(uint.MaxValue), Is.False);
        Assert.That(history.Acknowledge(1), Is.True);
        Assert.That(history.Pending.Count, Is.EqualTo(2));
        Assert.That(history.CanAcknowledge(unchecked(3u + 0x80000000u)), Is.False);
        var host = new HostInputBuffer(uint.MaxValue - 2);
        Assert.That(host.Receive([new(uint.MaxValue - 1, Drive()), new(uint.MaxValue, Drive()), new(0, Drive())]), Is.True);
        host.Consume(1);
        host.Consume(2);
        host.Consume(3);
        Assert.That(host.LastAcknowledged, Is.Zero);
    }

    /// <summary>Lost acknowledgements cannot grow memory without a bound.</summary>
    [Test]
    public void PredictionHistoryIsBounded()
    {
        var history = new InputHistory();
        for (int i = 0; i < InputHistory.Capacity; i++)
        {
            history.Add(Drive());
        }

        Assert.Throws<InvalidOperationException>(() => history.Add(Drive()));
        Assert.That(history.Pending.Count, Is.EqualTo(InputHistory.Capacity));
    }

    /// <summary>Redundant packets recover missing delivery without consuming duplicates or accelerating the host.</summary>
    [Test]
    public void HostRejectsStaleDuplicateAndExcessiveFutureInputs()
    {
        var host = new HostInputBuffer();
        var history = new InputHistory();
        history.Add(Drive());
        history.Add(Drive());
        SequencedInput[] packet = history.GetRedundancy();
        Assert.That(host.Receive(packet), Is.True);
        Assert.That(host.Receive(packet), Is.False);
        Assert.That(host.Receive([packet[0]]), Is.False);
        Assert.That(host.Receive([new SequencedInput(10000, Drive())]), Is.False);
        host.Consume(1);
        Assert.That(host.LastAcknowledged, Is.EqualTo(1));
        host.Consume(2);
        Assert.That(host.LastAcknowledged, Is.EqualTo(2));
        for (ulong tick = 3; tick <= 18; tick++)
        {
            host.Consume(tick);
        }

        Assert.That(host.Consume(19).Accelerate, Is.Zero);
        Assert.That(host.LastAcknowledged, Is.EqualTo(2));
    }

    /// <summary>A gap older than the redundancy wait is explicitly retired, preventing an eternal stalled queue.</summary>
    [Test]
    public void HostRetiresUnrecoverableLossGap()
    {
        var host = new HostInputBuffer();
        Assert.That(host.Receive([new SequencedInput(5, Drive())]), Is.True);
        host.Consume(1);
        host.Consume(2);
        Assert.That(host.LastAcknowledged, Is.Zero);
        host.Consume(3);
        Assert.That(host.LastAcknowledged, Is.EqualTo(5));
    }

    /// <summary>A delivery burst cannot leave controls permanently seconds behind, including sequence wrap.</summary>
    /// <param name="origin">Initial stream identity.</param>
    [TestCase(0u)]
    [TestCase(uint.MaxValue - 40)]
    public void HostRetiresBacklogWithoutExtraSimulationTicks(uint origin)
    {
        var host = new HostInputBuffer(origin);
        var history = new InputHistory(origin);
        for (int i = 0; i < 80; i++)
        {
            history.Add(Drive((short)i));
            Assert.That(host.Receive(history.GetRedundancy()), Is.True);
        }

        InputFrame frame = host.Consume(1);
        Assert.That(frame.Tick, Is.EqualTo(1));
        Assert.That(frame.Steering, Is.EqualTo(74));
        Assert.That(host.LastAcknowledged, Is.EqualTo(unchecked(origin + 75)));
        Assert.That(history.Acknowledge(host.LastAcknowledged), Is.True);
        Assert.That(history.Pending.Count, Is.EqualTo(5));
        for (ulong tick = 2; tick <= 6; tick++)
        {
            Assert.That(host.Consume(tick).Steering, Is.EqualTo(73 + (int)tick));
        }

        Assert.That(host.LastAcknowledged, Is.EqualTo(unchecked(origin + 80)));
        Assert.That(host.Receive([new(unchecked(origin + 1), Drive())]), Is.False);
    }

    /// <summary>Only established senders drive their assigned vehicle; full sessions cannot accept a ninth player.</summary>
    [Test]
    public void HostOwnershipAdmissionDepartureAndJoinAtCurrentTick()
    {
        var host = new HostVehicleSession(99);
        for (ulong peer = 1; peer <= 7; peer++)
        {
            Assert.That(host.Join(peer), Is.EqualTo(peer + 1));
        }

        Assert.That(host.Join(8), Is.Zero);
        Assert.That(host.Receive(8, 99, [new(1, Drive())]), Is.False);
        Assert.That(host.Receive(1, 98, [new(1, Drive())]), Is.False);
        Assert.That(host.Receive(1, 99, [new(1, Drive())]), Is.True);
        host.Step(default, Observe);
        Assert.That(host.World.GetVehicle(2).Movement.Physics.LinearVelocity.Z, Is.LessThan(0));
        Assert.That(host.World.GetVehicle(3).Movement.Physics.LinearVelocity.Z, Is.Zero);
        host.Leave(1);
        Assert.That(host.Receive(1, 99, [new(2, Drive())]), Is.False);
        Assert.That(host.Join(8), Is.EqualTo(9));
        Assert.That(host.World.GetVehicle(9).Movement.Tick, Is.EqualTo(1));
        Assert.That(host.World.GetVehicle(9).Movement.Physics.Position, Is.EqualTo(Trackstorm.Core.Arenas.PrototypeArena.Configuration.Players[1].Position), "A fresh identity reuses the departed spawn slot, not the host slot.");
        host.Step(default, Observe);
        Assert.That(host.Snapshot().Vehicles.Count, Is.EqualTo(8));
    }

    /// <summary>Apply-authority then ordered replay converges exactly with uninterrupted prediction.</summary>
    [Test]
    public void ReconciliationReplaysUnacknowledgedInputsInOrder()
    {
        var host = new HostVehicleSession(99);
        host.Join(42);
        var prediction = new PredictedVehicle(host.Snapshot().Vehicles[1]);
        var frames = new[] { Drive(), Drive(12000), Drive(-18000), new InputFrame(0, 0, 0, 65535, 0, 0, 0) };
        foreach (InputFrame frame in frames)
        {
            prediction.Predict(frame, Observe);
        }

        Assert.That(prediction.State.Movement.Physics.LinearVelocity.Z, Is.LessThan(0), "Prediction advances before any acknowledgement.");
        VehicleState expected = prediction.State.Movement;
        host.Receive(42, 99, prediction.History.GetRedundancy());
        host.Step(default, Observe);
        host.Step(default, Observe);
        var replayed = new List<ulong>();
        bool accepted = prediction.Reconcile(host.Snapshot().Vehicles[1], state =>
        {
            replayed.Add(state.Movement.Tick);
            return Observe(state);
        });
        Assert.That(accepted, Is.True);
        Assert.That(replayed, Is.EqualTo(new ulong[] { 2, 3 }));
        Assert.That(prediction.State.Movement, Is.EqualTo(expected));
        Assert.That(prediction.History.Pending.Select(input => input.Sequence), Is.EqualTo(new uint[] { 3, 4 }));
        Assert.That(prediction.PredictionError, Is.Zero);
        Assert.That(prediction.Reconcile(host.Snapshot().Vehicles[1], Observe), Is.False);
    }

    /// <summary>Complete eight-vehicle snapshots are compact and malformed payloads fail before publication.</summary>
    [Test]
    public void SnapshotBinaryRoundTripAndCorruption()
    {
        var host = new HostVehicleSession(99);
        for (ulong peer = 1; peer <= 7; peer++)
        {
            host.Join(peer);
        }

        host.Step(Drive(), Observe);
        WorldSnapshot expected = host.Snapshot();
        byte[] bytes = VehicleNetworkCodec.EncodeSnapshot(expected);
        Assert.That(bytes.Length, Is.LessThan(1200));
        WorldSnapshot decoded = VehicleNetworkCodec.DecodeSnapshot(bytes);
        Assert.That(decoded.Tick, Is.EqualTo(expected.Tick));
        Assert.That(decoded.Session, Is.EqualTo(99));
        for (int i = 0; i < 8; i++)
        {
            Assert.That(decoded.Vehicles[i].State.Movement, Is.EqualTo(expected.Vehicles[i].State.Movement));
            Assert.That(decoded.Vehicles[i].State.ObservedPhysics, Is.EqualTo(expected.Vehicles[i].State.ObservedPhysics));
            Assert.That(decoded.Vehicles[i].State.Damage, Is.EqualTo(expected.Vehicles[i].State.Damage));
        }

        Assert.Throws<ArgumentException>(() => VehicleNetworkCodec.DecodeSnapshot(bytes[..^1]));
        Assert.Throws<ArgumentException>(() => VehicleNetworkCodec.DecodeSnapshot([.. bytes, 0]));
        bytes[2] = 99;
        Assert.Throws<ArgumentException>(() => VehicleNetworkCodec.DecodeSnapshot(bytes));
    }

    /// <summary>Input/control payloads preserve logical values and reject invalid lengths and counts.</summary>
    [Test]
    public void ControlAndInputCodecRoundTrip()
    {
        Assert.That(VehicleNetworkCodec.DecodeWelcome(VehicleNetworkCodec.EncodeWelcome(99, 2)), Is.EqualTo((99ul, 2ul)));
        SequencedInput[] inputs = [new(1, Drive(1234)), new(2, Drive(-321))];
        byte[] packet = VehicleNetworkCodec.EncodeInputs(99, inputs);
        var decoded = VehicleNetworkCodec.DecodeInputs(packet);
        Assert.That(decoded.Session, Is.EqualTo(99));
        Assert.That(decoded.Inputs, Is.EqualTo(inputs));
        Assert.That(decoded.Life, Is.EqualTo(1));
        packet[20] = 255;
        Assert.Throws<ArgumentException>(() => VehicleNetworkCodec.DecodeInputs(packet));
    }

    /// <summary>Snapshot ordering rejects duplicates, stale data, other sessions and ambiguous jumps while allowing wire-tick wrap.</summary>
    [Test]
    public void SnapshotHistoryOrderingAndBoundedStorage()
    {
        var host = new HostVehicleSession(99);
        var history = new SnapshotHistory(99);
        WorldSnapshot At(ulong tick)
        {
            VehicleSnapshot initial = host.World.GetVehicle(1);
            var state = new VehicleSnapshot(1, 1, new VehicleState(tick, initial.Movement.Physics, false, false, 0, 0), initial.Damage, initial.ObservedPhysics);
            return new WorldSnapshot(99, tick, [new(state, 0)]);
        }

        Assert.That(history.Add(At(uint.MaxValue - 1)), Is.True);
        Assert.That(history.Add(At(uint.MaxValue - 1)), Is.False);
        Assert.That(history.Add(At(uint.MaxValue - 2)), Is.False);
        Assert.That(history.Add(At((ulong)uint.MaxValue + 1)), Is.True);
        Assert.That(history.Add(new WorldSnapshot(100, (ulong)uint.MaxValue + 2, At((ulong)uint.MaxValue + 2).Vehicles)), Is.False);
        Assert.That(history.Add(At((ulong)uint.MaxValue + 1 + 0x80000000)), Is.False);
        for (ulong tick = (ulong)uint.MaxValue + 2; tick < (ulong)uint.MaxValue + 102; tick++)
        {
            history.Add(At(tick));
        }

        Assert.That(history.Snapshots.Count, Is.EqualTo(SnapshotHistory.Capacity));
    }

    /// <summary>Snapshot encoding preserves HP, damage attribution, collision gates, drift and boost replay state.</summary>
    [Test]
    public void SnapshotPreservesGameplayMemory()
    {
        var physics = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(2, 0, -10), Vector3.UnitY);
        var movement = new VehicleState(100, physics, true, true, 20, 10);
        var damage = new VehicleDamageState(100, 80, new DamageEvent(2, 99, 20, new DamageContext("collision", 7, "vehicle"), false), 99);
        var state = new VehicleSnapshot(2, 3, movement, damage, physics);
        var snapshot = new WorldSnapshot(99, 100, [new(state, 15)]);
        var decoded = VehicleNetworkCodec.DecodeSnapshot(VehicleNetworkCodec.EncodeSnapshot(snapshot)).Vehicles[0];
        Assert.That(decoded.State.Movement, Is.EqualTo(movement));
        Assert.That(decoded.State.Damage, Is.EqualTo(damage));
        Assert.That(decoded.State.LifeId, Is.EqualTo(3));
        Assert.That(decoded.AcknowledgedInput, Is.EqualTo(15));
    }

    private static InputFrame Drive(short steering = 0) => new(0, steering, 65535, 0, 0, 0, 0);

    private static VehicleObservation Observe(VehicleSnapshot state)
    {
        VehiclePhysicsState p = state.Movement.Physics;
        Vector3 position = p.Position + (p.LinearVelocity / 60);
        position.Y = 1;
        return new VehicleObservation(new VehiclePhysicsState(position, p.Orientation, new Vector3(p.LinearVelocity.X, 0, p.LinearVelocity.Z), p.AngularVelocity), Vector3.UnitY);
    }
}
