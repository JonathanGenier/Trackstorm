using System.Buffers.Binary;
using System.Numerics;
using Epic.OnlineServices.P2P;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Online;
using Trackstorm.Core.Input;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Transport.Tests;

/// <summary>Exercises the production gateway with deterministic authenticated datagrams and lifecycle callbacks.</summary>
[TestFixture]
internal sealed class EosP2pTransportTests
{
    /// <summary>Three authenticated peers restore the same match after losing the original authority.</summary>
    /// <param name="arena">Whether to migrate a running match instead of its lobby.</param>
    /// <param name="agree">Whether every eligible survivor remains available.</param>
    /// <param name="recover">Whether the original authority returns within grace.</param>
    [TestCase(false, true, false)]
    [TestCase(true, true, false)]
    [TestCase(true, false, false)]
    [TestCase(true, true, true)]
    public void MigratesLobbyAndActiveMatchThroughProductionFraming(bool arena, bool agree, bool recover)
    {
        var identities = Enumerable.Range(1, 3).Select(Id).ToArray();
        var lobby = new OnlineLobby("migration", "Migration", identities[0], 100, LobbyAccess.Public, 3, 8, OnlineLobby.CurrentProtocol, true, null) { MemberIds = identities };
        var wires = identities.Select(identity => new Wire(identity)).ToArray();
        var routes = wires.Select((wire, index) => (wire, index)).ToDictionary(entry => identities[entry.index], entry => entry.wire);
        var gateways = identities.Select((identity, index) => new EosP2pTransport(wires[index], identity, () => lobby)).ToArray();
        var subjects = Enumerable.Range(0, 3).Select(_ => new Dictionary<ulong, string>()).ToArray();
        var drivers = new LobbyNetworkDriver[3];
        try
        {
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                wires[i].Routes = routes;
                gateways[i].Authorize = (peer, identity, _) =>
                {
                    subjects[index][peer] = identity.Value;
                    return true;
                };
            }

            gateways[0].Listen(EosP2pTransport.Endpoint(lobby, identities[0]));
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                ulong server = i == 0 ? 0 : gateways[i].Connect(EosP2pTransport.Endpoint(lobby, identities[0]));
                drivers[i] = new LobbyNetworkDriver(gateways[i], i == 0 ? 100UL : 0, server, "Player" + i, identity: peer => subjects[index].GetValueOrDefault(peer));
                drivers[i].Reconnect = () => throw new InvalidOperationException("Host terminated in this scenario.");
                drivers[i].Migration = new SessionMigration(drivers[i], gateways[i], identities[i].Value, peer => subjects[index].GetValueOrDefault(peer), (subject, _) => gateways[index].RebindHost(new OnlineProductUserId(subject)));
            }

            for (int tick = 0; tick < 120; tick++)
            {
                foreach (var driver in drivers)
                {
                    driver.Pump(1.0 / 60);
                }
            }

            foreach (var driver in drivers)
            {
                Assert.That(driver.Migration!.Subjects, Is.Not.Null);
                driver.Request(LobbyCommand.Ready, true);
            }

            if (!arena)
            {
                gateways[0].Stop();
                for (int tick = 0; tick < 2100; tick++)
                {
                    drivers[1].Pump(1.0 / 60);
                    drivers[2].Pump(1.0 / 60);
                }

                Assert.That(drivers[1].State!.AuthorityEpoch, Is.EqualTo(2), drivers[1].Failure);
                Assert.That(drivers[2].State!.AuthorityEpoch, Is.EqualTo(2), drivers[2].Failure);
                Assert.That(drivers[1].State!.Players.All(player => !player.Ready), Is.True);
                Assert.That(drivers[1].LocalPlayerId, Is.EqualTo(2));
                Assert.That(drivers[2].LocalPlayerId, Is.EqualTo(3));
                return;
            }

            drivers[0].Pump(1.0 / 60);
            Assert.That(drivers[0].Request(LobbyCommand.Start), Is.True);
            drivers[1].Pump(1.0 / 60);
            drivers[2].Pump(1.0 / 60);
            var vehicles = drivers.Select((driver, index) => new VehicleNetworkDriver(gateways[index], index == 0 ? driver.State!.Match : 0, driver.ServerPeer, driver)).ToArray();
            vehicles[0].Host!.RegisterSpawns(Trackstorm.Core.Arenas.PrototypeArena.Configuration);
            vehicles[0].Host!.Items.Grant(vehicles[0].Host!.World, 3, Trackstorm.Core.Items.HeldItem.Wrench);
            for (int tick = 0; tick < 120; tick++)
            {
                foreach (var vehicle in vehicles)
                {
                    vehicle.Advance(default, Observe);
                }
            }

            if (recover)
            {
                foreach (ulong peer in drivers[0].Authority!.Peers.Keys)
                {
                    gateways[0].Disconnect(peer);
                }

                bool available = false;
                for (int i = 1; i < 3; i++)
                {
                    int index = i;
                    gateways[i].Disconnect(drivers[i].ServerPeer);
                    drivers[i].Reconnect = () => available ? gateways[index].RebindHost(identities[0]) : throw new InvalidOperationException("Transient outage");
                }

                ulong frozenTick = 0;
                for (int tick = 0; tick < 600; tick++)
                {
                    if (tick == 180)
                    {
                        Assert.That(drivers[0].Migration!.Frozen, Is.True);
                        frozenTick = vehicles[0].Host!.World.State.Tick;
                    }

                    if (tick == 239)
                    {
                        Assert.That(vehicles[0].Host!.World.State.Tick, Is.EqualTo(frozenTick));
                    }

                    available = tick >= 240;
                    foreach (var vehicle in vehicles)
                    {
                        vehicle.Advance(default, Observe);
                    }
                }

                Assert.That(drivers.All(driver => driver.State!.AuthorityEpoch == 1 && driver.Failure.Length == 0), Is.True);
                Assert.That(drivers.All(driver => !driver.Migration!.Frozen && !driver.Reconnecting), Is.True);
                Assert.That(vehicles.All(vehicle => vehicle.IsActive && vehicle.Latest!.Vehicles.Count == 3), Is.True);
                Assert.That(drivers[1].Generation, Is.EqualTo(2));
                return;
            }

