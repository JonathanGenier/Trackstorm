using System.Numerics;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Transport.Tests;

/// <summary>Fast client-driver checks that do not load native transport or a Godot scene tree.</summary>
[TestFixture]
internal sealed class VehicleNetworkDriverTests
{
    private const ulong ServerPeer = 42;
    private const ulong Session = 99;

    /// <summary>Only clients expose snapshot freshness; host diagnostics render the metric as unavailable.</summary>
    [Test]
    public void SnapshotAgeIsClientOnly()
    {
        using var hostGateway = new DriverGateway();
        var host = new VehicleNetworkDriver(hostGateway, Session);
        host.Advance(default, Observe);
        host.Advance(default, Observe);
        Assert.That(host.SnapshotAge, Is.Null);
        Assert.That(NetworkVehicleArena.FormatSnapshotAge(host.SnapshotAge), Is.EqualTo("N/A"));

        using var clientGateway = ConnectedGateway();
        var client = new VehicleNetworkDriver(clientGateway, 0, ServerPeer);
        client.Advance(default, Observe);
        Assert.That(client.SnapshotAge, Is.EqualTo(1.0 / HostVehicleSession.TickRate).Within(0.000001));
    }

    /// <summary>Post-assignment inputs are sent before authority arrives, then acknowledged and replayed exactly once.</summary>
    [Test]
    public void DelayedFirstSnapshotAdoptsAcknowledgesAndReplaysQueuedInputs()
    {
        using var gateway = ConnectedGateway();
        var client = new VehicleNetworkDriver(gateway, 0, ServerPeer);
        InputFrame first = Drive(1000, InputButtons.Drift, InputButtons.Drift, 0);
        InputFrame second = Drive(2000, 0, 0, InputButtons.Drift);
        InputFrame third = Drive(3000, 0, InputButtons.UseItem, InputButtons.UseItem);
        InputFrame fourth = Drive(4000);

        client.Advance(first, Observe);
        Assert.That(client.Inputs, Is.Null, "Input must not be sequenced or sent before a trusted welcome.");
        Assert.That(gateway.Sent, Is.Empty);

        gateway.Receive(new TransportMessage(ServerPeer, VehicleNetworkCodec.EncodeWelcome(Session, 2), TransportDelivery.Reliable));
        client.Advance(first, Observe);
        client.Advance(second, Observe);

        Assert.That(client.Prediction, Is.Null, "The client must not invent state before the first host snapshot.");
        Assert.That(client.Inputs!.Pending.Select(input => input.Sequence), Is.EqualTo(new uint[] { 1, 2 }));
        Assert.That(DecodeLastInputs(gateway).Select(input => input.Sequence), Is.EqualTo(new uint[] { 1, 2 }));

        var authoritativeHost = new HostVehicleSession(Session);
        Assert.That(authoritativeHost.Join(ServerPeer), Is.EqualTo(2));
        Assert.That(authoritativeHost.Receive(ServerPeer, Session, client.Inputs.GetRedundancy()), Is.True);
        authoritativeHost.Step(default, Observe);
        WorldSnapshot firstSnapshot = authoritativeHost.Snapshot();
        ReplicatedVehicle local = firstSnapshot.Vehicles.Single(vehicle => vehicle.State.VehicleId == 2);
        Assert.That(local.AcknowledgedInput, Is.EqualTo(1));

        gateway.Receive(new TransportMessage(ServerPeer, VehicleNetworkCodec.EncodeSnapshot(firstSnapshot), TransportDelivery.Unreliable));
        client.Advance(third, Observe);

        var expected = new PredictedVehicle(local);
        expected.Predict(second, Observe);
        expected.Predict(third, Observe);
        Assert.That(client.Prediction, Is.Not.Null);
        Assert.That(client.Prediction!.History, Is.SameAs(client.Inputs));
        Assert.That(client.Prediction.History.LastAcknowledged, Is.EqualTo(1));
        Assert.That(client.Prediction.History.Pending.Select(input => input.Sequence), Is.EqualTo(new uint[] { 2, 3 }));
        Assert.That(client.Prediction.State, Is.EqualTo(expected.State), "Unacknowledged commands and their edges must replay once in sequence order.");
        Assert.That(client.ReceivedSnapshots, Is.EqualTo(1));
        Assert.That(client.SnapshotAge, Is.Zero);

        int rejectedBeforeDuplicate = client.RejectedPackets;
        gateway.Receive(new TransportMessage(ServerPeer, VehicleNetworkCodec.EncodeSnapshot(firstSnapshot), TransportDelivery.Unreliable));
        expected.Predict(fourth, Observe);
        client.Advance(fourth, Observe);

        Assert.That(client.RejectedPackets, Is.EqualTo(rejectedBeforeDuplicate + 1));
        Assert.That(client.ReceivedSnapshots, Is.EqualTo(1));
        Assert.That(client.Prediction.History.Pending.Select(input => input.Sequence), Is.EqualTo(new uint[] { 2, 3, 4 }));
        Assert.That(client.Prediction.State, Is.EqualTo(expected.State), "A duplicate first snapshot must not replay retained inputs again.");
    }

