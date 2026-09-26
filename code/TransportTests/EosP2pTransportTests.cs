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
    [Test]
    public void LargeReliableBoundaryUsesProductionEosFramingWithoutExceedingNativeQueue()
    {
        using var pair = new Pair();
        pair.Pump();
        var host = ReliableMessageGateway.For(pair.Host);
        var client = ReliableMessageGateway.For(pair.Client);
        byte[] payload = new byte[250000]; new Random(172).NextBytes(payload);
        host.Send(new(pair.Host.Connections.Single(p => p.Value == TransportConnectionState.Connected).Key, payload));
        byte[]? received = null;
        for (int i = 0; i < 20; i++)
        {
            host.Poll(); client.Poll();
            Assert.That(host.TryReceive(out _), Is.False);
            if (client.TryReceive(out var message)) { Assert.That(received, Is.Null); received = message.Payload.ToArray(); }
        }
        Assert.That(received, Is.EqualTo(payload));
        Assert.That(pair.Client.ConnectionState, Is.EqualTo(TransportConnectionState.Connected));
    }

    /// <summary>A fresh participant bootstraps through production EOS framing after the host is already active.</summary>
    [Test]
    public void FreshActiveJoinUsesEosCheckpointAndActivationPath()
    {
        using var pair = new Pair();
        pair.Pump();
        var host = new LobbyNetworkDriver(pair.Host, pair.Lobby.Session, 0, "Host", _ => true, identity: _ => pair.ClientId.Value);
        host.Authority!.SetReady(0, true);
        Assert.That(host.Authority.Start(0), Is.True);
        var hostVehicles = new VehicleNetworkDriver(pair.Host, host.State!.Match, lobby: host);
        hostVehicles.Host!.RegisterSpawns(Trackstorm.Core.Arenas.PrototypeArena.Configuration);
        Assert.That(hostVehicles.ForceDeveloperStart(), Is.True);
        for (int tick = 0; tick < 190; tick++)
        {
            hostVehicles.Advance(default, Observe);
        }

        Assert.That(hostVehicles.Match!.Phase, Is.EqualTo(Core.Matches.MatchPhase.Active));
        var client = new LobbyNetworkDriver(pair.Client, 0, pair.Server, "Fresh", expectedSession: pair.Lobby.Session);
        client.Pump(0);
        hostVehicles.Advance(default, Observe);
        client.Pump(0);
        Assert.That(client.JoiningArena, Is.True);
        var clientVehicles = new VehicleNetworkDriver(pair.Client, 0, pair.Server, client);
        for (int tick = 0; tick < 60; tick++)
        {
            hostVehicles.Advance(default, Observe);
            clientVehicles.Advance(new InputFrame(0, 0, 20000, 0, 0, 0, 0), Observe);
        }

        Assert.That(client.Failure, Is.Empty);
        Assert.That(clientVehicles.IsActive, Is.True);
        Assert.That(client.LocalPlayerId, Is.EqualTo(2));
        Assert.That(host.State.Players.Count, Is.EqualTo(2));
        Assert.That(hostVehicles.Host.World.State.Vehicles.Count, Is.EqualTo(2));
        Assert.That(clientVehicles.Latest!.Vehicles.Count, Is.EqualTo(2));
        Assert.That(clientVehicles.Inputs!.LastAcknowledged, Is.GreaterThan(40));
        Assert.That(clientVehicles.ItemState!.Spawns.Count, Is.EqualTo(8));
        Assert.That(clientVehicles.Match!.Phase, Is.EqualTo(Core.Matches.MatchPhase.Active));
    }

    /// <summary>Losing both coordination and P2P freezes the old host and never creates replacement authority.</summary>
    [Test]
    public void TrustedLeaseOutageFailsClosedAtBothPeers()
    {
        using var pair = new MigrationPair();
        pair.EnableLeases();
        pair.Step(180);
        pair.StartArena();
        pair.SetLeaseOutage();
        pair.Link.HostWire.DropOutgoing = pair.Link.ClientWire.DropOutgoing = true;
        pair.Step(600);
        Assert.That(pair.Host.Migration!.Frozen, Is.True);
        ulong tick = pair.HostVehicles!.Host!.World.State.Tick;
        pair.Step(300);
        Assert.That(pair.HostVehicles.Host.World.State.Tick, Is.EqualTo(tick));
        Assert.That(pair.Client.Authority, Is.Null);
        Assert.That(pair.Client.State!.AuthorityEpoch, Is.EqualTo(1));
        Assert.That(pair.Client.Migration!.Frozen, Is.True);
    }

    /// <summary>Real lease semantics fence a crash within ten seconds, retain the match and permit password-independent rebind.</summary>
    /// <param name="arena">Whether loss occurs in an active match.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void TrustedLeaseCrashMigrationAndFormerHostRetention(bool arena)
    {
        using var pair = new MigrationPair();
        pair.EnableLeases();
        pair.Step(180);
        if (arena)
        {
            pair.StartArena();
            pair.HostVehicles!.Host!.Items.Grant(pair.HostVehicles.Host.World, 1, Trackstorm.Core.Items.HeldItem.Wrench);
            pair.Step(60);
        }

        var before = pair.Client.State!;
        pair.RetireHostTransport();
        int frames = 0;
        while (pair.Client.Authority is null && frames++ < 780)
        {
            pair.Step(1, host: false);
        }

        Assert.That(pair.Client.Failure, Is.Empty);
        Assert.That(pair.Client.State!.AuthorityEpoch, Is.EqualTo(2));
        Assert.That(frames, Is.LessThan(780), "Failover must finish promptly after trusted fencing, independently of player retention.");
        Assert.That(pair.Client.State.Session, Is.EqualTo(before.Session));
        Assert.That(pair.Client.State.Match, Is.EqualTo(before.Match));
        if (arena)
        {
            pair.Step(2100, host: false);
            Assert.That(pair.Client.State.Players.Single(player => player.Id == 1), Is.EqualTo(before.Players.Single(player => player.Id == 1) with { Ready = false, Connected = false, RetainedHost = true }));
            Assert.That(pair.Client.State.Phase, Is.EqualTo(SessionPhase.Arena));
            Assert.That(pair.ClientVehicles!.Host!.World.State.Vehicles.Count, Is.EqualTo(2));
            Assert.That(pair.ClientVehicles.Host.Items.Slots.Single(slot => slot.Vehicle == 1).Item, Is.EqualTo(Trackstorm.Core.Items.HeldItem.Wrench));
            pair.RestartFormerHost();
            pair.Step(180);
            Assert.That(pair.Host.Authority, Is.Null);
            Assert.That(pair.Host.LocalPlayerId, Is.EqualTo(1));
            Assert.That(pair.Host.Generation, Is.EqualTo(2));
        }
        else
        {
            Assert.That(pair.Client.State.Players.Select(player => player.Id), Is.EqualTo(new ulong[] { 2 }));
            Assert.That(pair.Client.Request(LobbyCommand.Ready, true), Is.True);
            Assert.That(pair.Client.Request(LobbyCommand.Start), Is.True);
            pair.ReplaceClientVehicles(new VehicleNetworkDriver(pair.Link.Client, pair.Client.State.Match, 0, pair.Client));
            Assert.That(pair.ClientVehicles!.Host!.World.State.Vehicles.Select(vehicle => vehicle.VehicleId), Is.EqualTo(new ulong[] { 2 }));
            return;
        }

        Assert.That(pair.Host.State!.AuthorityEpoch, Is.EqualTo(2));
        pair.RetireClientTransport();
        pair.Step(780, client: false);
        Assert.That(pair.Host.Failure, Is.Empty);
        Assert.That(pair.Host.State.AuthorityEpoch, Is.EqualTo(3));
    }

    /// <summary>A healthy renewing host continues while its client has no gameplay route.</summary>
    [Test]
    public void TrustedLeaseP2pPartitionPreservesHealthyHost()
    {
        using var pair = new MigrationPair();
        pair.EnableLeases();
        pair.Step(180);
        pair.StartArena();
        ulong tick = pair.HostVehicles!.Host!.World.State.Tick;
        pair.Link.HostWire.DropOutgoing = pair.Link.ClientWire.DropOutgoing = true;
        pair.Step(900);
        Assert.That(pair.Host.Migration!.Frozen, Is.False);
        Assert.That(pair.HostVehicles.Host.World.State.Tick, Is.GreaterThan(tick));
        Assert.That(pair.Client.Authority, Is.Null);
        Assert.That(pair.Client.State!.AuthorityEpoch, Is.EqualTo(1));
        pair.RetireHostTransport();
        pair.Step(1200, host: false);
        Assert.That(pair.Client.Authority, Is.Null, "Continuing service renewals made the pre-partition checkpoint stale.");
        Assert.That(pair.Client.Failure, Is.Not.Empty);
    }

    /// <summary>A complete older tuning boundary may be restored at a new epoch, without relaxing in-epoch ordering.</summary>
    [Test]
    public void MigrationRestoresCheckpointConfigurationAfterUncheckpointedTuning()
    {
        using var pair = new MigrationPair();
        pair.StartArena();
        Assert.That(pair.HostVehicles!.TryConfigure(new Dictionary<string, double> { ["vehicle.acceleration"] = 7 }, out _), Is.True);
        pair.Step(90);
        var retained = pair.HostVehicles.Configuration;
        Assert.That(pair.HostVehicles.TryConfigure(new Dictionary<string, double> { ["vehicle.acceleration"] = 9 }, out _), Is.True);
        pair.Step(2);
        Assert.That(pair.ClientVehicles!.Configuration.Revision, Is.EqualTo(retained.Revision + 1));
        pair.RetireHostTransport();
        pair.Step(2100, host: false);
        Assert.That(pair.Client.Failure, Is.Empty);
        Assert.That(pair.Client.State!.AuthorityEpoch, Is.EqualTo(2));
        Assert.That(pair.ClientVehicles.Configuration, Is.EqualTo(retained));
    }

    /// <summary>Live tuning and actions follow successive hosts, while retired-epoch tuning cannot cross a fresh connection.</summary>
    [Test]
    public void DeveloperAuthorityAndConfigurationSurviveSuccessiveMigrations()
    {
        using var pair = new MigrationPair();
        pair.StartArena(waiting: true);
        var edits = new Dictionary<string, double> { ["vehicle.acceleration"] = 7, ["damage.max_hp"] = 230, ["spawns.seed"] = 42, ["items.wrench_heal"] = 17 };
        Assert.That(pair.HostVehicles!.TryConfigure(edits, out _), Is.True);
        pair.Step(120);
        var expected = pair.HostVehicles.Configuration;
        ulong rng = pair.HostVehicles.Host!.Spawns!.RandomState;
        var retiredDriver = pair.HostVehicles;
        pair.Link.HostWire.DropOutgoing = pair.Link.ClientWire.DropOutgoing = true;
        pair.HostServiceLive = false;
        for (int i = 0; i < 2100 && pair.Client.Authority is null; i++)
        {
            pair.Step(1);
        }

        Assert.That(pair.Client.State!.AuthorityEpoch, Is.EqualTo(2));
        Assert.That(retiredDriver.TryConfigure(edits, out _), Is.False);
        Assert.That(retiredDriver.GiveDeveloperItem(Trackstorm.Core.Items.HeldItem.Missile), Is.False);
        Assert.That(retiredDriver.ForceDeveloperStart(), Is.False);
        Assert.That(pair.ClientVehicles!.Configuration, Is.EqualTo(expected));
        Assert.That(pair.ClientVehicles.Host!.Spawns!.RandomState, Is.EqualTo(rng));
        Assert.That(pair.ClientVehicles.ForceDeveloperStart(), Is.True);
        Assert.That(pair.ClientVehicles.RequestItemUse(), Is.True);
        pair.Step(2, host: false);
        Assert.That(pair.ClientVehicles.GiveDeveloperItem(Trackstorm.Core.Items.HeldItem.Missile), Is.True);
        Assert.That(pair.ClientVehicles.LocalItem!.Vehicle, Is.EqualTo(2));
        Assert.That(pair.ClientVehicles.TryConfigure(new Dictionary<string, double> { ["vehicle.acceleration"] = 9 }, out _), Is.True);
        expected = pair.ClientVehicles.Configuration;
        pair.Link.HostWire.DropOutgoing = pair.Link.ClientWire.DropOutgoing = false;
        pair.RestartFormerHost();
        pair.Step(120);
        Assert.That(pair.HostVehicles!.Host, Is.Null);
        Assert.That(pair.HostVehicles.Configuration, Is.EqualTo(expected));
        Assert.That(pair.HostVehicles.TryConfigure(edits, out _), Is.False);
        Assert.That(pair.HostVehicles.GiveDeveloperItem(Trackstorm.Core.Items.HeldItem.Wrench), Is.False);
        Assert.That(pair.HostVehicles.ForceDeveloperStart(), Is.False);
        int rejected = pair.Client.RejectedPackets;
        byte[] stale = Trackstorm.Core.Development.GameplayConfigurationCodec.Encode(pair.Client.State.Match, new(expected.Revision + 1, expected.Configuration));
        pair.Link.Host.Send(new(pair.Host.ServerPeer, ConnectionEnvelope.Encode(pair.Client.State.Session, pair.Host.Generation, stale, 1), TransportDelivery.Reliable));
        pair.Step(2);
        Assert.That(pair.Client.RejectedPackets, Is.GreaterThan(rejected));
        Assert.That(pair.ClientVehicles.Configuration, Is.EqualTo(expected));
        pair.Step(60);
        pair.RetireClientTransport();
        pair.Step(2100, client: false);
        Assert.That(pair.Host.State!.AuthorityEpoch, Is.EqualTo(3));
        Assert.That(pair.HostVehicles.Configuration, Is.EqualTo(expected));
        Assert.That(pair.HostVehicles.Host!.Spawns!.RandomState, Is.EqualTo(rng));
        Assert.That(pair.HostVehicles.GiveDeveloperItem(Trackstorm.Core.Items.HeldItem.Wrench), Is.True);
        Assert.That(pair.HostVehicles.LocalItem!.Vehicle, Is.EqualTo(1));
        Assert.That(pair.HostVehicles.TryConfigure(new Dictionary<string, double> { ["vehicle.acceleration"] = 11 }, out _), Is.True);
        Assert.That(pair.HostVehicles.Configuration.Revision, Is.EqualTo(expected.Revision + 1));
    }

    /// <summary>Neither a stale nor an expanded checkpoint roster permits timeout-only promotion.</summary>
    /// <param name="acknowledgeExpansion">Whether the original client sees the larger checkpoint before partition.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void TwoPlayerRosterExpansionCannotLeaveTwoAuthorities(bool acknowledgeExpansion)
    {
        using var pair = new MigrationPair();
        var thirdId = Id(3);
        var thirdWire = new EosP2pWire(thirdId);
        var routes = new Dictionary<OnlineProductUserId, EosP2pWire> { [pair.Link.HostId] = pair.Link.HostWire, [pair.Link.ClientId] = pair.Link.ClientWire, [thirdId] = thirdWire };
        foreach (var wire in routes.Values)
        {
            wire.Routes = routes;
        }

        pair.Link.Lobby = pair.Link.Lobby with { Members = 3, MemberIds = routes.Keys.ToArray() };
        if (!acknowledgeExpansion)
        {
            pair.Link.HostWire.DropRecipients.Add(pair.Link.ClientId);
            pair.Link.ClientWire.DropOutgoing = true;
        }

        using var gateway = new EosP2pTransport(thirdWire, thirdId, () => pair.Link.Lobby, time: pair.Link.Clock);
        ulong server = gateway.Connect(EosP2pTransport.Endpoint(pair.Link.Lobby, pair.Link.HostId));
        var third = new LobbyNetworkDriver(gateway, 0, server, "Third", identity: _ => pair.Link.HostId.Value);
        third.Reconnect = () => throw new InvalidOperationException("Partitioned fixture");
        third.Migration = new SessionMigration(third, gateway, thirdId.Value, _ => pair.Link.HostId.Value, (subject, _) => gateway.RebindHost(new(subject)), pair.Link.Clock);
        for (int i = 0; i < 150; i++)
        {
            pair.Step(1);
            third.Pump(1.0 / 60);
        }

        Assert.That(third.Migration.Subjects?.Count, Is.EqualTo(3));
        pair.Link.HostWire.DropRecipients.Add(pair.Link.ClientId);
        pair.Link.ClientWire.DropOutgoing = true;
        for (int i = 0; i < 3300; i++)
        {
            pair.Step(1);
            third.Pump(1.0 / 60);
        }

        Assert.That(pair.Host.State!.AuthorityEpoch, Is.EqualTo(1));
        Assert.That(pair.Host.Migration!.Frozen, Is.False);
        Assert.That(pair.Client.Authority, Is.Null, "A missing P2P path cannot retire the healthy host, with either retained roster.");
        Assert.That(pair.Client.Failure, Is.Not.Empty);
    }

    /// <summary>The sole survivor promotes only after departure or lease-safe grace, including a paused old process.</summary>
    /// <param name="loss">Host-loss mode.</param>
    [TestCase("leave")]
    [TestCase("crash")]
    [TestCase("partition")]
    [TestCase("pause")]
    public void TwoPlayerLobbyMigrationFencesOldHost(string loss)
    {
        using var pair = new MigrationPair();
        pair.Host.Request(LobbyCommand.Ready, true);
        pair.Client.Request(LobbyCommand.Ready, true);
        pair.Step(90);
        var before = pair.Client.State!;
        if (loss == "leave")
        {
            pair.Host.BeginLeave();
        }
        else if (loss == "crash")
        {
            pair.RetireHostTransport();
        }
        else if (loss == "partition")
        {
            pair.Link.HostWire.DropOutgoing = pair.Link.ClientWire.DropOutgoing = true;
        }

        if (loss is "partition" or "pause")
        {
            pair.HostServiceLive = false;
        }

        for (int tick = 0; tick < 2100 && pair.Client.Authority is null; tick++)
        {
            pair.Step(1, host: loss is "leave" or "partition");

            if (pair.Client.Authority is not null && loss is "leave" or "partition")
            {
                Assert.That(pair.Host.Migration!.Frozen, Is.True, "Old authority must be frozen at the promotion boundary.");
            }
        }

        pair.Step(180, host: loss is "leave" or "partition");
        if (loss == "pause")
        {
            pair.Host.Pump(1.0 / 60);
            Assert.That(pair.Host.Migration!.Frozen, Is.True, "Monotonic lease time expires before a resumed process can simulate.");
            pair.Host.Pump(1.0 / 60);
            Assert.That(pair.Host.Migration.Frozen, Is.True, "Queued old acknowledgements cannot revive an expired lease.");
        }

        Assert.That(pair.Client.Failure, Is.Empty);
        Assert.That(pair.Client.State!.Session, Is.EqualTo(before.Session));
        Assert.That(pair.Client.State.Match, Is.EqualTo(before.Match));
        Assert.That(pair.Client.State.AuthorityEpoch, Is.EqualTo(2));
        Assert.That(pair.Client.State.CurrentHostId, Is.EqualTo(2));
        Assert.That(pair.Client.LocalPlayerId, Is.EqualTo(2));
        Assert.That(pair.Client.State.Players.Select(player => player.Id), Is.EqualTo(new ulong[] { 2 }));
        Assert.That(pair.Client.State.Players.All(player => player.Ready), Is.True);
        Assert.That(pair.Client.Migration!.Frozen, Is.False, "The new sole host does not require an acknowledgement from its reserved former host.");
    }

    /// <summary>Two-player bidirectional and acknowledgement-only outages recover through TS-45 without an election.</summary>
    /// <param name="oneWay">Whether only client acknowledgements are lost.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void TwoPlayerTransientLossRetainsEpoch(bool oneWay)
    {
        using var pair = new MigrationPair();
        pair.StartArena();
        if (!oneWay)
        {
            pair.Link.Host.Disconnect(pair.Host.Authority!.Peers.Keys.Single());
            pair.Link.Client.Disconnect(pair.Client.ServerPeer);
        }

        pair.Link.ClientWire.DropOutgoing = true;
        pair.Link.HostWire.DropOutgoing = !oneWay;
        pair.Step(180);
        Assert.That(pair.Host.Migration!.Frozen, Is.False);
        Assert.That(pair.Host.State!.Players.Select(player => player.Id), Is.EquivalentTo(new ulong[] { 1, 2 }));
        if (!oneWay)
        {
            Assert.That(pair.Host.State.Players.Single(player => player.Id == 2).Connected, Is.False);
        }

        ulong tick = pair.HostVehicles!.Host!.World.State.Tick;
        pair.Step(60);
        Assert.That(pair.HostVehicles.Host.World.State.Tick, Is.GreaterThan(tick));
        pair.Link.ClientWire.DropOutgoing = pair.Link.HostWire.DropOutgoing = false;
        // Model EOS closing the remote side when the client's full input window resets its connection.
        if (oneWay)
        {
            pair.Link.Host.Disconnect(pair.Host.Authority!.Peers.Keys.Single());
            pair.Link.Client.Disconnect(pair.Client.ServerPeer);
        }

        pair.Client.Reconnect = () => pair.Link.Client.RebindHost(pair.Link.HostId);
        pair.Step(300);
        Assert.That(pair.Host.Failure, Is.Empty);
        Assert.That(pair.Client.Failure, Is.Empty);
        Assert.That(pair.Host.State!.AuthorityEpoch, Is.EqualTo(1));
        Assert.That(pair.Client.State!.AuthorityEpoch, Is.EqualTo(1));
        Assert.That(pair.Host.Migration.Frozen, Is.False);
        Assert.That(pair.Client.Migration!.Frozen, Is.False, $"{pair.Client.ResumeStatus}; {pair.Client.Migration.Diagnostics}; reconnect={pair.Client.Reconnecting}; rejected={pair.Client.RejectedPackets}; peers={string.Join(',', pair.Link.Client.Connections.Values)}");
        Assert.That(pair.ClientVehicles!.IsActive, Is.True);
        Assert.That(pair.Client.LocalPlayerId, Is.EqualTo(2));
        Assert.That(pair.Client.Generation, Is.EqualTo(2));
        Assert.That(pair.ClientVehicles.Latest!.Vehicles.Select(vehicle => vehicle.State.VehicleId), Is.EquivalentTo(new ulong[] { 1, 2 }));
    }

    /// <summary>P2P loss never retires a healthy authority, even when only one other player survives.</summary>
    [Test]
    public void TwoPlayerP2pPartitionCannotGrantAuthority()
    {
        using var pair = new MigrationPair();
        pair.StartArena();
        ulong before = pair.HostVehicles!.Host!.World.State.Tick;
        pair.Link.HostWire.DropOutgoing = pair.Link.ClientWire.DropOutgoing = true;
        pair.Step(2100);
        Assert.That(pair.Client.Authority, Is.Null);
        Assert.That(pair.Client.State!.AuthorityEpoch, Is.EqualTo(1));
        Assert.That(pair.Client.Failure, Is.Not.Empty);
        Assert.That(pair.Host.State!.AuthorityEpoch, Is.EqualTo(1));
        Assert.That(pair.Host.Migration!.Frozen, Is.False);
        Assert.That(pair.HostVehicles.Host.World.State.Tick, Is.GreaterThan(before));
    }

    /// <summary>An isolated survivor cannot restore a pre-partition checkpoint after the recoverability window.</summary>
    [Test]
    public void TwoPlayerLongPartitionThenConfirmedHostLossRejectsExpiredCheckpoint()
    {
        using var pair = new MigrationPair();
        pair.StartArena();
        ulong observed = pair.ClientVehicles!.Latest!.Tick;
        ulong hostTick = pair.HostVehicles!.Host!.World.State.Tick;
        pair.Link.HostWire.DropOutgoing = pair.Link.ClientWire.DropOutgoing = true;

        pair.Step(300);

        Assert.That(pair.Host.Migration!.Frozen, Is.False, "An isolated client cannot pause a healthy host.");
        Assert.That(pair.HostVehicles.Host.World.State.Tick, Is.GreaterThan(hostTick));
        Assert.That(pair.ClientVehicles.Latest!.Tick, Is.EqualTo(observed));
        Assert.That(pair.Client.Migration!.Subjects, Is.Not.Null, "The survivor still retains its external pre-partition checkpoint.");

        pair.Client.Migration!.HostProgressAt = () => pair.Link.Clock.GetTimestamp();
        pair.RetireHostTransport();
        for (int i = 0; i < 2100 && pair.Client.Failure.Length == 0; i++)
        {
            pair.Step(1, host: false);
        }

        Assert.That(pair.Client.Failure, Does.Contain("Host migration failed"));
        Assert.That(pair.Client.Authority, Is.Null);
        Assert.That(pair.Client.State!.AuthorityEpoch, Is.EqualTo(1));
        Assert.That(pair.ClientVehicles.Host, Is.Null);
    }

    /// <summary>The actual framed reconnect restores the former host as a client and permits a later reverse migration.</summary>
    [Test]
    public void TwoPlayerFormerHostResumesAndMigratesAgain()
    {
        using var pair = new MigrationPair();
        pair.StartArena();
        var retired = pair.Host.State!;
        pair.RetireHostTransport();
        pair.Step(1950, host: false);
        Assert.That(pair.Client.State!.AuthorityEpoch, Is.EqualTo(2));
        pair.RestartFormerHost();
        pair.Step(120);
        Assert.That(pair.Host.Failure, Is.Empty);
        Assert.That(pair.Host.LocalPlayerId, Is.EqualTo(1));
        Assert.That(pair.Host.Generation, Is.EqualTo(2));
        Assert.That(pair.Host.Authority, Is.Null);
        int rejected = pair.Client.RejectedPackets;
        pair.Host.Request(LobbyCommand.Start);
        pair.Step(2);
        Assert.That(pair.Client.RejectedPackets, Is.GreaterThan(rejected));
        Assert.That(pair.Client.State!.Phase, Is.EqualTo(SessionPhase.Arena));
        rejected = pair.Client.RejectedPackets;
        pair.Link.Host.Send(new(pair.Host.ServerPeer, LobbyCodec.EncodeCommand(LobbyCommand.Ready, retired, true), TransportDelivery.Reliable));
        pair.Step(2);
        Assert.That(pair.Client.RejectedPackets, Is.GreaterThan(rejected));
        pair.RetireClientTransport();
        pair.Step(2100, client: false);
        Assert.That(pair.Host.Failure, Is.Empty);
        Assert.That(pair.Host.State!.AuthorityEpoch, Is.EqualTo(3));
        Assert.That(pair.Host.State.CurrentHostId, Is.EqualTo(1));
        Assert.That(pair.Host.Migration!.Frozen, Is.False);
    }

    /// <summary>Only an accepted reliable current-host checkpoint can supply a client's restart route.</summary>
    /// <param name="invalid">The rejected boundary following a valid route.</param>
    [TestCase("peer")]
    [TestCase("unreliable")]
    [TestCase("epoch")]
    [TestCase("sequence")]
    [TestCase("corrupt")]
    public void MigrationRoutingRequiresAcceptedCheckpoint(string invalid)
    {
        using var pair = new MigrationPair();
        pair.AttachClientMigration();
        var lobby = pair.Host.Authority!.Capture(pair.Link.HostId.Value);
        string routingId = new('B', 64);
        byte[] accepted = [(byte)'T', (byte)'X', 1, .. MigrationCheckpointCodec.Encode(new(100, lobby, null, null, new string('A', 64), routingId))];
        pair.Client.Migration!.Receive(new(pair.Client.ServerPeer, accepted, TransportDelivery.Reliable));
        Assert.That(pair.Client.Migration.RoutingId, Is.EqualTo(routingId));
        if (invalid == "epoch")
        {
            var state = lobby.State;
            var wrong = new LobbySnapshot(state.Session, state.Revision, state.Match, state.Phase, state.Players, state.CurrentHostId, 2);
            lobby = new(wrong, lobby.Tick, lobby.NextId, lobby.Subjects);
        }

        byte[] rejected = [(byte)'T', (byte)'X', 1, .. MigrationCheckpointCodec.Encode(new(invalid == "sequence" ? 100UL : 101UL, lobby, null, null, new string('A', 64), new string('C', 64)))];
        if (invalid == "corrupt")
        {
            rejected[^1] ^= 1;
        }

        pair.Client.Migration.Receive(new(invalid == "peer" ? pair.Client.ServerPeer + 123 : pair.Client.ServerPeer, rejected, invalid == "unreliable" ? TransportDelivery.Unreliable : TransportDelivery.Reliable));
        Assert.That(pair.Client.Migration.RoutingId, Is.EqualTo(routingId));
        Assert.That(pair.Client.Authority, Is.Null);
    }

    /// <summary>No externally retained boundary must produce bounded failure rather than authority.</summary>
    [Test]
    public void TwoPlayerMissingCheckpointFailsClosed()
    {
        using var pair = new MigrationPair();
        pair.AttachClientMigration();
        pair.RetireHostTransport();
        pair.Step(2100, host: false);
        Assert.That(pair.Client.Authority, Is.Null);
        Assert.That(pair.Client.State!.AuthorityEpoch, Is.EqualTo(1));
        Assert.That(pair.Client.Failure, Does.Contain("Host migration failed"));
    }

    /// <summary>The newest recoverable external boundary restores two-player combat, and invalid choices fail closed.</summary>
    /// <param name="boundary">Validity of the externally retained arena state.</param>
    [TestCase("valid")]
    [TestCase("stale")]
    [TestCase("future")]
    [TestCase("corrupt")]
    [TestCase("wrong-epoch")]
    [TestCase("finished-observed")]
    public void TwoPlayerArenaRestoresLatestSafeCheckpoint(string boundary)
    {
        using var pair = new MigrationPair();
        pair.StartArena();
        var host = pair.HostVehicles!.Host!;
        ulong match = pair.Client.State!.Match;
        var older = pair.Host.Migration!.CaptureArena!();
        byte[] olderBytes = MigrationCheckpointCodec.Encode(new MigrationCheckpoint(99, pair.Host.Authority!.Capture(pair.Link.HostId.Value), older.Arena, older.Host));
        // Seed a lethal authoritative boundary after the countdown, with one retained item and respawn/score state.
        ulong tick = host.World.State.Tick + 1;
        var frame = new InputFrame(tick, 0, 0, 0, 0, 0, 0);
        host.World.Step(frame, host.World.State.Vehicles.Select(vehicle => new VehicleStepRequest(
            vehicle.VehicleId,
            frame,
            Observe(vehicle),
            vehicle.VehicleId == 1 ? [new VehicleEffectRequest(new DamageEffect(100, Vector3.Zero, Vector3.Zero), new DamageContext("missile", 2, "two-player-migration"))] : [])).ToArray());
        var captured = pair.Host.Migration!.CaptureArena!();
        var checkpoint = new MigrationCheckpoint(100, pair.Host.Authority!.Capture(pair.Link.HostId.Value), captured.Arena, captured.Host);
        var dead = host.World.GetVehicle(1);
        Assert.That(dead.RespawnAtTick, Is.GreaterThan(0));
        // Start with no retained copies so invalid input cannot fall back to an unrelated older checkpoint.
        pair.AttachClientMigration();
        var vehicleDriver = new VehicleNetworkDriver(pair.Link.Client, 0, pair.Client.ServerPeer, pair.Client);
        pair.ReplaceClientVehicles(vehicleDriver);
        pair.Client.Migration!.ObservedTick = () => boundary == "stale" ? tick + 241 : boundary == "future" ? tick - 1 : tick;
        if (boundary == "finished-observed")
        {
            pair.Client.Migration.ObservedMatch = () => new Core.Matches.MatchState(tick, 999, 5, Core.Matches.MatchPhase.Finished, null, 2,
                [new Core.Matches.PlayerScore(2, 0, 0, 1, 0)]);
        }
        if (boundary == "valid")
        {
            byte[] olderPacket = [(byte)'T', (byte)'X', 1, .. olderBytes];
            pair.Client.Migration.Receive(new(pair.Client.ServerPeer, olderPacket, TransportDelivery.Reliable));
        }

        byte[] bytes = MigrationCheckpointCodec.Encode(checkpoint);
        if (boundary == "corrupt")
        {
            bytes[^1] ^= 1;
        }
        else if (boundary == "wrong-epoch")
        {
            var state = checkpoint.Lobby.State;
            var wrong = new LobbySnapshot(state.Session, state.Revision, state.Match, state.Phase, state.Players, state.CurrentHostId, 2);
            var lobby = checkpoint.Lobby;
            bytes = MigrationCheckpointCodec.Encode(new MigrationCheckpoint(100, new LobbyRestoreState(wrong, lobby.Tick, lobby.NextId, lobby.Subjects, lobby.Configuration), captured.Arena, captured.Host));
        }

        byte[] packet = [(byte)'T', (byte)'X', 1, .. bytes];
        pair.Client.Migration!.Receive(new(pair.Client.ServerPeer, packet, TransportDelivery.Reliable));
        Trackstorm.Core.Networking.Replication.WorldSnapshot? restored = null;
        pair.ClientVehicles!.Resynchronized += world => restored = world;
        pair.Client.Migration!.HostProgressAt = () => pair.Link.Clock.GetTimestamp();
        pair.RetireHostTransport();
        for (int i = 0; i < 2100 && restored is null && pair.Client.Failure.Length == 0; i++)
        {
            pair.Step(1, host: false);
        }

        if (boundary != "valid")
        {
            Assert.That(pair.Client.Failure, Does.Contain("Host migration failed"));
            Assert.That(pair.Client.Authority, Is.Null);
            Assert.That(pair.ClientVehicles.Host, Is.Null);
            return;
        }

        Assert.That(pair.Client.Failure, Is.Empty);
        Assert.That(restored!.Tick, Is.EqualTo(tick));
        Assert.That(pair.Client.State!.Session, Is.EqualTo(pair.Link.Lobby.Session));
        Assert.That(pair.Client.State.Match, Is.EqualTo(match));
        Assert.That(pair.Client.State.AuthorityEpoch, Is.EqualTo(2));
        var replacement = pair.ClientVehicles.Host!;
        Assert.That(restored.Vehicles.Select(vehicle => vehicle.State.VehicleId), Is.EqualTo(new ulong[] { 1, 2 }));
        Assert.That(restored.Vehicles.Single(vehicle => vehicle.State.VehicleId == 1).State.Damage, Is.EqualTo(dead.Damage));
        Assert.That(replacement.World.GetVehicle(1).RespawnAtTick, Is.EqualTo(dead.RespawnAtTick));
        Assert.That(replacement.Items.Slots, Is.EqualTo(host.Items.Slots));
        Assert.That(replacement.Spawns!.States, Is.EqualTo(host.Spawns!.States));
        Assert.That(replacement.Spawns.RandomState, Is.EqualTo(host.Spawns.RandomState));
        Assert.That(replacement.World.State.Match!.Players.Single(player => player.Player == 2).Kills, Is.EqualTo(1));
        pair.Step(200, host: false);
        Assert.That(replacement.World.GetVehicle(1).LifeId, Is.EqualTo(dead.LifeId + 1));
        Assert.That(replacement.World.State.Match.Players.Single(player => player.Player == 2).Kills, Is.EqualTo(1));
        Assert.That(replacement.World.State.Match.Players.Single(player => player.Player == 1).Deaths, Is.EqualTo(1));
        Assert.That(pair.Client.Migration.Frozen, Is.False);
    }

    /// <summary>Three authenticated peers restore the same match after losing the original authority.</summary>
    /// <param name="arena">Whether to migrate a running match instead of its lobby.</param>
    /// <param name="agree">Whether every eligible survivor remains available.</param>
    /// <param name="recover">Whether the original authority returns within grace.</param>
    /// <param name="oneWay">Whether only client-to-host delivery fails.</param>
    /// <param name="trustedLease">Whether to fence through the real conditional store.</param>
    [TestCase(false, true, false, false, false)]
    [TestCase(true, true, false, false, false)]
    [TestCase(true, false, false, false, false)]
    [TestCase(true, true, true, false, false)]
    [TestCase(true, true, true, true, false)]
    [TestCase(false, true, false, false, true)]
    [TestCase(true, true, false, false, true)]
    [TestCase(true, false, false, false, true)]
    public void MigratesLobbyAndActiveMatchThroughProductionFraming(bool arena, bool agree, bool recover, bool oneWay, bool trustedLease)
    {
        var identities = Enumerable.Range(1, 3).Select(Id).ToArray();
        var lobby = new OnlineLobby("migration", "Migration", identities[0], 100, LobbyAccess.Public, 3, 8, OnlineLobby.CurrentProtocol, true, null) { MemberIds = identities };
        var wires = identities.Select(identity => new EosP2pWire(identity)).ToArray();
        var routes = wires.Select((wire, index) => (wire, index)).ToDictionary(entry => identities[entry.index], entry => entry.wire);
        var gateways = identities.Select((identity, index) => new EosP2pTransport(wires[index], identity, () => lobby)).ToArray();
        var subjects = Enumerable.Range(0, 3).Select(_ => new Dictionary<ulong, string>()).ToArray();
        var drivers = new LobbyNetworkDriver[3];
        var clock = new Clock();
        var leaseClients = new List<AuthorityLeaseClient>();
        using var store = trustedLease ? new LeaseStore(clock) : null;
        void VerifyMigratedEvents()
        {
            drivers[1].Events.Record(Trackstorm.Core.Events.EventCategory.Network, "After migration", actor: 2);
            for (int tick = 0; tick < 10; tick++)
            {
                if (trustedLease)
                {
                    clock.Advance(1.0 / 60);
                }

                drivers[1].Pump(1.0 / 60);
                drivers[2].Pump(1.0 / 60);
            }

            Assert.That(drivers[2].Events.Entries.Any(entry => entry.Kind == "After migration" && entry.Actor == 2), Is.True, "The new epoch must not inherit the old journal's deduplication watermark.");
            ulong generation = drivers[2].Generation;
            byte[] body = Trackstorm.Core.Events.EventCodec.Encode([new Trackstorm.Core.Events.RuntimeEvent { Sequence = drivers[2].Events.LastSequence + 100, Category = Trackstorm.Core.Events.EventCategory.Network, Kind = "Old epoch event" }]);
            ulong peer = drivers[1].Authority!.Peers.Single(pair => pair.Value == 3).Key;
            byte[] stale = [(byte)'T', (byte)'E', 1, .. ConnectionEnvelope.Encode(100, generation, body, 1)];
            gateways[1].Send(new(peer, stale, TransportDelivery.Reliable));
            int rejected = drivers[2].RejectedPackets;
            drivers[2].Pump(1.0 / 60);
            Assert.That(drivers[2].RejectedPackets, Is.GreaterThan(rejected));
            Assert.That(drivers[2].Events.Entries.Any(entry => entry.Kind == "Old epoch event"), Is.False);
        }

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
                drivers[i].Migration = new SessionMigration(drivers[i], gateways[i], identities[i].Value, peer => subjects[index].GetValueOrDefault(peer), (subject, _) => gateways[index].RebindHost(new OnlineProductUserId(subject)), clock);
                drivers[i].Migration!.RetirementConfirmedAt = _ => !gateways[0].IsListening ? 0L : null;
                if (trustedLease)
                {
                    var driver = drivers[i];
                    var lease = new AuthorityLeaseClient(new LeaseTransport(store!, identities[i].Value), identities[i].Value, clock);
                    leaseClients.Add(lease);
                    driver.Migration!.LeaseSession = new string('D', 64);
                    driver.Migration.PollCoordination = () => lease.Poll(driver.Migration.LeaseSession, index == 0, driver.State?.AuthorityEpoch ?? 1, driver.Migration.NeedsLeaseObservation);
                    driver.Migration.AuthorityAvailable = () => lease.Available(driver.State!.AuthorityEpoch);
                    driver.Migration.RetirementConfirmedAt = checkpoint => lease.Expired(checkpoint.Lobby.State.AuthorityEpoch, checkpoint.Lobby.Subjects[checkpoint.Lobby.State.CurrentHostId]) ? clock.GetTimestamp() : null;
                    driver.Migration.AcquireAuthority = checkpoint => lease.Acquire(checkpoint.Lobby.State.AuthorityEpoch);
                    driver.Migration.ConfirmSuccessor = (checkpoint, candidate) => lease.Confirms(checkpoint.Lobby.State.AuthorityEpoch + 1, checkpoint.Lobby.Subjects[candidate]);
                    driver.Migration.HostProgressAt = () => lease.HostProgressAt;
                }
            }

            for (int tick = 0; tick < 120; tick++)
            {
                if (trustedLease)
                {
                    clock.Advance(1.0 / 60);
                }

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
                    if (trustedLease)
                    {
                        clock.Advance(1.0 / 60);
                    }

                    drivers[1].Pump(1.0 / 60);
                    drivers[2].Pump(1.0 / 60);
                }

                Assert.That(drivers[1].State!.AuthorityEpoch, Is.EqualTo(2), $"candidate: {drivers[1].Failure}; {drivers[1].Migration!.Diagnostics}; voter: {drivers[2].Failure}; {drivers[2].Migration!.Diagnostics}");
                Assert.That(drivers[2].State!.AuthorityEpoch, Is.EqualTo(2), drivers[2].Failure);
                Assert.That(drivers[1].State!.Players.All(player => !player.Ready), Is.True);
                Assert.That(drivers[1].LocalPlayerId, Is.EqualTo(2));
                Assert.That(drivers[2].LocalPlayerId, Is.EqualTo(3));
                VerifyMigratedEvents();
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
                if (trustedLease)
                {
                    clock.Advance(1.0 / 60);
                }

                foreach (var vehicle in vehicles)
                {
                    vehicle.Advance(default, Observe);
                }
            }

            if (recover)
            {
                foreach (ulong peer in drivers[0].Authority!.Peers.Keys)
                {
                    if (!oneWay)
                    {
                        gateways[0].Disconnect(peer);
                    }
                }

                bool available = false;
                for (int i = 1; i < 3; i++)
                {
                    int index = i;
                    if (!oneWay)
                    {
                        gateways[i].Disconnect(drivers[i].ServerPeer);
                    }

                    drivers[i].Reconnect = () => available ? gateways[index].RebindHost(identities[0]) : throw new InvalidOperationException("Transient outage");
                }

                ulong frozenTick = 0;
                for (int tick = 0; tick < 600; tick++)
                {
                    if (trustedLease)
                    {
                        clock.Advance(1.0 / 60);
                    }

                    if (tick == 180)
                    {
                        Assert.That(drivers[0].Migration!.Frozen, Is.False);

                        frozenTick = vehicles[0].Host!.World.State.Tick;
                    }

                    if (tick == 239)
                    {
                        Assert.That(vehicles[0].Host!.World.State.Tick, Is.GreaterThan(frozenTick));
                    }

                    available = tick >= 240;
                    if (oneWay && tick == 240)
                    {
                        foreach (ulong peer in drivers[0].Authority!.Peers.Keys.ToArray())
                        {
                            gateways[0].Disconnect(peer);
                        }

                        foreach (int index in new[] { 1, 2 })
                        {
                            gateways[index].Disconnect(drivers[index].ServerPeer);
                        }
                    }

                    wires[1].DropOutgoing = wires[2].DropOutgoing = oneWay && !available;
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
                var common = new MigrationCheckpoint(100, drivers[0].Authority!.Capture(identities[0].Value), captured.Arena, captured.Host, drivers[0].Migration!.LeaseSession);
                byte[] commonBytes = MigrationCheckpointCodec.Encode(common);
                foreach (int index in new[] { 1, 2 })
                {
                    byte[] packet = [(byte)'T', (byte)'X', 1, .. commonBytes];
                    drivers[index].Migration!.Receive(new(drivers[index].ServerPeer, packet, TransportDelivery.Reliable));
                }

                // Only the candidate sees the newest boundary; agreement must choose the older common copy.
                vehicles[0].Host!.Items.Grant(vehicles[0].Host!.World, 3, Trackstorm.Core.Items.HeldItem.Missile);
                captured = drivers[0].Migration!.CaptureArena!();
                var newest = new MigrationCheckpoint(101, common.Lobby, captured.Arena, captured.Host, drivers[0].Migration!.LeaseSession);
                byte[] newestPacket = [(byte)'T', (byte)'X', 1, .. MigrationCheckpointCodec.Encode(newest)];
                drivers[1].Migration!.Receive(new(drivers[1].ServerPeer, newestPacket, TransportDelivery.Reliable));
                // Reliable checkpoint delivery can precede its unreliable world snapshot.
                vehicles[0].Host!.Step(default, Observe);
                captured = drivers[0].Migration!.CaptureArena!();
                var ahead = new MigrationCheckpoint(102, common.Lobby, captured.Arena, captured.Host, drivers[0].Migration!.LeaseSession);
                byte[] aheadPacket = [(byte)'T', (byte)'X', 1, .. MigrationCheckpointCodec.Encode(ahead)];
                drivers[1].Migration!.Receive(new(drivers[1].ServerPeer, aheadPacket, TransportDelivery.Reliable));
                vehicles[1].Resynchronized += world => selectedTick ??= world.Tick;
            }

            gateways[0].Stop();
            if (!agree)
            {
                gateways[2].Stop();
            }

            for (int tick = 0; tick < (agree ? 2100 : 3300); tick++)
            {
                if (trustedLease)
                {
                    clock.Advance(1.0 / 60);
                }

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
            Assert.That(vehicles[1].Host!.World.Events, Is.SameAs(drivers[1].Events));
            VerifyMigratedEvents();
        }
        finally
        {
            foreach (var lease in leaseClients)
            {
                lease.Dispose();
            }

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
        Assert.That(pair.Host.Capabilities, Is.EqualTo(TransportCapabilities.Ping));
        Assert.That(pair.Host.GetStatistics(99), Is.EqualTo(default(TransportStatistics)));
        Assert.Throws<NotSupportedException>(() => pair.Host.ConfigureSimulation(new()));
    }

    /// <summary>Timed real framing supplies neutral RTT, expires silence and rejects old-generation replies.</summary>
    [Test]
    public void SamplesLatencyExpiresAndRejectsRetiredReplies()
    {
        using var pair = new Pair();
        pair.Pump();
        Assert.That(pair.Client.GetStatistics(pair.Server).PingMilliseconds, Is.Null);
        pair.Clock.Advance(1);
        pair.Client.Poll();
        pair.Clock.Advance(0.04);
        pair.Host.Poll();
        var oldReply = pair.ClientWire.Packets.Single(packet => packet.Bytes[0] == 6);
        pair.Clock.Advance(0.04);
        pair.Client.Poll();
        Assert.That(pair.Client.GetStatistics(pair.Server).PingMilliseconds, Is.EqualTo(80));
        Assert.That(pair.Client.GetStatistics(pair.Server).IncomingQuality, Is.Null);
        Assert.That(pair.Client.TryReceive(out _), Is.False, "Probes never become gameplay messages.");
        pair.Clock.Advance(5);
        Assert.That(pair.Client.GetStatistics(pair.Server).PingMilliseconds, Is.Null, "Silent samples expire without requiring a native disconnect.");
        pair.ClientWire.Packets.Enqueue(oldReply);
        pair.Client.Poll();
        Assert.That(pair.Client.GetStatistics(pair.Server).PingMilliseconds, Is.Null, "Duplicate replies cannot refresh an expired sample.");
        ulong oldPeer = pair.Server;
        pair.Client.Stop();
        pair.Host.Stop();
        pair.Host.Listen(EosP2pTransport.Endpoint(pair.Lobby, pair.HostId));
        pair.Server = pair.Client.Connect(EosP2pTransport.Endpoint(pair.Lobby, pair.HostId));
        pair.Pump();
        pair.Clock.Advance(1);
        pair.Client.Poll();
        pair.ClientWire.Packets.Enqueue(oldReply);
        pair.Client.Poll();
        Assert.That(pair.Client.GetStatistics(oldPeer).PingMilliseconds, Is.Null);
        Assert.That(pair.Client.GetStatistics(pair.Server).PingMilliseconds, Is.Null, "Matching probe IDs with retired nonces are rejected.");
        pair.Clock.Advance(0.1);
        pair.Host.Poll();
        pair.Clock.Advance(0.1);
        pair.Client.Poll();
        Assert.That(pair.Client.GetStatistics(pair.Server).PingMilliseconds, Is.EqualTo(200));
        pair.Host.Poll();
        ulong clientPeer = pair.Host.Connections.Keys.Single();
        int? hostPing = pair.Host.GetStatistics(clientPeer).PingMilliseconds;
        Assert.That(hostPing, Is.Not.Null);
        var roster = new LobbySnapshot(17, 1, 17, SessionPhase.Lobby, [new(1, "Host", false), new(2, "Client", false)]);
        var latency = new PlayerLatency();
        latency.Sample(roster, new Dictionary<ulong, ulong> { [clientPeer] = 2 }, pair.Host);
        Assert.That(latency.Get(roster, 2), Is.EqualTo(hostPing), "Standings reuse EOS statistics without another probe mechanism.");
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
    public void ReconnectsArenaWithOneVehicleAndFreshCheckpoint()
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
        host.Request(LobbyCommand.Ready, true);
        host.Pump(1.0 / 60);
        Assert.That(host.Request(LobbyCommand.Start), Is.True);
        client.Pump(1.0 / 60);
        var authority = new VehicleNetworkDriver(pair.Host, host.State!.Match, lobby: host);
        var replica = new VehicleNetworkDriver(pair.Client, 0, client.ServerPeer, client);
        authority.Host!.RegisterSpawns(Trackstorm.Core.Arenas.PrototypeArena.Configuration);
        authority.Host.Items.Grant(authority.Host.World, player, Trackstorm.Core.Items.HeldItem.Wrench);
        for (int i = 0; i < 20; i++)
        {
            authority.Advance(default, Observe);
            replica.Advance(default, Observe);
        }

        int resyncs = 0;
        replica.Resynchronized += _ => resyncs++;
        for (int cycle = 0; cycle < 4; cycle++)
        {
            ulong oldPeer = host.Authority!.Peers.Keys.Single();
            ulong generation = client.Generation;
            pair.Host.Disconnect(oldPeer);
            pair.Client.Disconnect(client.ServerPeer);
            Assert.That(TransportDiagnostics.Capture(pair.Client, client).Statistics.PingMilliseconds, Is.Null);
            client.Pump(0);
            Assert.That(TransportDiagnostics.Capture(pair.Client, client).State, Is.EqualTo(ConnectionDiagnosticState.Reconnecting));
            host.Pump(181);
            client.Pump(181);
            Assert.That(host.State!.Players.Count, Is.EqualTo(2), "An ordinary client remains reserved beyond both former deadlines.");
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
        var routes = new Dictionary<OnlineProductUserId, EosP2pWire>();
        var gateways = new List<EosP2pTransport>();
        var members = Enumerable.Range(1, players).Select(Id).ToArray();
        var lobby = new OnlineLobby("measure", "Measure", Id(1), 91, LobbyAccess.Public, players, 8, OnlineLobby.CurrentProtocol, true, null) { MemberIds = members };
        try
        {
            foreach (var member in members)
            {
                routes.Add(member, new EosP2pWire(member) { Routes = routes });
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

    private sealed class MigrationPair : IDisposable
    {
        private readonly List<AuthorityLeaseClient> _leaseClients = new();
        private readonly List<LeaseTransport> _leaseTransports = new();
        private LeaseStore? _leases;
        private bool _hostServiceLive = true;
        private bool _clientServiceLive = true;
        private long? _hostRetiredAt;
        private long? _clientRetiredAt;

        internal MigrationPair()
        {
            Link.Host.Authorize = (peer, identity, _) =>
            {
                Subjects[peer] = identity.Value;
                return true;
            };
            Link.Client.Authorize = (_, _, _) => true;
            Host = new LobbyNetworkDriver(Link.Host, Link.Lobby.Session, 0, "Host", identity: peer => Subjects.GetValueOrDefault(peer));
            Client = new LobbyNetworkDriver(Link.Client, 0, Link.Server, "Client", expectedSession: Link.Lobby.Session, identity: _ => Link.HostId.Value);
            Host.Reconnect = () => throw new InvalidOperationException("Replacement unavailable");
            Host.Migration = new SessionMigration(Host, Link.Host, Link.HostId.Value, peer => Subjects.GetValueOrDefault(peer), (subject, _) => Link.Host.RebindHost(new(subject)), Link.Clock);
            Host.Migration.AuthorityAvailable = () => HostServiceLive;
            Host.Migration.RetirementConfirmedAt = RetiredAt;
            AttachClientMigration();
            Step(150);
            Assert.That(Client.Migration!.Subjects?.Count, Is.EqualTo(2));
        }

        internal Pair Link { get; } = new();
        internal Dictionary<ulong, string> Subjects { get; } = new();
        internal LobbyNetworkDriver Host { get; private set; }
        internal LobbyNetworkDriver Client { get; }
        internal VehicleNetworkDriver? HostVehicles { get; private set; }
        internal VehicleNetworkDriver? ClientVehicles { get; private set; }

        internal bool HostServiceLive
        {
            get => _hostServiceLive;
            set
            {
                if (_hostServiceLive && !value)
                {
                    _hostRetiredAt = Link.Clock.GetTimestamp();
                }

                _hostServiceLive = value;
            }
        }

        internal bool ClientServiceLive
        {
            get => _clientServiceLive;
            set
            {
                if (_clientServiceLive && !value)
                {
                    _clientRetiredAt = Link.Clock.GetTimestamp();
                }

                _clientServiceLive = value;
            }
        }

        public void Dispose()
        {
            Link.Dispose();
            _leaseClients.ForEach(client => client.Dispose());
            _leases?.Dispose();
        }

        internal void EnableLeases()
        {
            _leases = new(Link.Clock);
            Host.Migration!.LeaseSession = new string('A', 64);
            AttachLease(Host, Link.HostId.Value, true);
            AttachLease(Client, Link.ClientId.Value, false);
        }

        internal void SetLeaseOutage()
        {
            foreach (var transport in _leaseTransports)
            {
                transport.Offline = true;
            }
        }

        internal void AttachClientMigration()
        {
            Client.Reconnect = () => throw new InvalidOperationException("Host unavailable");
            Client.Migration = new SessionMigration(Client, Link.Client, Link.ClientId.Value, _ => Link.HostId.Value, (subject, _) => Link.Client.RebindHost(new(subject)), Link.Clock);
            Client.Migration.AuthorityAvailable = () => ClientServiceLive;
            Client.Migration.RetirementConfirmedAt = RetiredAt;
        }

        internal void ReplaceClientVehicles(VehicleNetworkDriver driver) => ClientVehicles = driver;

        internal void RetireHostTransport()
        {
            _hostRetiredAt ??= Link.Clock.GetTimestamp();
            Link.Host.Stop();
        }

        internal void RetireClientTransport()
        {
            _clientRetiredAt ??= Link.Clock.GetTimestamp();
            Link.Client.Stop();
        }

        internal void RestartFormerHost()
        {
            HostServiceLive = true;
            HostVehicles = null;
            ulong server = Link.Host.RebindHost(Link.ClientId);
            Host = new LobbyNetworkDriver(Link.Host, 0, server, "Former host", expectedSession: Link.Lobby.Session, expectedEpoch: 2);
            Host.BeginResume(1, 1);
            Host.Reconnect = () => throw new InvalidOperationException("Replacement unavailable");
            Host.Migration = new SessionMigration(Host, Link.Host, Link.HostId.Value, _ => Link.ClientId.Value, (subject, _) => Link.Host.RebindHost(new(subject)), Link.Clock);
            Host.Migration.AuthorityAvailable = () => HostServiceLive;
            Host.Migration.RetirementConfirmedAt = RetiredAt;
            if (_leases is not null)
            {
                AttachLease(Host, Link.HostId.Value, false);
            }
        }

        internal void StartArena(bool waiting = false)
        {
            Host.Request(LobbyCommand.Ready, true);
            Client.Request(LobbyCommand.Ready, true);
            Step(2);
            Assert.That(Host.Request(LobbyCommand.Start), Is.True);
            Step(2);
            HostVehicles = new VehicleNetworkDriver(Link.Host, Host.State!.Match, 0, Host);
            ClientVehicles = new VehicleNetworkDriver(Link.Client, 0, Client.ServerPeer, Client);
            HostVehicles.Host!.RegisterSpawns(Trackstorm.Core.Arenas.PrototypeArena.Configuration);
            if (waiting)
            {
                Assert.That(HostVehicles.TryConfigure(new Dictionary<string, double> { ["match.minimum_players"] = 3 }, out _), Is.True);
            }

            HostVehicles.Host.Items.Grant(HostVehicles.Host.World, 2, Trackstorm.Core.Items.HeldItem.Wrench);
            Step(240);
        }

        internal void Step(int count, bool host = true, bool client = true)
        {
            for (int i = 0; i < count; i++)
            {
                Link.Clock.Advance(1.0 / 60);
                if (host)
                {
                    if (HostVehicles is null && Host.Authority is null && Host.State?.Phase == SessionPhase.Arena)
                    {
                        // A different local persisted preset cannot override a joining/former host's resync boundary.
                        HostVehicles = new VehicleNetworkDriver(Link.Host, 0, Host.ServerPeer, Host, configuration: new() { Vehicle = new() { Acceleration = 80 } });
                    }

                    if (HostVehicles is null)
                    {
                        Host.Pump(1.0 / 60);
                    }
                    else
                    {
                        HostVehicles.Advance(default, Observe);
                    }
                }

                if (client)
                {
                    if (ClientVehicles is null)
                    {
                        Client.Pump(1.0 / 60);
                    }
                    else
                    {
                        ClientVehicles.Advance(default, Observe);
                    }
                }
            }
        }

        private void AttachLease(LobbyNetworkDriver driver, string subject, bool create)
        {
            var transport = new LeaseTransport(_leases!, subject);
            _leaseTransports.Add(transport);
            var lease = new AuthorityLeaseClient(transport, subject, Link.Clock);
            _leaseClients.Add(lease);
            driver.Migration!.PollCoordination = () => lease.Poll(driver.Migration.LeaseSession, create, driver.State?.AuthorityEpoch ?? 1, driver.Migration.NeedsLeaseObservation);
            driver.Migration.AuthorityAvailable = () => lease.Available(driver.State!.AuthorityEpoch);
            driver.Migration.RetirementConfirmedAt = checkpoint => lease.Expired(checkpoint.Lobby.State.AuthorityEpoch, checkpoint.Lobby.Subjects[checkpoint.Lobby.State.CurrentHostId]) ? Link.Clock.GetTimestamp() : null;
            driver.Migration.AcquireAuthority = checkpoint => lease.Acquire(checkpoint.Lobby.State.AuthorityEpoch);
            driver.Migration.ConfirmSuccessor = (checkpoint, candidate) => lease.Confirms(checkpoint.Lobby.State.AuthorityEpoch + 1, checkpoint.Lobby.Subjects[candidate]);
            driver.Migration.HostProgressAt = () => lease.HostProgressAt;
            driver.Migration.ReleaseAuthority = () => lease.Release(driver.State!.AuthorityEpoch);
        }

        private long? RetiredAt(MigrationCheckpoint checkpoint)
        {
            string subject = checkpoint.Lobby.Subjects[checkpoint.Lobby.State.CurrentHostId];
            return subject == Link.HostId.Value
                ? !HostServiceLive || !Link.Host.IsListening ? _hostRetiredAt : null
                : !ClientServiceLive || !Link.Client.IsListening ? _clientRetiredAt : null;
        }

    }

    private sealed class Pair : IDisposable
    {
        internal Pair(bool accept = true)
        {
            Lobby = new("lobby", "Test", HostId, 17, LobbyAccess.Public, 2, 8, OnlineLobby.CurrentProtocol, true, null) { MemberIds = new[] { HostId, ClientId } };
            HostWire = new EosP2pWire(HostId);
            ClientWire = new EosP2pWire(ClientId);
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
        internal EosP2pWire HostWire { get; }
        internal EosP2pWire ClientWire { get; }
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

}
