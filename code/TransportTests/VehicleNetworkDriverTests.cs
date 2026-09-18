using System.Numerics;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Transport.Tests;

/// <summary>Fast client-driver checks that do not load native transport or a Godot scene tree.</summary>
[TestFixture]
internal sealed partial class VehicleNetworkDriverTests
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
        gateway.Receive(new TransportMessage(ServerPeer, Core.Development.GameplayConfigurationCodec.Encode(Session, new(0, new())), TransportDelivery.Reliable));
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
        gateway.Receive(new TransportMessage(ServerPeer, Core.Development.GameplayConfigurationCodec.Encode(Session, new(0, new())), TransportDelivery.Reliable));
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
        gateway.Receive(new TransportMessage(ServerPeer, Core.Development.GameplayConfigurationCodec.Encode(Session, new(0, new())), TransportDelivery.Reliable));
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

        Assert.That(hostGateway.Sent.Count(message => !Trackstorm.Core.Items.ItemCodec.IsItem(message.Payload.Span) && !MatchCodec.IsMatch(message.Payload.Span) && !Trackstorm.Core.Development.GameplayConfigurationCodec.IsConfiguration(message.Payload.Span) && VehicleNetworkCodec.Kind(message.Payload.Span) == VehicleNetworkCodec.Props), Is.EqualTo(1));
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
        gateway.Receive(new TransportMessage(ServerPeer, Core.Development.GameplayConfigurationCodec.Encode(Session, new(0, new())), TransportDelivery.Reliable));
        driver.Advance(default, Observe);
        var host = new HostVehicleSession(Session);
        host.Join(ServerPeer);
        host.RegisterSpawns(Trackstorm.Core.Arenas.PrototypeArena.Configuration);
        host.Items.Grant(host.World, 2, Trackstorm.Core.Items.HeldItem.Wrench);
        host.Step(default, Observe);
        byte[] payload = Trackstorm.Core.Items.ItemCodec.EncodeState(new Trackstorm.Core.Items.ItemPublication(1, host.Snapshot(), host.Items.Slots, host.Items.Missiles, host.Items.Events, host.Spawns!.States));
        int events = 0;
        driver.ItemsReceived += _ => events++;
        gateway.Receive(new TransportMessage(ServerPeer + 1, payload, TransportDelivery.Reliable));
        gateway.Receive(new TransportMessage(ServerPeer, payload, TransportDelivery.Unreliable));
        driver.Advance(default, Observe);
        Assert.That(driver.ItemState, Is.Null);
        gateway.Receive(new TransportMessage(ServerPeer, payload, TransportDelivery.Reliable));
        driver.Advance(default, Observe);
        Assert.That(driver.ItemState!.Spawns, Is.EqualTo(host.Spawns!.States));
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
        gateway.Receive(new TransportMessage(ServerPeer, Core.Development.GameplayConfigurationCodec.Encode(Session, new(0, new())), TransportDelivery.Reliable));
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
        TransportMessage death = gateway.Sent.Single(message => !MatchCodec.IsMatch(message.Payload.Span) && !Trackstorm.Core.Development.GameplayConfigurationCodec.IsConfiguration(message.Payload.Span) && VehicleNetworkCodec.Kind(message.Payload.Span) == VehicleNetworkCodec.Snapshot);
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
        TransportMessage lifecycle = replacement.Single(message => !Trackstorm.Core.Items.ItemCodec.IsItem(message.Payload.Span) && !MatchCodec.IsMatch(message.Payload.Span) && !Trackstorm.Core.Development.GameplayConfigurationCodec.IsConfiguration(message.Payload.Span) && VehicleNetworkCodec.Kind(message.Payload.Span) == VehicleNetworkCodec.Snapshot);
        Assert.That(VehicleNetworkCodec.DecodeSnapshot(lifecycle.Payload.Span).Vehicles.Select(vehicle => vehicle.State.VehicleId), Is.EqualTo(new ulong[] { 1, 3 }));
    }

    /// <summary>Scores use reliable host authority and independent revisions, even after newer movement arrives.</summary>
    [Test]
    public void MatchPublicationsRejectForgedStaleUnreliableAndPostFinishState()
    {
        using var gateway = ConnectedGateway();
        gateway.ConnectPeer(77);
        var client = new VehicleNetworkDriver(gateway, 0, ServerPeer);
        gateway.Receive(new TransportMessage(ServerPeer, VehicleNetworkCodec.EncodeWelcome(Session, 2), TransportDelivery.Reliable));
        gateway.Receive(new TransportMessage(ServerPeer, Core.Development.GameplayConfigurationCodec.Encode(Session, new(0, new())), TransportDelivery.Reliable));
        int callbacks = 0;
        client.MatchReceived += _ => callbacks++;
        client.Advance(default, Observe);
        var active = new MatchState(1, 1, 5, MatchPhase.Active, null, null, [new PlayerScore(1, 0, 0, 0, 0), new PlayerScore(2, 0, 0, 0, 0)]);
        byte[] payload = MatchCodec.Encode(Session, active);
        gateway.Receive(new TransportMessage(77, payload, TransportDelivery.Reliable));
        gateway.Receive(new TransportMessage(ServerPeer, payload, TransportDelivery.Unreliable));
        gateway.Receive(new TransportMessage(ServerPeer, MatchCodec.Encode(Session + 1, active), TransportDelivery.Reliable));
        client.Advance(default, Observe);
        Assert.That(client.Match, Is.Null);
        Assert.That(callbacks, Is.Zero);
        gateway.Receive(new TransportMessage(ServerPeer, payload, TransportDelivery.Reliable));
        gateway.Receive(new TransportMessage(ServerPeer, payload, TransportDelivery.Reliable));
        client.Advance(default, Observe);
        Assert.That(callbacks, Is.EqualTo(1));
        var host = new HostVehicleSession(Session);
        host.Join(ServerPeer);
        for (int tick = 0; tick < 10; tick++)
        {
            host.Step(default, Observe);
        }

        gateway.Receive(new TransportMessage(ServerPeer, VehicleNetworkCodec.EncodeSnapshot(host.Snapshot()), TransportDelivery.Unreliable));
        var final = new MatchState(2, 2, 5, MatchPhase.Finished, null, 1, [new PlayerScore(1, 5, 0, 1, 0), new PlayerScore(2, 0, 5, 0, 5)], [new ScoredDeath(2, 5, 1)]);
        gateway.Receive(new TransportMessage(ServerPeer, MatchCodec.Encode(Session, final), TransportDelivery.Reliable));
        client.Advance(default, Observe);
        Assert.That(client.Latest!.Tick, Is.GreaterThan(client.Match!.Tick));
        Assert.That(client.Match.Winner, Is.EqualTo(1));
        Assert.That(callbacks, Is.EqualTo(2));
        var replaced = new MatchState(3, 3, 5, MatchPhase.Finished, null, 2, [new PlayerScore(1, 0, 5, 0, 5), new PlayerScore(2, 5, 0, 1, 0)]);
        gateway.Receive(new TransportMessage(ServerPeer, MatchCodec.Encode(Session, replaced), TransportDelivery.Reliable));
        client.Advance(default, Observe);
        Assert.That(client.Match.Winner, Is.EqualTo(1));
        Assert.That(callbacks, Is.EqualTo(2));
    }

    /// <summary>The host rejects client score claims and sends current totals to a replacement peer reliably.</summary>
    [Test]
    public void HostOwnsMatchAndReliablyInitializesJoiningPlayers()
    {
        using var gateway = ConnectedGateway();
        var host = new VehicleNetworkDriver(gateway, Session);
        host.Advance(default, Observe);
        var publication = gateway.Sent.Single(message => MatchCodec.IsMatch(message.Payload.Span));
        Assert.That(publication.Delivery, Is.EqualTo(TransportDelivery.Reliable));
        Assert.That(MatchCodec.Decode(publication.Payload.Span).State.Players.Count, Is.EqualTo(2));
        MatchState before = host.Match!;
        gateway.Receive(new TransportMessage(ServerPeer, publication.Payload, TransportDelivery.Reliable));
        host.Advance(default, Observe);
        Assert.That(host.Match, Is.SameAs(before));
        Assert.That(host.RejectedPackets, Is.GreaterThan(0));
        gateway.Sent.Clear();
        gateway.Disconnect(ServerPeer);
        gateway.ConnectPeer(77);
        host.Advance(default, Observe);
        Assert.That(gateway.Sent.Any(message => message.RemotePeerId == 77 && message.Delivery == TransportDelivery.Reliable && MatchCodec.IsMatch(message.Payload.Span)), Is.True);
        Assert.That(host.Match!.Players.Select(player => player.Player), Is.EqualTo(new ulong[] { 1, 2, 3 }));
    }

    /// <summary>Reliable host configuration precedes gameplay, including a peer admitted after live tuning.</summary>
    [Test]
    public void LiveConfigurationReplicatesToExistingAndNewPeersAndClientCannotMutate()
    {
        using var hostGateway = ConnectedGateway();
        var host = new VehicleNetworkDriver(hostGateway, Session);
        using var clientGateway = ConnectedGateway();
        var client = new VehicleNetworkDriver(clientGateway, 0, ServerPeer);
        void Transfer()
        {
            foreach (var message in hostGateway.Sent.Where(message => message.RemotePeerId == ServerPeer))
            {
                clientGateway.Receive(message);
            }

            hostGateway.Sent.Clear();
            client.Advance(default, Observe);
        }

        host.Advance(default, Observe);
        Transfer();
        var edits = new Dictionary<string, double> { ["vehicle.acceleration"] = 4, ["damage.max_hp"] = 250, ["items.missile_speed"] = 90 };
        Assert.That(client.TryConfigure(edits, out _), Is.False);
        Assert.That(host.TryConfigure(edits, out var error), Is.True, error);
        host.Advance(default, Observe);
        Transfer();
        Assert.That(client.Configuration, Is.EqualTo(host.Configuration));
        Assert.That(client.LocalState!.Damage.MaxHP, Is.EqualTo(250));
        Assert.That(client.Latest!.ConfigurationRevision, Is.EqualTo(host.Configuration.Revision));
        hostGateway.ConnectPeer(77);
        host.Advance(default, Observe);
        var publication = hostGateway.Sent.Single(message => message.RemotePeerId == 77 && Core.Development.GameplayConfigurationCodec.IsConfiguration(message.Payload.Span));
        Assert.That(Core.Development.GameplayConfigurationCodec.Decode(publication.Payload.Span).State, Is.EqualTo(host.Configuration));
        Assert.That(host.Host!.World.GetVehicle(3).Damage.MaxHP, Is.EqualTo(250));
    }

    /// <summary>Stale, conflicting duplicate, wrong-peer and unreliable tuning cannot rewind a client.</summary>
    [Test]
    public void ConfigurationPublicationsAreOrderedIdempotentAndHostOnly()
    {
        using var gateway = ConnectedGateway();
        var client = new VehicleNetworkDriver(gateway, 0, ServerPeer);
        gateway.Receive(new TransportMessage(ServerPeer, VehicleNetworkCodec.EncodeWelcome(Session, 2), TransportDelivery.Reliable));
        gateway.Receive(new TransportMessage(ServerPeer, Core.Development.GameplayConfigurationCodec.Encode(Session, new(0, new())), TransportDelivery.Reliable));
        client.Advance(default, Observe);
        int changes = 0;
        client.ConfigurationChanged += _ => changes++;
        byte[] Payload(ulong revision, float acceleration) => Core.Development.GameplayConfigurationCodec.Encode(Session, new(revision, new() { Vehicle = new() { Acceleration = acceleration } }));
        gateway.Receive(new TransportMessage(ServerPeer, Payload(5, 20), TransportDelivery.Reliable));
        gateway.Receive(new TransportMessage(ServerPeer, Payload(5, 20), TransportDelivery.Reliable));
        client.Advance(default, Observe);
        Assert.That(changes, Is.EqualTo(1));
        int rejected = client.RejectedPackets;
        gateway.Receive(new TransportMessage(ServerPeer, Payload(4, 11), TransportDelivery.Reliable));
        gateway.Receive(new TransportMessage(ServerPeer, Payload(5, 11), TransportDelivery.Reliable));
        gateway.Receive(new TransportMessage(ServerPeer, Payload(6, 11), TransportDelivery.Unreliable));
        gateway.Receive(new TransportMessage(ServerPeer + 1, Payload(6, 11), TransportDelivery.Reliable));
        client.Advance(default, Observe);
        Assert.That(client.RejectedPackets, Is.EqualTo(rejected + 4));
        Assert.That(client.Configuration.Revision, Is.EqualTo(5));
        Assert.That(client.Configuration.Configuration.Vehicle.Acceleration, Is.EqualTo(20));
        Assert.That(changes, Is.EqualTo(1));
        using var hostGateway = ConnectedGateway();
        var host = new VehicleNetworkDriver(hostGateway, Session);
        hostGateway.Receive(new TransportMessage(ServerPeer, Payload(99, 50), TransportDelivery.Reliable));
        host.Advance(default, Observe);
        Assert.That(host.Configuration.Revision, Is.Zero);
        Assert.That(host.RejectedPackets, Is.EqualTo(1));
    }

    /// <summary>Unreliable revision-zero movement cannot initialize prediction before persisted host tuning arrives.</summary>
    [Test]
    public void FirstSnapshotWaitsForActualHostConfiguration()
    {
        using var gateway = ConnectedGateway();
        var client = new VehicleNetworkDriver(gateway, 0, ServerPeer);
        var tuning = new Core.Development.GameplayConfiguration { Vehicle = new() { Acceleration = 4 } };
        var host = new HostVehicleSession(Session, configuration: tuning);
        host.Join(ServerPeer);
        host.Step(default, Observe);
        gateway.Receive(new TransportMessage(ServerPeer, VehicleNetworkCodec.EncodeWelcome(Session, 2), TransportDelivery.Reliable));
        gateway.Receive(new TransportMessage(ServerPeer, VehicleNetworkCodec.EncodeSnapshot(host.Snapshot()), TransportDelivery.Unreliable));
        client.Advance(default, Observe);
        Assert.That(client.Prediction, Is.Null);
        Assert.That(client.RejectedPackets, Is.EqualTo(1));
        gateway.Receive(new TransportMessage(ServerPeer, Core.Development.GameplayConfigurationCodec.Encode(Session, host.Configuration), TransportDelivery.Reliable));
        gateway.Receive(new TransportMessage(ServerPeer, VehicleNetworkCodec.EncodeSnapshot(host.Snapshot()), TransportDelivery.Reliable));
        client.Advance(default, Observe);
        Assert.That(client.Prediction, Is.Not.Null);
        Assert.That(client.Configuration, Is.EqualTo(host.Configuration));
    }

    /// <summary>Native-free provider checks never call unsupported network simulation or expose raw identities.</summary>
    [Test]
    public void DeveloperCapabilitiesAndDiagnosticsExcludeUnsupportedOperationsAndRawIdentity()
    {
        using var gateway = ConnectedGateway();
        Assert.That(Client.Development.NetworkSimulationControl.TryApply(gateway, true, new NetworkSimulation(50)), Is.False);
        Assert.That(gateway.SimulationCalls, Is.Zero);
        gateway.SimulationSupported = true;
        Assert.That(Client.Development.NetworkSimulationControl.TryApply(gateway, false, new NetworkSimulation(50)), Is.False);
        Assert.That(Client.Development.NetworkSimulationControl.TryApply(gateway, true, new NetworkSimulation(50)), Is.True);
        Assert.That(gateway.SimulationCalls, Is.EqualTo(1));
        gateway.SimulationRejected = true;
        Assert.That(Client.Development.NetworkSimulationControl.TryApply(gateway, true, new NetworkSimulation(50)), Is.False);
        string raw = "1234567890abcdef1234567890abcdef";
        var diagnostic = Client.Development.DeveloperDiagnostics.Identity(Client.Online.OnlineIdentityState.LoggedIn, new Client.Online.OnlineProductUserId(raw));
        Assert.That(diagnostic, Does.Contain("LoggedIn").And.Contain("puid#").And.Not.Contain(raw));
        Assert.That(Client.Development.DeveloperDiagnostics.Identity(Client.Online.OnlineIdentityState.Failed, null), Does.Contain("Failed").And.Contain("unavailable"));
    }

    /// <summary>Event replication requires the current host and connection, preserving identity and original time.</summary>
    [Test]
    public void EventReplicationRejectsDuplicatesForgedSendersAndOldGenerations()
    {
        using var hostWire = new DriverGateway(2, TransportConnectionState.Connected);
        using var clientWire = new DriverGateway(1, TransportConnectionState.Connected);
        var host = new LobbyNetworkDriver(hostWire, 10, 0, "Host");
        host.Authority!.Join(2, "Guest");
        var client = new LobbyNetworkDriver(clientWire, 0, 1, "Guest");
        host.Pump(0.25);
        foreach (var packet in hostWire.Sent)
        {
            clientWire.Receive(new TransportMessage(1, packet.Payload, packet.Delivery));
        }

        client.Pump(0);
        hostWire.Sent.Clear();
        using var feed = new Trackstorm.Client.Hud.ActivityFeedView();
        feed.Update(client.Events, true, 0);
        host.Events.Record(Trackstorm.Core.Events.EventCategory.Damage, "Applied", 1, 2, "Missile", amount: 3.125, hp: 96.875, maxHP: 100);
        host.Events.Record(Trackstorm.Core.Events.EventCategory.Network, "Reconnected", actor: 2);
        host.Pump(0.1);
        var publication = hostWire.Sent.Single(packet => Trackstorm.Core.Events.EventCodec.IsEvent(packet.Payload.Span));
        clientWire.Receive(new TransportMessage(1, publication.Payload, TransportDelivery.Reliable));
        clientWire.Receive(new TransportMessage(1, publication.Payload, TransportDelivery.Reliable));
        client.Pump(0.1);
        var hit = client.Events.Entries.Single(entry => entry.Category == Trackstorm.Core.Events.EventCategory.Damage);
        Assert.That(hit, Is.EqualTo(host.Events.Entries.Single(entry => entry.Category == Trackstorm.Core.Events.EventCategory.Damage)));
        Assert.That(Trackstorm.Client.Development.EventLogFormatter.Format(hit), Does.Contain("3.125").And.Contain("Host").And.Contain("Guest"));
        Assert.That(feed.Entries.Single().Text, Is.EqualTo("Guest reconnected"));
        var next = hit with { Sequence = client.Events.LastSequence + 1 };
        byte[] body = Trackstorm.Core.Events.EventCodec.Encode([next]);
        byte[] retired = [(byte)'T', (byte)'E', 1, .. Trackstorm.Core.Sessions.ConnectionEnvelope.Encode(10, 2, body)];
        clientWire.Receive(new TransportMessage(1, retired, TransportDelivery.Reliable));
        clientWire.Receive(new TransportMessage(1, publication.Payload, TransportDelivery.Unreliable));
        client.Pump(0);
        Assert.That(client.Events.Entries.Count(entry => entry.Category == Trackstorm.Core.Events.EventCategory.Damage), Is.EqualTo(1));
        Assert.That(client.RejectedPackets, Is.GreaterThanOrEqualTo(2));
        Assert.That(feed.Entries.Count, Is.EqualTo(1));
        hostWire.Receive(new TransportMessage(2, publication.Payload, TransportDelivery.Reliable));
        host.Pump(0);
        Assert.That(host.RejectedPackets, Is.GreaterThan(0));
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
        public TransportCapabilities Capabilities => SimulationSupported ? TransportCapabilities.NetworkSimulation : TransportCapabilities.None;
        internal List<TransportMessage> Sent { get; } = [];
        internal List<ulong> DisconnectedPeers { get; } = [];
        internal bool FailSend { get; set; }
        internal bool SimulationSupported { get; set; }
        internal bool SimulationRejected { get; set; }
        internal int SimulationCalls { get; private set; }

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
            if (SimulationRejected)
            {
                throw new InvalidOperationException("Native provider rejected simulation.");
            }

            SimulationCalls++;
        }

        public TransportStatistics GetStatistics(ulong peerId) => default;

        internal void Receive(TransportMessage message) => _received.Enqueue(message);
        internal void ConnectPeer(ulong peer) => _connections.Add(peer, TransportConnectionState.Connected);
    }
}
