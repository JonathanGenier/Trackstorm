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

    /// <summary>Only newer complete publications from the connected host and current generation are accepted.</summary>
    [Test]
    public void PropPublicationsRespectAuthorityGenerationAndOrdering()
    {
        using var gateway = ConnectedGateway();
        var driver = new VehicleNetworkDriver(gateway, 0, ServerPeer);
        gateway.Receive(new TransportMessage(ServerPeer, VehicleNetworkCodec.EncodeWelcome(Session, 2), TransportDelivery.Reliable));
        driver.Advance(default, Observe);
        var body = new VehiclePhysicsState(Vector3.One, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        byte[] Payload(ulong session, ulong tick) => VehicleNetworkCodec.EncodeProps(new Trackstorm.Core.Arenas.ArenaPropSnapshot(session, tick, new[] { body, body, body }));
        int publications = 0;
        driver.PropsReceived += _ => publications++;
        gateway.Receive(new TransportMessage(ServerPeer, Payload(Session, 30), TransportDelivery.Unreliable));
        driver.Advance(default, Observe);
        Assert.That(driver.PropSnapshot!.Tick, Is.EqualTo(30));
        gateway.Receive(new TransportMessage(ServerPeer, Payload(Session, 29), TransportDelivery.Unreliable));
        gateway.Receive(new TransportMessage(ServerPeer, Payload(Session, 30), TransportDelivery.Unreliable));
        gateway.Receive(new TransportMessage(ServerPeer, Payload(Session + 1, 31), TransportDelivery.Unreliable));
        gateway.Receive(new TransportMessage(ServerPeer + 1, Payload(Session, 31), TransportDelivery.Unreliable));
        gateway.Receive(new TransportMessage(ServerPeer, Payload(Session, 31), TransportDelivery.Reliable));
        driver.Advance(default, Observe);
        Assert.That(publications, Is.EqualTo(1));
        Assert.That(driver.PropSnapshot.Tick, Is.EqualTo(30));
        Assert.That(driver.RejectedPackets, Is.EqualTo(5));
        gateway.Receive(new TransportMessage(ServerPeer, Payload(Session, 32), TransportDelivery.Unreliable));
        driver.Advance(default, Observe);
        Assert.That(publications, Is.EqualTo(2));

        using var hostGateway = ConnectedGateway();
        var host = new VehicleNetworkDriver(hostGateway, Session) { ObserveProps = () => new[] { body, body, body } };
        for (int index = 0; index < 3; index++)
        {
            host.Advance(default, Observe);
        }

        Assert.That(hostGateway.Sent.Count(message => !Trackstorm.Core.Items.ItemCodec.IsItem(message.Payload.Span) && VehicleNetworkCodec.Kind(message.Payload.Span) == VehicleNetworkCodec.Props), Is.EqualTo(1));
        hostGateway.Receive(new TransportMessage(ServerPeer, Payload(Session, 100), TransportDelivery.Unreliable));
        host.Advance(default, Observe);
        Assert.That(host.RejectedPackets, Is.EqualTo(1));
        Assert.That(host.PropSnapshot!.Tick, Is.EqualTo(3));
    }

    /// <summary>Clients accept item state only reliably from the established host in the current generation.</summary>
    [Test]
    public void ItemPublicationsRejectForgedStaleAndUnreliableState()
    {
        using var gateway = ConnectedGateway();
        var driver = new VehicleNetworkDriver(gateway, 0, ServerPeer);
        gateway.Receive(new TransportMessage(ServerPeer, VehicleNetworkCodec.EncodeWelcome(Session, 2), TransportDelivery.Reliable));
        driver.Advance(default, Observe);
        var host = new HostVehicleSession(Session);
        host.Join(ServerPeer);
        host.Items.Grant(host.World, 2, Trackstorm.Core.Items.HeldItem.Wrench);
        host.Step(default, Observe);
        byte[] payload = Trackstorm.Core.Items.ItemCodec.EncodeState(new Trackstorm.Core.Items.ItemPublication(1, host.Snapshot(), host.Items.Slots, host.Items.Missiles, host.Items.Events));
        int events = 0;
        driver.ItemsReceived += _ => events++;
        gateway.Receive(new TransportMessage(ServerPeer + 1, payload, TransportDelivery.Reliable));
        gateway.Receive(new TransportMessage(ServerPeer, payload, TransportDelivery.Unreliable));
        driver.Advance(default, Observe);
        Assert.That(driver.ItemState, Is.Null);
        gateway.Receive(new TransportMessage(ServerPeer, payload, TransportDelivery.Reliable));
        driver.Advance(default, Observe);
        Assert.That(driver.LocalItem!.Item, Is.EqualTo(Trackstorm.Core.Items.HeldItem.Wrench));
        Assert.That(driver.RequestItemUse(), Is.True);
        Assert.That(gateway.Sent[^1].Delivery, Is.EqualTo(TransportDelivery.Reliable));
        Assert.That(driver.LocalItem.Item, Is.EqualTo(Trackstorm.Core.Items.HeldItem.Wrench), "Request cannot predict authoritative consumption.");
        gateway.Receive(new TransportMessage(ServerPeer, payload, TransportDelivery.Reliable));
        driver.Advance(default, Observe);
        Assert.That(events, Is.EqualTo(1));
        Assert.That(driver.RejectedPackets, Is.EqualTo(3));
    }

    /// <summary>A native close between Poll and Send removes that peer without terminating host simulation.</summary>
    [Test]
    public void HostSurvivesNativeSendClosure()
    {
        var gateway = ConnectedGateway();
        var host = new VehicleNetworkDriver(gateway, 99);
        host.Advance(default, Observe);
        gateway.FailSend = true;
        for (int tick = 0; tick < 6; tick++)
        {
            host.Advance(default, Observe);
        }

        Assert.That(host.Failure, Is.Empty);
        Assert.That(gateway.DisconnectedPeers, Is.EqualTo(new[] { ServerPeer }));
        Assert.That(host.Host!.World.State.Vehicles.Count, Is.EqualTo(1));
        Assert.That(host.Host.World.State.Tick, Is.EqualTo(7));
    }

    /// <summary>Reliable lifecycle events survive newer unreliable poses without rewinding gameplay or duplicating hooks.</summary>
    [Test]
    public void DelayedReliableDeathDoesNotRewindRespawn()
    {
        using var gateway = ConnectedGateway();
        var client = new VehicleNetworkDriver(gateway, 0, ServerPeer);
        var authority = new HostVehicleSession(Session, respawnConfiguration: new RespawnConfiguration { DelayTicks = 2 });
        authority.Join(ServerPeer);
        var boundaries = new List<WorldSnapshot>();
        client.LifecycleReceived += boundaries.Add;
        gateway.Receive(new TransportMessage(ServerPeer, VehicleNetworkCodec.EncodeWelcome(Session, 2), TransportDelivery.Reliable));
        authority.Step(default, state => state.VehicleId == 2
            ? new VehicleObservation(state.ObservedPhysics, Vector3.UnitY, [new VehicleContact(-Vector3.UnitX * 100, Vector3.UnitX, 0, 0)]) : Observe(state));
        byte[] death = VehicleNetworkCodec.EncodeSnapshot(authority.Snapshot());
        authority.Step(default, Observe);
        byte[] waiting = VehicleNetworkCodec.EncodeSnapshot(authority.Snapshot());
        authority.Step(default, Observe);
        byte[] respawn = VehicleNetworkCodec.EncodeSnapshot(authority.Snapshot());
        gateway.Receive(new TransportMessage(ServerPeer, respawn, TransportDelivery.Unreliable));
        client.Advance(default, Observe);
        ulong latest = client.Latest!.Tick;
        gateway.Receive(new TransportMessage(ServerPeer, death, TransportDelivery.Reliable));
        gateway.Receive(new TransportMessage(ServerPeer, waiting, TransportDelivery.Reliable));
        gateway.Receive(new TransportMessage(ServerPeer, respawn, TransportDelivery.Reliable));
        gateway.Receive(new TransportMessage(ServerPeer, death, TransportDelivery.Reliable));
        client.Advance(default, Observe);
        Assert.That(boundaries.Select(world => world.Vehicles.Single(vehicle => vehicle.State.VehicleId == 2).State.Lifecycle), Is.EqualTo(new[] { VehicleLifecycle.Dead, VehicleLifecycle.Respawning, VehicleLifecycle.Alive }));
        Assert.That(client.Latest.Tick, Is.EqualTo(latest));
        Assert.That(client.LocalState!.LifeId, Is.EqualTo(2));
        Assert.That(client.LocalState.CanInteract, Is.True);
        gateway.Receive(new TransportMessage(ServerPeer + 1, death, TransportDelivery.Reliable));
        client.Advance(default, Observe);
        Assert.That(boundaries.Count, Is.EqualTo(3));
    }

    /// <summary>Collision deaths publish reliably even when no inventory changes and the tick misses snapshot cadence.</summary>
    [Test]
    public void HostPublishesEveryLifecycleBoundaryReliably()
    {
        using var gateway = ConnectedGateway();
        var host = new VehicleNetworkDriver(gateway, Session);
        host.Advance(default, Observe);
        gateway.Sent.Clear();
        host.Advance(default, state => state.VehicleId == 2
            ? new VehicleObservation(state.ObservedPhysics, Vector3.UnitY, [new VehicleContact(-Vector3.UnitX * 100, Vector3.UnitX, 0, 0)]) : Observe(state));
        TransportMessage death = gateway.Sent.Single(message => VehicleNetworkCodec.Kind(message.Payload.Span) == VehicleNetworkCodec.Snapshot);
        Assert.That(death.Delivery, Is.EqualTo(TransportDelivery.Reliable));
        Assert.That(VehicleNetworkCodec.DecodeSnapshot(death.Payload.Span).Vehicles.Single(vehicle => vehicle.State.VehicleId == 2).State.Lifecycle, Is.EqualTo(VehicleLifecycle.Dead));
        gateway.Sent.Clear();
        host.Advance(default, Observe);
        Assert.That(gateway.Sent.Any(message => message.Delivery == TransportDelivery.Reliable && VehicleNetworkCodec.DecodeSnapshot(message.Payload.Span).Vehicles.Single(vehicle => vehicle.State.VehicleId == 2).State.Lifecycle == VehicleLifecycle.Respawning), Is.True);
    }

    /// <summary>A same-tick leave/join still reliably initializes the new peer despite unchanged roster size.</summary>
    [Test]
    public void ReplacementPeerReceivesReliableCurrentLifecycleAndItems()
    {
        using var gateway = ConnectedGateway();
        var host = new VehicleNetworkDriver(gateway, Session);
        host.Advance(default, Observe);
        gateway.Sent.Clear();
        gateway.Disconnect(ServerPeer);
        gateway.ConnectPeer(ServerPeer + 1);
        host.Advance(default, Observe);
        var replacement = gateway.Sent.Where(message => message.RemotePeerId == ServerPeer + 1 && message.Delivery == TransportDelivery.Reliable).ToArray();
        Assert.That(replacement.Any(message => Trackstorm.Core.Items.ItemCodec.IsItem(message.Payload.Span)), Is.True);
        TransportMessage lifecycle = replacement.Single(message => !Trackstorm.Core.Items.ItemCodec.IsItem(message.Payload.Span) && VehicleNetworkCodec.Kind(message.Payload.Span) == VehicleNetworkCodec.Snapshot);
        Assert.That(VehicleNetworkCodec.DecodeSnapshot(lifecycle.Payload.Span).Vehicles.Select(vehicle => vehicle.State.VehicleId), Is.EqualTo(new ulong[] { 1, 3 }));
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
        internal bool FailSend { get; set; }

        public void Disconnect(ulong peerId)
        {
            DisconnectedPeers.Add(peerId);
            _connections[peerId] = TransportConnectionState.Disconnected;
        }

        public void Send(TransportMessage message)
        {
            if (FailSend)
            {
                throw new InvalidOperationException("Native peer closed before send.");
            }

            Sent.Add(message);
        }

        public bool TryReceive(out TransportMessage message) => _received.TryDequeue(out message);
        public void Dispose()
        {
        }

        public void Listen(TransportEndpoint endpoint) => throw new NotSupportedException();
        public ulong Connect(TransportEndpoint endpoint) => throw new NotSupportedException();
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
        internal void ConnectPeer(ulong peer) => _connections.Add(peer, TransportConnectionState.Connected);
    }
}