            ulong? selectedTick = null;
            ulong commonTick = vehicles[0].Host!.World.State.Tick;
            if (agree)
            {
                var captured = drivers[0].Migration!.CaptureArena!();
                var common = new MigrationCheckpoint(100, drivers[0].Authority!.Capture(identities[0].Value), captured.Arena, captured.Host);
                byte[] commonBytes = MigrationCheckpointCodec.Encode(common);
                foreach (int index in new[] { 1, 2 })
                {
                    byte[] packet = [(byte)'T', (byte)'X', 1, .. commonBytes];
                    drivers[index].Migration!.Receive(new(drivers[index].ServerPeer, packet, TransportDelivery.Reliable));
                }

                // Only the candidate sees the newest boundary; agreement must choose the older common copy.
                vehicles[0].Host!.Items.Grant(vehicles[0].Host!.World, 3, Trackstorm.Core.Items.HeldItem.Missile);
                captured = drivers[0].Migration!.CaptureArena!();
                var newest = new MigrationCheckpoint(101, common.Lobby, captured.Arena, captured.Host);
                byte[] newestPacket = [(byte)'T', (byte)'X', 1, .. MigrationCheckpointCodec.Encode(newest)];
                drivers[1].Migration!.Receive(new(drivers[1].ServerPeer, newestPacket, TransportDelivery.Reliable));
                vehicles[1].Resynchronized += world => selectedTick ??= world.Tick;
            }

            gateways[0].Stop();
            if (!agree)
            {
                gateways[2].Stop();
            }

            for (int tick = 0; tick < (agree ? 2100 : 3300); tick++)
            {
                vehicles[1].Advance(default, Observe);
                if (agree)
                {
                    vehicles[2].Advance(default, Observe);
                }
            }

            if (!agree)
            {
                Assert.That(drivers[1].Failure, Does.Contain("Host migration failed"));
                Assert.That(drivers[1].State!.AuthorityEpoch, Is.EqualTo(1));
                Assert.That(vehicles[1].Host, Is.Null);
                Assert.That(vehicles[1].IsActive, Is.False);
                Assert.That(gateways[1].ConnectionState, Is.EqualTo(TransportConnectionState.Disconnected));
                return;
            }