    /// <summary>A missing first snapshot cannot grow the post-welcome queue beyond the established replay bound.</summary>
    [Test]
    public void DelayedFirstSnapshotHistoryExhaustionStopsSession()
    {
        using var gateway = ConnectedGateway();
        gateway.Receive(new TransportMessage(ServerPeer, VehicleNetworkCodec.EncodeWelcome(Session, 2), TransportDelivery.Reliable));
        var client = new VehicleNetworkDriver(gateway, 0, ServerPeer);
        for (int i = 0; i < InputHistory.Capacity; i++)
        {
            client.Advance(Drive((short)i), Observe);
        }

        Assert.That(client.Prediction, Is.Null);
        Assert.That(client.Inputs!.Pending.Count, Is.EqualTo(InputHistory.Capacity));
        Assert.That(gateway.Sent.Count, Is.EqualTo(InputHistory.Capacity));

        client.Advance(Drive(), Observe);

        Assert.That(client.Inputs.Pending.Count, Is.EqualTo(InputHistory.Capacity));
        Assert.That(client.Failure, Does.Contain("reconnect to resynchronize"));
        Assert.That(gateway.DisconnectedPeers, Is.EqualTo(new[] { ServerPeer }));
        Assert.That(gateway.Sent.Count, Is.EqualTo(InputHistory.Capacity));
    }

    private static DriverGateway ConnectedGateway() => new(ServerPeer, TransportConnectionState.Connected);

    private static SequencedInput[] DecodeLastInputs(DriverGateway gateway) => VehicleNetworkCodec.DecodeInputs(gateway.Sent[^1].Payload.Span).Inputs;

    private static InputFrame Drive(short steering = 0, InputButtons held = 0, InputButtons pressed = 0, InputButtons released = 0) =>
        new(0, steering, ushort.MaxValue, 0, held, pressed, released);

    private static VehicleObservation Observe(VehicleSnapshot state)
    {
        VehiclePhysicsState physics = state.Movement.Physics;
        Vector3 position = physics.Position + (physics.LinearVelocity / HostVehicleSession.TickRate);
        position.Y = 1;
        return new VehicleObservation(new VehiclePhysicsState(position, physics.Orientation, new Vector3(physics.LinearVelocity.X, 0, physics.LinearVelocity.Z), physics.AngularVelocity), Vector3.UnitY);
    }

    private sealed class DriverGateway : ITransportGateway
    {
        private readonly Dictionary<ulong, TransportConnectionState> _connections = new();
        private readonly Queue<TransportMessage> _received = new();

        internal DriverGateway(ulong peer = 0, TransportConnectionState? state = null)
        {
            if (state.HasValue)
            {
                _connections.Add(peer, state.Value);
            }
        }

        public event Action<TransportConnectionChange>? ConnectionChanged
        {
            add { }
            remove { }
        }

        public bool IsListening => false;
        public IReadOnlyDictionary<ulong, TransportConnectionState> Connections => _connections;
        public TransportConnectionState ConnectionState => _connections.Values.FirstOrDefault();
        internal List<TransportMessage> Sent { get; } = [];
        internal List<ulong> DisconnectedPeers { get; } = [];

        public void Disconnect(ulong peerId)
        {
            DisconnectedPeers.Add(peerId);
            _connections[peerId] = TransportConnectionState.Disconnected;
        }

        public void Send(TransportMessage message) => Sent.Add(message);
        public bool TryReceive(out TransportMessage message) => _received.TryDequeue(out message);
        public void Dispose()
        {
        }

        public void Listen(string address) => throw new NotSupportedException();
        public ulong Connect(string address) => throw new NotSupportedException();
        public void Poll()
        {
        }

        public void Stop()
        {
        }

        public void ConfigureSimulation(NetworkSimulation simulation)
        {
        }

        public TransportStatistics GetStatistics(ulong peerId) => default;

        internal void Receive(TransportMessage message) => _received.Enqueue(message);
    }
}