            Assert.That(drivers[1].Failure, Is.Empty);
            Assert.That(drivers[2].Failure, Is.Empty);
            Assert.That(drivers[1].State!.AuthorityEpoch, Is.EqualTo(2));
            Assert.That(drivers[2].State!.AuthorityEpoch, Is.EqualTo(2));
            Assert.That(drivers[1].State!.CurrentHostId, Is.EqualTo(2));
            Assert.That(drivers[2].State!.CurrentHostId, Is.EqualTo(2));
            Assert.That(vehicles[1].Host, Is.Not.Null);
            Assert.That(vehicles[2].Host, Is.Null);
            Assert.That(vehicles[2].Latest!.Vehicles.Count, Is.EqualTo(3));
            Assert.That(vehicles[2].ItemState!.Slots.Single(slot => slot.Vehicle == 3).Item, Is.EqualTo(Trackstorm.Core.Items.HeldItem.Wrench));
            Assert.That(vehicles[2].IsActive, Is.True);
            Assert.That(selectedTick, Is.EqualTo(commonTick));
        }
        finally
        {
            foreach (var gateway in gateways)
            {
                gateway.Dispose();
            }
        }
    }

    /// <summary>EOS reliability, closure reasons and unsupported metrics remain explicit.</summary>
    [Test]
    public void MapsCapabilitiesDeliveryAndFailures()
    {
        Assert.That(EosP2pSdk.Reliability(TransportDelivery.Reliable), Is.EqualTo(PacketReliability.ReliableOrdered));
        Assert.That(EosP2pSdk.Reliability(TransportDelivery.Unreliable), Is.EqualTo(PacketReliability.UnreliableUnordered));
        Assert.Throws<ArgumentOutOfRangeException>(() => EosP2pSdk.Reliability((TransportDelivery)9));
        Assert.That(EosP2pSdk.Reason(ConnectionClosedReason.TimedOut), Is.EqualTo(TransportDisconnectReason.Timeout));
        Assert.That(EosP2pSdk.Reason(ConnectionClosedReason.TooManyConnections), Is.EqualTo(TransportDisconnectReason.SessionFull));
        Assert.That(EosP2pSdk.Reason(ConnectionClosedReason.ClosedByPeer), Is.EqualTo(TransportDisconnectReason.RemoteRequest));
        Assert.That(EosP2pSdk.Reason(ConnectionClosedReason.NegotiationFailed), Is.EqualTo(TransportDisconnectReason.Failure));
        using var pair = new Pair();
        Assert.That(pair.Host.Capabilities, Is.EqualTo(TransportCapabilities.None));
        Assert.That(pair.Host.GetStatistics(99), Is.EqualTo(default(TransportStatistics)));
        Assert.Throws<NotSupportedException>(() => pair.Host.ConfigureSimulation(new()));
    }

    /// <summary>The full existing lobby authority path works over the actual EOS framing and handshake.</summary>
    [Test]
    public void CarriesLobbyReadyStartAndReturn()
    {
        using var pair = new Pair();
        var host = new LobbyNetworkDriver(pair.Host, pair.Lobby.Session, 0, "Host", _ => true);
        var client = new LobbyNetworkDriver(pair.Client, 0, pair.Server, "Client", expectedSession: pair.Lobby.Session);
        for (int i = 0; i < 10; i++)
        {
            host.Pump(1.0 / 60);
            client.Pump(1.0 / 60);
        }

        Assert.That(host.State!.Players.Count, Is.EqualTo(2));
        Assert.That(client.State!.Players.Count, Is.EqualTo(2));
        host.Pump(1);
        client.Pump(1);
        Assert.That(host.State.Players.All(player => host.Latency.Get(host.State, player.Id) is null), Is.True, "EOS cannot manufacture RTT samples.");
        Assert.That(client.State.Players.All(player => client.Latency.Get(client.State, player.Id) is null), Is.True, "Host-published EOS diagnostics stay unavailable.");
        Assert.That(client.RejectedPackets, Is.Zero, "Production EOS framing carries the diagnostics protocol.");
        Assert.That(host.Request(LobbyCommand.Ready, true), Is.True);
        Assert.That(client.Request(LobbyCommand.Ready, true), Is.True);
        host.Pump(1.0 / 60);
        Assert.That(host.Request(LobbyCommand.Start), Is.True);
        client.Pump(1.0 / 60);
        Assert.That(client.State!.Phase, Is.EqualTo(SessionPhase.Arena));
        Assert.That(host.Request(LobbyCommand.Return), Is.True);
        client.Pump(1.0 / 60);
        Assert.That(client.State.Phase, Is.EqualTo(SessionPhase.Lobby));
    }

    /// <summary>Real EOS framing carries repeated authenticated rebinds without replacing gameplay identity or history.</summary>
    [Test]
    public void ReconnectsLobbyAndArenaWithOneVehicleAndFreshCheckpoint()
    {
        using var pair = new Pair();
        var host = new LobbyNetworkDriver(pair.Host, pair.Lobby.Session, 0, "Host", _ => true, identity: _ => "authenticated-client");
        var client = new LobbyNetworkDriver(pair.Client, 0, pair.Server, "Client", expectedSession: pair.Lobby.Session)
        {
            Reconnect = () =>
            {
                pair.Client.Stop();
                return pair.Client.Connect(EosP2pTransport.Endpoint(pair.Lobby, pair.HostId));
            }
        };
        for (int i = 0; i < 10; i++)
        {
            host.Pump(1.0 / 60);
            client.Pump(1.0 / 60);
        }

        ulong player = client.LocalPlayerId;
        client.Request(LobbyCommand.Ready, true);
        host.Pump(1.0 / 60);
        pair.Host.Disconnect(host.Authority!.Peers.Keys.Single());
        pair.Client.Disconnect(client.ServerPeer);
        for (int i = 0; i < 100; i++)
        {
            host.Pump(1.0 / 60);
            client.Pump(1.0 / 60);
        }

        Assert.That(client.ResumeStatus, Is.EqualTo("Resume succeeded"));
        Assert.That(client.Generation, Is.EqualTo(2));
        Assert.That(client.LocalPlayerId, Is.EqualTo(player));
        Assert.That(client.State!.Players.Single(p => p.Id == player).Ready, Is.False);
        host.Request(LobbyCommand.Ready, true);
        client.Request(LobbyCommand.Ready, true);
        host.Pump(1.0 / 60);
        Assert.That(host.Request(LobbyCommand.Start), Is.True);
        client.Pump(1.0 / 60);
        var authority = new VehicleNetworkDriver(pair.Host, host.State!.Match, lobby: host);
        var replica = new VehicleNetworkDriver(pair.Client, 0, client.ServerPeer, client);
        authority.Host!.RegisterSpawns(Trackstorm.Core.Arenas.PrototypeArena.Configuration);
        authority.Host.Items.Grant(authority.Host.World, player, Trackstorm.Core.Items.HeldItem.Wrench);
        // This is a new arena after a lobby-only resume, so its initial state is ordinary replication.
        for (int i = 0; i < 20; i++)
        {
            authority.Advance(default, Observe);
            replica.Advance(default, Observe);
        }

        int resyncs = 0;
        replica.Resynchronized += _ => resyncs++;
        for (int cycle = 0; cycle < 4; cycle++)
        {
            ulong oldPeer = host.Authority.Peers.Keys.Single();
            ulong generation = client.Generation;
            pair.Host.Disconnect(oldPeer);
            pair.Client.Disconnect(client.ServerPeer);
            for (int i = 0; i < 110; i++)
            {
                authority.Advance(default, Observe);
                replica.Advance(default, Observe);
            }

            Assert.That(client.Failure, Is.Empty);
            Assert.That(replica.Failure, Is.Empty);
            Assert.That(replica.LocalVehicleId, Is.EqualTo(player));
            Assert.That(authority.Host.World.State.Vehicles.Count, Is.EqualTo(2));
            Assert.That(client.Generation, Is.EqualTo(generation + 1));
            Assert.That(replica.LocalItem!.Item, Is.EqualTo(Trackstorm.Core.Items.HeldItem.Wrench));
            Assert.That(replica.ItemState!.Spawns.Count, Is.EqualTo(8));
            Assert.That(replica.Match!.Players.Count, Is.EqualTo(2));
            Assert.That(replica.Prediction, Is.Not.Null);
            Assert.That(replica.History!.Snapshots.All(s => s.Tick > (ulong)(cycle * 110)), Is.True);
            Assert.That(authority.Host.Receive(oldPeer, host.State.Match, [new Trackstorm.Core.Networking.Replication.SequencedInput(1, default)]), Is.False);
            int rejected = host.RejectedPackets;
            pair.Client.Send(new(client.ServerPeer, ConnectionEnvelope.Encode(host.State.Session, generation, [1, 2, 3]), TransportDelivery.Unreliable));
            authority.Advance(default, Observe);
            Assert.That(host.RejectedPackets, Is.GreaterThan(rejected));
            rejected = client.RejectedPackets;
            pair.Host.Send(new(host.Authority.Peers.Keys.Single(), ConnectionEnvelope.Encode(host.State.Session, generation, [1, 2, 3]), TransportDelivery.Reliable));
            replica.Advance(default, Observe);
            Assert.That(client.RejectedPackets, Is.GreaterThan(rejected), "Old-generation snapshots and acknowledgements never reach a decoder.");
        }

        Assert.That(resyncs, Is.EqualTo(4));
        Assert.That(pair.Host.Connections.Count, Is.EqualTo(1));
        Assert.That(pair.Client.Connections.Count, Is.EqualTo(1));
    }

    /// <summary>Independent production prop messages cannot suppress earlier vehicle snapshots or their acknowledgements.</summary>
    [Test]
    public void ReversedProductionVehicleAndPropMessagesBothAdvance()
    {
        using var pair = new Pair();
        pair.Pump();
        var body = new VehiclePhysicsState(Vector3.One, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        var host = new VehicleNetworkDriver(pair.Host, 17) { ObserveProps = () => new[] { body, body, body } };
        var client = new VehicleNetworkDriver(pair.Client, 0, pair.Server);
        for (int tick = 0; tick < 30; tick++)
        {
            host.Advance(default, Observe);
            pair.ClientWire.ReverseUnreliable();
            client.Advance(default, Observe);
        }

        Assert.That(client.PropSnapshot?.Tick, Is.EqualTo(30));
        Assert.That(client.ReceivedSnapshots, Is.EqualTo(11), "Ten periodic snapshots plus the initial reliable item snapshot must arrive.");
        Assert.That(client.Latest!.Tick, Is.EqualTo(30));
        Assert.That(client.Inputs!.LastAcknowledged, Is.GreaterThan(24));
        Assert.That(client.Failure, Is.Empty);
    }

    /// <summary>Large payloads retain ownership and unreliable loss never blocks reliable delivery.</summary>
    [Test]
    public void FragmentsReordersDropsAndBoundsPayloads()
    {
        using var pair = new Pair();
        pair.Pump();
        byte[] payload = Enumerable.Range(0, 65536).Select(i => (byte)i).ToArray();
        pair.Client.Send(new(pair.Server, payload));
        pair.Host.Poll();
        Assert.That(pair.Host.TryReceive(out var message), Is.True);
        Assert.That(message.Payload.ToArray(), Is.EqualTo(payload));
        pair.Client.Send(new(pair.Server, new byte[4000], TransportDelivery.Unreliable));
        pair.HostWire.Packets.Dequeue();
        pair.Client.Send(new(pair.Server, new byte[] { 5, 6 }, TransportDelivery.Reliable));
        pair.Host.Poll();
        Assert.That(pair.Host.TryReceive(out var control), Is.True);
        Assert.That(control.Payload.ToArray(), Is.EqualTo(new byte[] { 5, 6 }));
        Assert.That(message.Payload.ToArray(), Is.EqualTo(payload), "Published memory must survive later receives.");
        pair.Client.Send(new(pair.Server, new byte[4000], TransportDelivery.Unreliable));
        var reversed = pair.HostWire.Packets.Reverse().ToArray();
        pair.HostWire.Packets.Clear();
        foreach (var packet in reversed)
        {
            pair.HostWire.Packets.Enqueue(packet);
        }

        pair.Host.Poll();
        Assert.That(pair.Host.TryReceive(out var unordered), Is.True);
        Assert.That(unordered.Payload.Length, Is.EqualTo(4000));
        Assert.That(pair.Client.PeakPacketBytes, Is.LessThanOrEqualTo(1170));
        Assert.Throws<ArgumentOutOfRangeException>(() => pair.Client.Send(new(pair.Server, new byte[65537])));
    }

    /// <summary>Independent maximum-sized unordered messages can interleave, duplicate and complete in reverse order.</summary>
    [Test]
    public void InterleavesMaximumUnreliableMessagesExactlyOnce()
    {
        using var pair = new Pair();
        pair.Pump();
        byte[] first = Enumerable.Range(0, EosPacketAssembly.MaximumPayload).Select(i => (byte)i).ToArray();
        byte[] second = first.Select(value => (byte)(value ^ 255)).ToArray();
        var a = CaptureUnreliable(pair, first);
        var b = CaptureUnreliable(pair, second);
        // A third, incomplete message must not block either assembly or reliable control.
        var lost = CaptureUnreliable(pair, new byte[4000]);
        pair.HostWire.Packets.Enqueue(lost[0]);
        pair.Client.Send(new(pair.Server, new byte[] { 7 }, TransportDelivery.Reliable));
        pair.Client.Send(new(pair.Server, new byte[] { 8 }, TransportDelivery.Reliable));
        for (int i = a.Length - 1; i >= 0; i--)
        {
            pair.HostWire.Packets.Enqueue(b[i]);
            pair.HostWire.Packets.Enqueue(a[i]);
            pair.HostWire.Packets.Enqueue(b[i]);
            pair.HostWire.Packets.Enqueue(a[i]);
        }

        pair.Host.Poll();
        Assert.That(pair.Host.TryReceive(out var control1), Is.True);
        Assert.That(control1.Payload.ToArray(), Is.EqualTo(new byte[] { 7 }));
        Assert.That(pair.Host.TryReceive(out var control2), Is.True);
        Assert.That(control2.Payload.ToArray(), Is.EqualTo(new byte[] { 8 }));
        Assert.That(pair.Host.TryReceive(out var completedB), Is.True);
        Assert.That(completedB.Payload.ToArray(), Is.EqualTo(second));
        Assert.That(pair.Host.TryReceive(out var completedA), Is.True);
        Assert.That(completedA.Payload.ToArray(), Is.EqualTo(first));
        Assert.That(pair.Host.TryReceive(out _), Is.False);
        // Reuse every slot; consumers must retain their original published memory.
        for (int i = 0; i < EosUnreliableWindow.Capacity; i++)
        {
            pair.Client.Send(new(pair.Server, new byte[] { 9 }, TransportDelivery.Unreliable));
        }

        pair.Host.Poll();
        Assert.That(completedA.Payload.ToArray(), Is.EqualTo(first));
        Assert.That(completedB.Payload.ToArray(), Is.EqualTo(second));
        Assert.That(pair.Host.ConnectionState, Is.EqualTo(TransportConnectionState.Connected));
    }

    /// <summary>Accepts the oldest in-window message across wrap, but cannot resurrect evicted or ambiguous serials.</summary>
    /// <param name="origin">First serial, including a range crossing uint wrap.</param>
    [TestCase(1u)]
    [TestCase(uint.MaxValue - 7)]
    public void UnreliableWindowRejectsEvictedDuplicatesAndHandlesWrap(uint origin)
    {
        using var pair = new Pair();
        pair.Pump();
        var oldest = CaptureUnreliable(pair, new byte[2000], origin);
        var newest = CaptureUnreliable(pair, new byte[] { 16 }, unchecked(origin + 15));
        pair.HostWire.Packets.Enqueue(newest[0]);
        foreach (var packet in oldest)
        {
            pair.HostWire.Packets.Enqueue(packet);
        }

        pair.Host.Poll();
        Assert.That(pair.Host.TryReceive(out var recent), Is.True);
        Assert.That(recent.Payload.ToArray(), Is.EqualTo(new byte[] { 16 }));
        Assert.That(pair.Host.TryReceive(out var late), Is.True);
        Assert.That(late.Payload.Length, Is.EqualTo(2000), "Fifteen-behind sequence is still inside the window.");
        var edge = CaptureUnreliable(pair, new byte[] { 17 }, unchecked(origin + 16));
        pair.HostWire.Packets.Enqueue(edge[0]);
        foreach (var packet in oldest.Concat(newest).Concat(edge))
        {
            pair.HostWire.Packets.Enqueue(packet);
        }

        var ambiguous = CaptureUnreliable(pair, new byte[] { 99 }, unchecked(origin + 16 + 0x80000000u));
        pair.HostWire.Packets.Enqueue(ambiguous[0]);
        pair.Host.Poll();
        Assert.That(pair.Host.TryReceive(out var next), Is.True);
        Assert.That(next.Payload.ToArray(), Is.EqualTo(new byte[] { 17 }));
        Assert.That(pair.Host.TryReceive(out _), Is.False, "Duplicates, sixteen-behind and half-range serials must be discarded.");
    }

    /// <summary>A stream of missing fragments evicts bounded old work while reliable and later unreliable messages progress.</summary>
    [Test]
    public void EvictsIncompleteUnreliableAssembliesWithoutBlockingProgress()
    {
        using var pair = new Pair();
        pair.Pump();
        var lost = CaptureUnreliable(pair, new byte[2000]);
        pair.HostWire.Packets.Enqueue(lost[0]);
        pair.Host.Poll();
        for (int i = 0; i < EosUnreliableWindow.Capacity * 4; i++)
        {
            var incomplete = CaptureUnreliable(pair, new byte[2000]);
            pair.HostWire.Packets.Enqueue(incomplete[0]);
            pair.Host.Poll();
            Assert.That(pair.Host.TryReceive(out _), Is.False);
        }

        foreach (var packet in lost)
        {
            pair.HostWire.Packets.Enqueue(packet);
        }

        pair.Client.Send(new(pair.Server, new byte[] { 5 }, TransportDelivery.Reliable));
        pair.Client.Send(new(pair.Server, new byte[] { 6 }, TransportDelivery.Unreliable));
        pair.Host.Poll();
        Assert.That(pair.Host.TryReceive(out var reliable), Is.True);
        Assert.That(reliable.Payload.ToArray(), Is.EqualTo(new byte[] { 5 }));
        Assert.That(pair.Host.TryReceive(out var unreliable), Is.True);
        Assert.That(unreliable.Payload.ToArray(), Is.EqualTo(new byte[] { 6 }));
        Assert.That(pair.Host.TryReceive(out _), Is.False);
        Assert.That(pair.Host.ConnectionState, Is.EqualTo(TransportConnectionState.Connected));
    }

    /// <summary>Idle polling expires incomplete work at its original deadline and retains duplicate tombstones.</summary>
    [Test]
    public void ExpiresIncompleteMessagesWithoutAllowingLateRestart()
    {
        using var pair = new Pair();
        pair.Pump();
        var lost = CaptureUnreliable(pair, new byte[2000]);
        pair.HostWire.Packets.Enqueue(lost[0]);
        pair.Host.Poll();
        pair.Clock.Advance(0.5);
        pair.HostWire.Packets.Enqueue(lost[0]);
        pair.Host.Poll();
        pair.Clock.Advance(0.5);
        pair.Host.Poll();
        foreach (var packet in lost.Reverse().Concat(lost))
        {
            pair.HostWire.Packets.Enqueue(packet);
        }

        pair.Host.Poll();
        Assert.That(pair.Host.TryReceive(out _), Is.False, "Late fragments and retries cannot extend or restart expired work.");
        pair.Client.Send(new(pair.Server, new byte[] { 1 }, TransportDelivery.Unreliable));
        pair.Host.Poll();
        Assert.That(pair.Host.TryReceive(out var fresh), Is.True);
        Assert.That(fresh.Payload.ToArray(), Is.EqualTo(new byte[] { 1 }));
    }

    /// <summary>Receive work and callback queues remain bounded even under a hostile producer.</summary>
    /// <param name="delivery">Stream producing completed messages faster than the consumer drains them.</param>
    [TestCase(TransportDelivery.Reliable)]
    [TestCase(TransportDelivery.Unreliable)]
    public void BoundsPollAndDisconnectsOnUnconsumedQueueOverflow(TransportDelivery delivery)
    {
        using var pair = new Pair();
        pair.Pump();
        for (int i = 0; i < 300; i++)
        {
            pair.Client.Send(new(pair.Server, new byte[] { 1 }, delivery));
        }

        int before = pair.HostWire.Reads;
        pair.Host.Poll();
        Assert.That(pair.HostWire.Reads - before, Is.EqualTo(EosP2pTransport.ReceiveBudget));
        pair.Host.Poll();
        Assert.That(pair.Host.Connections, Is.Empty);
    }

    /// <summary>Late callbacks, departed members and incorrect credentials cannot create gameplay peers.</summary>
    [Test]
    public void RejectsStaleCallbacksMembershipAndCredential()
    {
        using var pair = new Pair(accept: false);
        pair.Pump();
        Assert.That(pair.Host.Connections, Is.Empty);
        Assert.That(pair.Client.ConnectionState, Is.Not.EqualTo(TransportConnectionState.Connected));
        var old = pair.HostWire.Changed!;
        pair.Host.Stop();
        pair.Host.Stop();
        pair.Host.Listen(EosP2pTransport.Endpoint(pair.Lobby, pair.HostId));
        old(pair.ClientId, TransportConnectionState.Connecting, TransportDisconnectReason.None);
        pair.Host.Poll();
        Assert.That(pair.Host.Connections, Is.Empty);
        pair.HostWire.Changed!(Id(9), TransportConnectionState.Connecting, TransportDisconnectReason.None);
        pair.Host.Poll();
        Assert.That(pair.Host.Connections, Is.Empty);
        Assert.That(pair.HostWire.Closed, Does.Contain(Id(9)));
        pair.Host.Dispose();
        pair.Host.Dispose();
        pair.Host.Stop();
        Assert.That(pair.HostWire.Disposals, Is.EqualTo(1));
    }

    /// <summary>Connecting members reserve the seven slots; duplicates do not consume new identities.</summary>
    [Test]
    public void EnforcesCapacityAndConnectionDeadline()
    {
        using var pair = new Pair();
        pair.Host.Stop();
        pair.Lobby = pair.Lobby with { MemberIds = Enumerable.Range(1, 9).Select(Id).ToArray() };
        pair.Host.Listen(EosP2pTransport.Endpoint(pair.Lobby, pair.HostId));
        for (int i = 2; i <= 9; i++)
        {
            pair.HostWire.Changed!(Id(i), TransportConnectionState.Connecting, TransportDisconnectReason.None);
            pair.HostWire.Changed!(Id(i), TransportConnectionState.Connecting, TransportDisconnectReason.None);
        }

        pair.Host.Poll();
        Assert.That(pair.Host.Connections.Count, Is.EqualTo(7));
        Assert.That(pair.HostWire.Closed, Does.Contain(Id(9)));
        pair.Clock.Advance();
        pair.Host.Poll();
        Assert.That(pair.Host.Connections, Is.Empty);
    }

    /// <summary>Measures production framing and vehicle replication with deterministic in-memory delivery.</summary>
    /// <param name="players">Total players including the host.</param>
    /// <param name="reorderMixed">Reverse independent vehicle/prop datagrams on every publication.</param>
    [TestCase(2, false)]
    [TestCase(8, false)]
    [TestCase(2, true)]
    [TestCase(8, true)]
    public void MeasuresVehicleTrafficThroughEosFraming(int players, bool reorderMixed)
    {
        var routes = new Dictionary<OnlineProductUserId, Wire>();
        var gateways = new List<EosP2pTransport>();
        var members = Enumerable.Range(1, players).Select(Id).ToArray();
        var lobby = new OnlineLobby("measure", "Measure", Id(1), 91, LobbyAccess.Public, players, 8, OnlineLobby.CurrentProtocol, true, null) { MemberIds = members };
        try
        {
            foreach (var member in members)
            {
                routes.Add(member, new Wire(member) { Routes = routes });
            }

            var hostGateway = new EosP2pTransport(routes[Id(1)], Id(1), () => lobby) { Authorize = (_, _, _) => true };
            gateways.Add(hostGateway);
            hostGateway.Listen(EosP2pTransport.Endpoint(lobby, Id(1)));
            var host = new VehicleNetworkDriver(hostGateway, 91);
            if (reorderMixed)
            {
                host.ObserveProps = () => Enumerable.Repeat(new VehiclePhysicsState(new Vector3(host.Host!.World.State.Tick, 1, 0), Quaternion.Identity, Vector3.Zero, Vector3.Zero), 3).ToArray();
            }

            var clients = new List<VehicleNetworkDriver>();
            foreach (var member in members.Skip(1))
            {
                var gateway = new EosP2pTransport(routes[member], member, () => lobby);
                gateways.Add(gateway);
                clients.Add(new VehicleNetworkDriver(gateway, 0, gateway.Connect(EosP2pTransport.Endpoint(lobby, Id(1)))));
            }

            for (int tick = 0; tick < 600; tick++)
            {
                host.Advance(default, Observe);
                if (reorderMixed)
                {
                    foreach (var member in members.Skip(1))
                    {
                        routes[member].ReverseUnreliable();
                    }
                }

                foreach (var client in clients)
                {
                    client.Advance(new InputFrame(0, 0, 20000, 0, 0, 0, 0), Observe);
                    Assert.That(client.Failure, Is.Empty);
                    if (reorderMixed && tick >= 12)
                    {
                        Assert.That(client.Latest!.Tick, Is.GreaterThanOrEqualTo((ulong)(tick - 2)));
                        Assert.That(client.PropSnapshot!.Tick, Is.EqualTo(client.Latest.Tick));
                        Assert.That(client.Inputs!.LastAcknowledged, Is.GreaterThanOrEqualTo((uint)(tick - 5)));
                        Assert.That(client.Inputs.Pending.Count, Is.LessThanOrEqualTo(4), "Reordering unrelated publications must not stall acknowledgements.");
                    }
                }
            }

            Assert.That(host.Host!.World.State.Vehicles.Count, Is.EqualTo(players));
            Assert.That(clients.All(client => client.ReceivedSnapshots > 150 && client.Latest!.Vehicles.Count == players), Is.True);
            if (reorderMixed)
            {
                Assert.That(clients.All(client => client.PropSnapshot!.Tick == 600 && client.Latest!.Tick == 600 && client.ReceivedSnapshots >= 200), Is.True);
            }

            TestContext.WriteLine($"FAKE NATIVE, 10 simulated seconds, players={players}; host sent={hostGateway.SentPackets}, received={hostGateway.ReceivedPackets}, mean packet={hostGateway.SentBytes / (double)hostGateway.SentPackets:F1}B, peak={hostGateway.PeakPacketBytes}B, peak gateway poll={hostGateway.PeakPollMilliseconds:F3}ms; RTT/loss and SDK Tick NOT measured");
        }
        finally
        {
            foreach (var gateway in gateways)
            {
                gateway.Dispose();
            }
        }
    }

    /// <summary>Replacing a connection rejects previous nonces, while member departure removes the active slot.</summary>
    /// <param name="delivery">Stream whose incomplete and completed state must not survive a connection.</param>
    [TestCase(TransportDelivery.Reliable)]
    [TestCase(TransportDelivery.Unreliable)]
    public void RepeatedCyclesRejectOldPacketsAndMemberDeparture(TransportDelivery delivery)
    {
        using var pair = new Pair();
        for (int cycle = 0; cycle < 4; cycle++)
        {
            pair.Pump();
            Assert.That(pair.Host.ConnectionState, Is.EqualTo(TransportConnectionState.Connected));
            pair.Client.Send(new(pair.Server, new byte[2000], delivery));
            var first = pair.HostWire.Packets.Dequeue();
            var old = pair.HostWire.Packets.Dequeue();
            pair.HostWire.Packets.Enqueue(first);
            pair.Host.Poll();
            Assert.That(pair.Host.TryReceive(out _), Is.False);
            pair.Host.Disconnect(pair.Host.Connections.Keys.Single());
            pair.Client.Stop();
            pair.Host.Stop();
            pair.Host.Listen(EosP2pTransport.Endpoint(pair.Lobby, pair.HostId));
            pair.Server = pair.Client.Connect(EosP2pTransport.Endpoint(pair.Lobby, pair.HostId));
            pair.Pump();
            pair.HostWire.Packets.Enqueue(old);
            pair.Host.Poll();
            Assert.That(pair.Host.TryReceive(out _), Is.False);
            pair.Client.Send(new(pair.Server, new byte[] { 3 }, delivery));
            pair.Host.Poll();
            Assert.That(pair.Host.TryReceive(out var fresh), Is.True);
            Assert.That(fresh.Payload.ToArray(), Is.EqualTo(new byte[] { 3 }));
        }

        pair.Lobby = pair.Lobby with { MemberIds = new[] { pair.HostId }, Members = 1 };
        pair.Pump();
        Assert.That(pair.Host.Connections, Is.Empty);
        Assert.That(pair.Client.Connections, Is.Empty);
    }

    /// <summary>Session context and role cannot be redirected by arbitrary endpoint input.</summary>
    [Test]
    public void RejectsWrongSessionAndMalformedFragments()
    {
        using var pair = new Pair();
        pair.Pump();
        pair.Client.Send(new(pair.Server, new byte[] { 3 }));
        var packet = pair.HostWire.Packets.Dequeue();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(packet.Bytes.AsSpan(21), int.MaxValue);
        pair.HostWire.Packets.Enqueue(packet);
        pair.Host.Poll();
        Assert.That(pair.Host.Connections, Is.Empty);
        pair.Host.Stop();
        Assert.Throws<ArgumentException>(() => pair.Host.Listen(TransportEndpoint.PeerSession(pair.HostId.Value, "wrong-session")));
        Assert.Throws<ArgumentException>(() => pair.Host.Connect(EosP2pTransport.Endpoint(pair.Lobby, pair.HostId)));
    }

    private static VehicleObservation Observe(VehicleSnapshot state)
    {
        var physics = state.Movement.Physics;
        var velocity = physics.LinearVelocity;
        velocity.Y = 0;
        var position = physics.Position + (velocity / 60);
        position.Y = 1;
        return new(new VehiclePhysicsState(position, physics.Orientation, velocity, physics.AngularVelocity), Vector3.UnitY);
    }

    private static OnlineProductUserId Id(int value) => new(value.ToString("x32"));

    private static (OnlineProductUserId Peer, byte[] Bytes, TransportDelivery Delivery)[] CaptureUnreliable(Pair pair, byte[] payload, uint? sequence = null)
    {
        // Drain only packets emitted by this send. Inject serials on the fake wire, retaining real framing and nonces.
        var pending = pair.HostWire.Packets.ToArray();
        pair.HostWire.Packets.Clear();
        pair.Client.Send(new(pair.Server, payload, TransportDelivery.Unreliable));
        var packets = pair.HostWire.Packets.ToArray();
        pair.HostWire.Packets.Clear();
        foreach (var packet in pending)
        {
            pair.HostWire.Packets.Enqueue(packet);
        }

        if (sequence is uint serial)
        {
            foreach (var packet in packets)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(packet.Bytes.AsSpan(17), serial);
            }
        }

        return packets;
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        internal void Advance(double seconds = 13) => _ticks += (long)(TimeSpan.TicksPerSecond * seconds);
    }

    private sealed class Pair : IDisposable
    {
        internal Pair(bool accept = true)
        {
            Lobby = new("lobby", "Test", HostId, 17, LobbyAccess.Public, 2, 8, OnlineLobby.CurrentProtocol, true, null) { MemberIds = new[] { HostId, ClientId } };
            HostWire = new Wire(HostId);
            ClientWire = new Wire(ClientId);
            HostWire.Other = ClientWire;
            ClientWire.Other = HostWire;
            Host = new(HostWire, HostId, () => Lobby, time: Clock) { Authorize = (_, _, _) => accept };
            Client = new(ClientWire, ClientId, () => Lobby, time: Clock);
            Host.Listen(EosP2pTransport.Endpoint(Lobby, HostId));
            Server = Client.Connect(EosP2pTransport.Endpoint(Lobby, HostId));
        }

        internal OnlineProductUserId HostId { get; } = Id(1);
        internal OnlineProductUserId ClientId { get; } = Id(2);
        internal OnlineLobby Lobby { get; set; }
        internal Clock Clock { get; } = new();
        internal Wire HostWire { get; }
        internal Wire ClientWire { get; }
        internal EosP2pTransport Host { get; }
        internal EosP2pTransport Client { get; }
        internal ulong Server { get; set; }
        public void Dispose()
        {
            Host.Dispose();
            Client.Dispose();
        }

        internal void Pump()
        {
            for (int i = 0; i < 5; i++)
            {
                Host.Poll();
                Client.Poll();
            }
        }
    }

    private sealed class Wire(OnlineProductUserId local) : IEosP2p
    {
        private readonly HashSet<OnlineProductUserId> _accepted = new();
        internal Dictionary<OnlineProductUserId, Wire>? Routes { get; set; }
        internal Wire? Other { get; set; }
        internal Action<OnlineProductUserId, TransportConnectionState, TransportDisconnectReason>? Changed { get; set; }
        internal Queue<(OnlineProductUserId Peer, byte[] Bytes, TransportDelivery Delivery)> Packets { get; } = new();
        internal List<OnlineProductUserId> Closed { get; } = new();
        internal int Disposals { get; private set; }
        internal int Reads { get; private set; }
        public void Start(string socket, Action<OnlineProductUserId, TransportConnectionState, TransportDisconnectReason> changed) => Changed = changed;
        public bool Accept(OnlineProductUserId peer)
        {
            _accepted.Add(peer);
            var other = Routes?.GetValueOrDefault(peer) ?? Other!;
            if (other._accepted.Contains(local))
            {
                Changed!(peer, TransportConnectionState.Connected, TransportDisconnectReason.None);
                other.Changed!(local, TransportConnectionState.Connected, TransportDisconnectReason.None);
            }
            else
            {
                other.Changed!(local, TransportConnectionState.Connecting, TransportDisconnectReason.None);
            }

            return true;
        }

        public bool Send(OnlineProductUserId peer, ArraySegment<byte> data, TransportDelivery delivery)
        {
            (Routes?.GetValueOrDefault(peer) ?? Other!).Packets.Enqueue((local, data.ToArray(), delivery));
            return true;
        }

        public bool Receive(byte[] buffer, out OnlineProductUserId? peer, out int length, out TransportDelivery delivery)
        {
            Reads++;
            if (!Packets.TryDequeue(out var packet))
            {
                peer = null;
                length = 0;
                delivery = default;
                return false;
            }

            peer = packet.Peer;
            length = packet.Bytes.Length;
            delivery = packet.Delivery;
            packet.Bytes.CopyTo(buffer, 0);
            return true;
        }

        public void Close(OnlineProductUserId peer)
        {
            _accepted.Remove(peer);
            Closed.Add(peer);
        }

        public void Stop()
        {
            _accepted.Clear();
            Packets.Clear();
            Changed = null;
        }

        public void Dispose()
        {
            Disposals++;
            Stop();
        }

        internal void ReverseUnreliable()
        {
            var packets = Packets.ToArray();
            var reversed = new Queue<(OnlineProductUserId Peer, byte[] Bytes, TransportDelivery Delivery)>(packets.Where(packet => packet.Delivery == TransportDelivery.Unreliable).Reverse());
            Packets.Clear();
            foreach (var packet in packets)
            {
                Packets.Enqueue(packet.Delivery == TransportDelivery.Unreliable ? reversed.Dequeue() : packet);
            }
        }
    }
}
