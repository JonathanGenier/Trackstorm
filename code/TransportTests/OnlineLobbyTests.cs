using System.Text.Json;
using Epic.OnlineServices.Lobby;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Online;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Transport.Tests;

/// <summary>Provider-independent online coordination and real LobbyNetworkDriver authority admission checks.</summary>
[TestFixture]
internal sealed partial class OnlineLobbyTests
{
    /// <summary>Native EOS status mapping grants promotion no membership or retirement authority.</summary>
    /// <param name="status">Native EOS status.</param>
    /// <param name="local">Whether the status targets the local member.</param>
    /// <param name="kind">Expected local update authority.</param>
    /// <param name="departure">Whether the status can produce retirement evidence.</param>
    [TestCase(LobbyMemberStatus.Joined, false, OnlineLobbyUpdateKind.Joined, false)]
    [TestCase(LobbyMemberStatus.Promoted, false, OnlineLobbyUpdateKind.Ownership, false)]
    [TestCase(LobbyMemberStatus.Left, false, OnlineLobbyUpdateKind.Departed, true)]
    [TestCase(LobbyMemberStatus.Kicked, false, OnlineLobbyUpdateKind.Departed, true)]
    [TestCase(LobbyMemberStatus.Disconnected, false, OnlineLobbyUpdateKind.Departed, true)]
    [TestCase(LobbyMemberStatus.Left, true, OnlineLobbyUpdateKind.Closure, true)]
    [TestCase(LobbyMemberStatus.Kicked, true, OnlineLobbyUpdateKind.Closure, true)]
    [TestCase(LobbyMemberStatus.Disconnected, true, OnlineLobbyUpdateKind.Closure, true)]
    [TestCase(LobbyMemberStatus.Closed, false, OnlineLobbyUpdateKind.Closure, false)]
    public void ClassifiesNativeMemberStatus(LobbyMemberStatus status, bool local, OnlineLobbyUpdateKind kind, bool departure)
    {
        Assert.That(EosLobbyWatch.Classify(status, local), Is.EqualTo(kind));
        Assert.That(EosLobbyWatch.IsDeparture(status), Is.EqualTo(departure));
    }

    /// <summary>Lobby metadata refreshes cannot retire an admitted member without a member-status event.</summary>
    /// <param name="access">Admission policy; post-admission membership behavior must be identical.</param>
    [TestCase(LobbyAccess.Public)]
    [TestCase(LobbyAccess.Locked)]
    public void MetadataRefreshCannotTearDownAdmittedMember(LobbyAccess access)
    {
        var service = new Service();
        using var host = service.Coordinator(1);
        using var client = service.Coordinator(2);
        host.Create("Stable", access, "test-code");
        client.Refresh();
        client.Join(host.Active!.Id, access == LobbyAccess.Locked ? "test-code" : null);
        using var gateway = new Gateway();
        var binding = host.AttachTransport(gateway, 0, "Host");
        gateway.ConnectPeer(20);
        Assert.That(binding.AuthorizePeer(20, User(2), access == LobbyAccess.Locked ? "test-code" : null), Is.True);
        gateway.ReceiveJoin(20, "Client");
        binding.Driver.Pump(0);
        Assert.That(binding.Driver.State!.Players.Single(player => player.Id == 2).Connected, Is.True);

        OnlineLobby stable = service.Lobbies[host.Active.Id];
        service.Lobbies[stable.Id] = stable with { Members = 1, MemberIds = new[] { User(1) } };
        service.Notify(stable.Id, new(OnlineLobbyUpdateKind.Metadata));

        Assert.That(gateway.Connections[20], Is.EqualTo(TransportConnectionState.Connected));
        Assert.That(binding.Driver.State.Players.Single(player => player.Id == 2).Connected, Is.True);
        Assert.That(host.Active!.MemberIds, Does.Contain(User(2)));
        host.Rename("Still stable");
        Assert.That(gateway.Connections[20], Is.EqualTo(TransportConnectionState.Connected));
        Assert.That(host.Active!.MemberIds, Does.Contain(User(2)));
        service.NotifyIncompleteMembership(stable.Id);
        Assert.That(gateway.Connections[20], Is.EqualTo(TransportConnectionState.Connected));
        Assert.That(binding.Driver.State.Players.Single(player => player.Id == 2).Connected, Is.True);
    }

    /// <summary>EOS ownership promotion can refresh owner metadata without changing the admitted gameplay roster.</summary>
    /// <param name="access">Admission policy; promotion behavior is identical after admission.</param>
    [TestCase(LobbyAccess.Public)]
    [TestCase(LobbyAccess.Locked)]
    public void PromotionRefreshCannotTearDownAdmittedMember(LobbyAccess access)
    {
        var service = new Service();
        using var host = service.Coordinator(1);
        using var client = service.Coordinator(2);
        host.Create("Promotion", access, "test-code");
        client.Refresh();
        client.Join(host.Active!.Id, access == LobbyAccess.Locked ? "test-code" : null);
        using var gateway = new Gateway();
        var binding = host.AttachTransport(gateway, 0, "Host");
        gateway.ConnectPeer(20);
        Assert.That(binding.AuthorizePeer(20, User(2), access == LobbyAccess.Locked ? "test-code" : null), Is.True);
        gateway.ReceiveJoin(20, "Client");
        binding.Driver.Pump(0);

        OnlineLobby promoted = service.Lobbies[host.Active.Id] with { Owner = User(2) };
        service.Lobbies[promoted.Id] = promoted;
        service.Notify(promoted.Id, new(OnlineLobbyUpdateKind.Ownership));
        Assert.That(host.Active!.Owner, Is.EqualTo(User(2)));
        Assert.That(host.Active.MemberIds, Does.Contain(User(2)));
        Assert.That(host.Active.HostIdentity, Is.EqualTo(User(1)));
        Assert.That(binding.Driver.State!.CurrentHostId, Is.EqualTo(1));
        Assert.That(binding.Driver.State.AuthorityEpoch, Is.EqualTo(1));
        Assert.That(gateway.Connections[20], Is.EqualTo(TransportConnectionState.Connected));

        service.Lobbies[promoted.Id] = promoted with { Members = 1, MemberIds = new[] { User(1) } };
        service.Notify(promoted.Id, new(OnlineLobbyUpdateKind.Ownership));

        Assert.That(host.Active.MemberIds, Does.Contain(User(2)));
        Assert.That(gateway.Connections[20], Is.EqualTo(TransportConnectionState.Connected));
        Assert.That(binding.Driver.State!.Players.Single(player => player.Id == 2).Connected, Is.True);
        Assert.That(host.Diagnostics, Does.Contain("recovering membership: False"));
        Assert.That(host.Diagnostics, Does.Contain("retired callbacks: 0"));
    }

    /// <summary>An actual member departure removes only its target and tears down that transport/Core connection.</summary>
    /// <param name="status">Native departure operation represented by the targeted event.</param>
    /// <param name="access">Admission policy; post-admission departure behavior must be identical.</param>
    [TestCase(LobbyMemberStatus.Left, LobbyAccess.Public)]
    [TestCase(LobbyMemberStatus.Kicked, LobbyAccess.Public)]
    [TestCase(LobbyMemberStatus.Disconnected, LobbyAccess.Public)]
    [TestCase(LobbyMemberStatus.Left, LobbyAccess.Locked)]
    [TestCase(LobbyMemberStatus.Kicked, LobbyAccess.Locked)]
    [TestCase(LobbyMemberStatus.Disconnected, LobbyAccess.Locked)]
    public void ActualMembershipDepartureDisconnectsAdmittedMember(LobbyMemberStatus status, LobbyAccess access)
    {
        var service = new Service();
        using var host = service.Coordinator(1);
        using var client = service.Coordinator(2);
        using var departing = service.Coordinator(3);
        host.Create("Departure", access, "test-code");
        client.Refresh();
        client.Join(host.Active!.Id, access == LobbyAccess.Locked ? "test-code" : null);
        departing.Refresh();
        departing.Join(host.Active.Id, access == LobbyAccess.Locked ? "test-code" : null);
        using var gateway = new Gateway();
        var binding = host.AttachTransport(gateway, 0, "Host");
        gateway.ConnectPeer(20);
        Assert.That(binding.AuthorizePeer(20, User(2), access == LobbyAccess.Locked ? "test-code" : null), Is.True);
        gateway.ReceiveJoin(20, "Client");
        gateway.ConnectPeer(30);
        Assert.That(binding.AuthorizePeer(30, User(3), access == LobbyAccess.Locked ? "test-code" : null), Is.True);
        gateway.ReceiveJoin(30, "Departing");
        binding.Driver.Pump(0);

        OnlineLobby lobby = service.Lobbies[host.Active.Id];
        service.Lobbies[lobby.Id] = lobby with { Members = 2, MemberIds = new[] { User(1), User(2) } };
        OnlineLobby partial = lobby with { Members = 1, MemberIds = new[] { User(1) } };
        service.Notify(lobby.Id, partial, new(EosLobbyWatch.Classify(status, false), User(3)));
        service.Retire(lobby.Id, User(3));
        binding.Driver.Pump(0);

        Assert.That(host.Active!.MemberIds, Does.Contain(User(2)));
        Assert.That(host.Active.MemberIds, Does.Not.Contain(User(3)));
        Assert.That(host.Active.Members, Is.EqualTo(2));
        Assert.That(gateway.Connections[20], Is.EqualTo(TransportConnectionState.Connected));
        Assert.That(gateway.Connections[30], Is.EqualTo(TransportConnectionState.Disconnected));
        Assert.That(binding.Driver.State!.Players.Single(player => player.Id == 2).Connected, Is.True);
        Assert.That(binding.Driver.State.Players.Any(player => player.Id == 3), Is.False);
        Assert.That(binding.Driver.Authority!.FindPlayer(User(3).Value), Is.Zero);
        Assert.That(host.Diagnostics, Does.Contain("retired callbacks: 1"));
    }

    /// <summary>A member join cannot remove an unrelated admitted member omitted from the refreshed snapshot.</summary>
    /// <param name="access">Admission policy; post-admission membership behavior must be identical.</param>
    /// <param name="omitsTarget">Whether the partial snapshot omits the joined target instead of an unrelated member.</param>
    [TestCase(LobbyAccess.Public, false)]
    [TestCase(LobbyAccess.Public, true)]
    [TestCase(LobbyAccess.Locked, false)]
    [TestCase(LobbyAccess.Locked, true)]
    public void MembershipJoinRefreshCannotTearDownUnrelatedAdmittedMember(LobbyAccess access, bool omitsTarget)
    {
        var service = new Service();
        using var host = service.Coordinator(1);
        using var client = service.Coordinator(2);
        host.Create("Partial join", access, "test-code");
        client.Refresh();
        client.Join(host.Active!.Id, access == LobbyAccess.Locked ? "test-code" : null);
        using var gateway = new Gateway();
        var binding = host.AttachTransport(gateway, 0, "Host");
        gateway.ConnectPeer(20);
        Assert.That(binding.AuthorizePeer(20, User(2), access == LobbyAccess.Locked ? "test-code" : null), Is.True);
        gateway.ReceiveJoin(20, "Client");
        binding.Driver.Pump(0);

        OnlineLobby lobby = service.Lobbies[host.Active.Id];
        service.Lobbies[lobby.Id] = lobby with { Members = 3, MemberIds = new[] { User(1), User(2), User(3) } };
        OnlineLobby partial = lobby with
        {
            Members = 2,
            MemberIds = omitsTarget ? new[] { User(1), User(2) } : new[] { User(1), User(3) },
        };
        service.Notify(lobby.Id, partial, new(OnlineLobbyUpdateKind.Joined, User(3)));

        Assert.That(host.Active!.MemberIds, Does.Contain(User(2)));
        Assert.That(host.Active.MemberIds, Does.Contain(User(3)));
        Assert.That(host.Active.Members, Is.EqualTo(3));
        Assert.That(gateway.Connections[20], Is.EqualTo(TransportConnectionState.Connected));
        Assert.That(binding.Driver.State!.Players.Single(player => player.Id == 2).Connected, Is.True);
    }

    /// <summary>Repeated proof-related metadata refreshes leave a healthy Locked session intact beyond reconnect grace.</summary>
    [Test]
    public void LockedSessionRemainsStableAcrossProofRefreshesPastThirtySeconds()
    {
        var service = new Service { NotifyMetadataOnProof = true };
        var clock = new Clock();
        using var host = new OnlineLobbyCoordinator(new Provider(service, User(1)), User(1), clock);
        using var client = new OnlineLobbyCoordinator(new Provider(service, User(2)), User(2), clock);
        host.Create("Locked soak", LobbyAccess.Locked, "test-code");
        client.Refresh();
        client.Join(host.Active!.Id, "test-code");
        using var hostGateway = new Gateway();
        var hostBinding = host.AttachTransport(hostGateway, 0, "Host");
        hostGateway.ConnectPeer(20);
        Assert.That(hostBinding.AuthorizePeer(20, User(2), "test-code"), Is.True);
        hostGateway.ReceiveJoin(20, "Client");
        hostBinding.Driver.Pump(0);
        using var clientGateway = new Gateway();
        clientGateway.ConnectPeer(10);
        var clientBinding = client.AttachTransport(clientGateway, 10, "Client");
        clientGateway.ReceiveState(10, hostBinding.Driver.State!, 2);
        clientBinding.Driver.Pump(0);
        string lobbyId = host.Active.Id;
        ulong session = hostBinding.Driver.State!.Session;
        ulong epoch = hostBinding.Driver.State.AuthorityEpoch;

        for (int second = 0; second < 40; second++)
        {
            clock.Advance(1);
            host.Tick();
            client.Tick();
            hostBinding.Driver.Pump(1.0 / 60);
            clientBinding.Driver.Pump(1.0 / 60);
            Assert.That(host.Active?.Id, Is.EqualTo(lobbyId));
            Assert.That(client.Active?.Id, Is.EqualTo(lobbyId));
            Assert.That(clientBinding.Driver.State?.Session, Is.EqualTo(session));
            Assert.That(clientBinding.Driver.LocalPlayerId, Is.EqualTo(2));
            Assert.That(clientBinding.Driver.State?.AuthorityEpoch, Is.EqualTo(epoch));
            Assert.That(clientBinding.Driver.Reconnecting, Is.False);
            Assert.That(hostBinding.Driver.Migration?.Frozen, Is.Not.True);
            Assert.That(host.CoordinationAvailable, Is.True);
            Assert.That(client.CoordinationAvailable, Is.True);
            Assert.That(hostGateway.Connections[20], Is.EqualTo(TransportConnectionState.Connected));
            Assert.That(clientGateway.Connections[10], Is.EqualTo(TransportConnectionState.Connected));
        }

        Assert.That(service.ProofRequests, Is.GreaterThanOrEqualTo(16));
        Assert.That(hostBinding.Driver.State.Players.Single(player => player.Id == 2).Connected, Is.True);
        Assert.That(client.Diagnostics, Does.Contain("metadata/ownership/joined/departed callbacks:"));
        Assert.That(client.Diagnostics, Does.Contain("recovering membership: False"));
        Assert.That(client.Diagnostics, Does.Not.Contain("test-code"));
        Assert.That(client.Diagnostics, Does.Not.Contain(User(1).Value));
        Assert.That(client.Diagnostics, Does.Not.Contain(User(2).Value));
    }

    /// <summary>Display names never select or revive an earlier EOS or Trackstorm session lifetime.</summary>
    [Test]
    public void ReusedDisplayNameCreatesFreshLobbyAndSession()
    {
        string path = Path.Combine(Path.GetTempPath(), "trackstorm-reused-name-" + Guid.NewGuid().ToString("N") + ".json");
        var store = new ResumeLocatorStore(path);
        try
        {
            var service = new Service();
            using var host = new OnlineLobbyCoordinator(new Provider(service, User(1)), User(1), resumeStore: store);
            host.Create("TESER", LobbyAccess.Locked, "first-code");
            string firstLobby = host.Active!.Id;
            ulong firstSession = host.Active.Session;
            host.Leave();
            host.Create("TESER", LobbyAccess.Locked, "second-code");
            Assert.That(host.Active!.Id, Is.Not.EqualTo(firstLobby));
            Assert.That(host.Active.Session, Is.Not.EqualTo(firstSession));
            Assert.That(host.Active.AuthorityEpoch, Is.EqualTo(1));
            Assert.That(host.Active.HostIdentity, Is.EqualTo(User(1)));
            Assert.That(host.Active.Credential!.Verify("first-code"), Is.False);
            Assert.That(host.Active.Credential.Verify("second-code"), Is.True);
            Assert.That(store.Load(User(1).Value), Is.Null);
        }
        finally
        {
            store.Clear();
        }
    }

    /// <summary>A delayed service response cannot revive authority after its request-time lease expired.</summary>
    [Test]
    public void ExpiredAuthorityCannotBeRenewedByLateServiceCallback()
    {
        var service = new Service();
        var clock = new Clock();
        using var host = new OnlineLobbyCoordinator(new Provider(service, User(1)), User(1), clock);
        host.Create("Lease", LobbyAccess.Public, null);
        using var gateway = new Gateway();
        host.AttachTransport(gateway, 0, "Host");
        Assert.That(host.CoordinationAvailable, Is.True);
        service.DelayProof = true;
        clock.Advance(5);
        host.Tick();
        clock.Advance(6);
        service.LastProof!(true);
        Assert.That(host.CoordinationAvailable, Is.False);
        service.DelayProof = false;
        host.Tick();
        Assert.That(host.CoordinationAvailable, Is.False, "Expired authority must rejoin; a later success cannot revive it.");
    }

    /// <summary>Only explicit service retirement, a full lease wait and the exact survivor cohort permit recovery.</summary>
    /// <param name="status">Native established-host departure that supplies retirement evidence.</param>
    [TestCase(LobbyMemberStatus.Left)]
    [TestCase(LobbyMemberStatus.Kicked)]
    [TestCase(LobbyMemberStatus.Disconnected)]
    public void RetirementRequiresServiceEventFreshMembershipAndMatchingCohort(LobbyMemberStatus status)
    {
        var service = new Service();
        var clock = new Clock();
        using var host = service.Coordinator(1);
        using var client = new OnlineLobbyCoordinator(new Provider(service, User(2)), User(2), clock);
        host.Create("Retirement", LobbyAccess.Public, null);
        client.Refresh();
        client.Join(host.Active!.Id);
        using var gateway = new Gateway();
        var binding = host.AttachTransport(gateway, 0, "Host");
        gateway.ConnectPeer(20);
        Assert.That(binding.AuthorizePeer(20, User(2), null), Is.True);
        gateway.ReceiveJoin(20, "Client");
        binding.Driver.Pump(0);
        var checkpoint = new MigrationCheckpoint(1, binding.Driver.Authority!.Capture(User(1).Value), null, null);
        string id = host.Active.Id;
        service.Lobbies[id] = client.Active! with { Owner = User(2), MemberIds = new[] { User(2) }, Members = 1 };
        service.Notify(id, new(OnlineLobbyUpdateKind.Ownership));
        clock.Advance(11);
        client.Tick();
        Assert.That(client.Active!.MemberIds, Does.Contain(User(1)), "Promotion snapshots cannot remove the established host.");
        Assert.That(client.Diagnostics, Does.Contain("retired callbacks: 0"));
        Assert.That(client.HostRetired(checkpoint), Is.False, "Ownership and membership snapshots are not a retirement event.");
        Assert.That(client.HostRetiredAt(checkpoint), Is.Null);
        long retirementAt = clock.GetTimestamp();
        service.Notify(id, new(EosLobbyWatch.Classify(status, false), User(1)));
        service.Retire(id, User(1));
        clock.Advance(9);
        client.Tick();
        Assert.That(client.HostRetired(checkpoint), Is.False);
        clock.Advance(2);
        client.Tick();
        Assert.That(client.HostRetired(checkpoint), Is.True);
        Assert.That(client.HostRetiredAt(checkpoint), Is.EqualTo(retirementAt), "The safety wait must not replace the original monotonic retirement boundary.");
        clock.Advance(11);
        Assert.That(client.HostRetired(checkpoint), Is.False, "The survivor must itself have current service membership.");
        client.Tick();
        service.Lobbies[id] = client.Active! with { MemberIds = new[] { User(2), User(3) }, Members = 2 };
        service.Notify(id, new(OnlineLobbyUpdateKind.Joined, User(3)));
        service.Retire(id, User(1));
        clock.Advance(11);
        client.Tick();
        Assert.That(client.HostRetired(checkpoint), Is.False, "An older two-player checkpoint cannot exclude a current survivor.");
    }

    /// <summary>Former-host membership recovery routes retained Public/Locked players through Core resume admission.</summary>
    /// <param name="access">Original admission policy.</param>
    /// <param name="restart">Whether to recover the persisted locator in a new coordinator.</param>
    [TestCase(LobbyAccess.Public, false)]
    [TestCase(LobbyAccess.Public, true)]
    [TestCase(LobbyAccess.Locked, false)]
    [TestCase(LobbyAccess.Locked, true)]
    public void FormerHostResumesThroughProviderAndCoreWithoutPassword(LobbyAccess access, bool restart)
    {
        var store = new ResumeLocatorStore(Path.Combine(Path.GetTempPath(), "trackstorm-former-host-" + Guid.NewGuid().ToString("N") + ".json"));
        try
        {
            var service = new Service();
            var clock = new Clock();
            using var host = new OnlineLobbyCoordinator(new Provider(service, User(1)), User(1), clock, store);
            using var client = service.Coordinator(2);
            host.Create("Return", access, "test-code");
            client.Refresh();
            client.Join(host.Active!.Id, "test-code");
            using var oldGateway = new Gateway();
            var oldBinding = host.AttachTransport(oldGateway, 0, "Host");
            oldGateway.ConnectPeer(20);
            Assert.That(oldBinding.AuthorizePeer(20, User(2), "test-code"), Is.True);
            oldGateway.ReceiveJoin(20, "Client");
            oldBinding.Driver.Pump(0);
            oldBinding.Driver.Authority!.SetReady(0, true);
            oldBinding.Driver.Authority.SetReady(20, true);
            Assert.That(oldBinding.Driver.Authority.Start(0, [20]), Is.True);
            var arena = new HostVehicleSession(oldBinding.Driver.State!.Match);
            arena.JoinPlayer(20, 2);
            var arenaCheckpoint = new ResumeCheckpoint(new ItemPublication(1, arena.Snapshot(), arena.Items.Slots, arena.Items.Missiles, []), arena.World.State.Match!, null);
            oldBinding.Driver.Migration = new Trackstorm.Client.Networking.SessionMigration(oldBinding.Driver, oldGateway, User(1).Value, _ => User(2).Value, (_, _) => 0);
            oldBinding.Driver.Migration.CaptureArena = () => (arenaCheckpoint, arena.CaptureAuthority());
            oldBinding.Driver.Pump(0);
            var checkpoint = new MigrationCheckpoint(
                1,
                oldBinding.Driver.Authority!.Capture(User(1).Value),
                arenaCheckpoint,
                arena.CaptureAuthority());
            using var replacementGateway = new Gateway();
            replacementGateway.ConnectPeer(10);
            var replacement = client.AttachTransport(replacementGateway, 10, "Client");
            replacementGateway.ReceiveState(10, checkpoint.Lobby.State, 2);
            replacement.Driver.Pump(0);
            replacement.Driver.InstallMigration(checkpoint, 2, 0);
            client.MigrationCompleted(User(2).Value);
            Assert.That(client.Active!.AuthorityEpoch, Is.EqualTo(2), "The Core commit establishes routing before provider publication.");
            string id = host.Active.Id;
            host.Leave();
            Assert.That(client.Active.HostIdentity, Is.EqualTo(User(2)), "The delayed epoch-one membership event cannot replace the committed host.");
            Assert.That(host.CanResumeRetained, Is.True);
            using var restarted = restart ? new OnlineLobbyCoordinator(new Provider(service, User(1)), User(1), clock, store) : null;
            var returning = restarted ?? host;
            if (restart)
            {
                returning.Tick();
            }
            else
            {
                returning.ResumeRetained();
            }

            Assert.That(returning.Active, Is.Null, "A returning old host must not reconnect to its cached self route.");
            Assert.That(returning.Status, Does.Contain("replacement host"));
            service.Lobbies[id] = service.Lobbies[id] with { Owner = User(2), GameplayHost = User(2), AuthorityEpoch = 2, Open = false };
            service.Notify(id, new(OnlineLobbyUpdateKind.Metadata));
            clock.Advance(3);
            returning.Tick();
            Assert.That(returning.Active!.HostIdentity, Is.EqualTo(User(2)));
            Assert.That(returning.Active.AuthorityEpoch, Is.EqualTo(2));
            Assert.That(returning.StartsGameplayAuthority, Is.False);
            using var returnGateway = new Gateway();
            returnGateway.ConnectPeer(30);
            var returned = returning.AttachTransport(returnGateway, 30, "Former host");
            returned.Driver.Pump(0);
            ConfirmReservation(returning, returnGateway, returned.Driver, 30, checkpoint.Lobby.State.Session, 1, 1, 2);
            var resume = LobbyCodec.DecodeCommand(returnGateway.Sent.Last().Payload.Span);
            Assert.That(resume.Command, Is.EqualTo(LobbyCommand.Resume));
            Assert.That(resume.GameVersion, Is.EqualTo(GameVersion.Current.ToString()));
            Assert.That(resume.AuthorityEpoch, Is.EqualTo(2));
            var beforeResume = replacement.Driver.State;
            replacementGateway.ConnectPeer(38);
            Assert.That(replacement.AuthorizePeer(38, User(1), null), Is.True);
            string incompatibleVersion = new GameVersion(GameVersion.Current.Revision == 0 ? 1 : 0).ToString();
            replacementGateway.ReceiveResume(38, checkpoint.Lobby.State.Session, 1, 1, 2, incompatibleVersion);
            replacement.Driver.Pump(0);
            Assert.That(replacement.Driver.State, Is.SameAs(beforeResume), "A matching epoch cannot bypass the version gate after migration.");
            Assert.That(LobbyCodec.IsVersionMismatch(replacementGateway.Sent.Last().Payload.Span), Is.True);
            replacementGateway.Disconnect(38);
            replacementGateway.ConnectPeer(39);
            Assert.That(replacement.AuthorizePeer(39, User(1), null), Is.True);
            replacementGateway.ReceiveResume(39, checkpoint.Lobby.State.Session, 1, 1, 1);
            replacement.Driver.Pump(0);
            Assert.That(replacement.Driver.State, Is.SameAs(beforeResume), "A matching version cannot bypass the authority fence.");
            Assert.That(LobbyCodec.IsRejection(replacementGateway.Sent.Last().Payload.Span), Is.True);
            replacementGateway.Disconnect(39);
            replacementGateway.ConnectPeer(40);
            Assert.That(replacement.AuthorizePeer(40, User(1), null), Is.True);
            replacementGateway.ReceiveResume(40, checkpoint.Lobby.State.Session, 1, 1, 2);
            replacement.Driver.Pump(0);
            returnGateway.ReceiveState(30, replacement.Driver.State!, 1);
            returned.Driver.Pump(0);
            Assert.That(returned.Driver.Authority, Is.Null);
            Assert.That(returned.Driver.LocalPlayerId, Is.EqualTo(1));
            Assert.That(returned.Driver.Generation, Is.EqualTo(2));
            Assert.That(returned.Driver.Reconnecting, Is.True, "Arena resume remains frozen until the complete gameplay checkpoint arrives.");
            Assert.That(returned.Driver.NeedsArenaCheckpoint, Is.True);
            Assert.That(returned.Driver.State!.Session, Is.EqualTo(checkpoint.Lobby.State.Session));
            Assert.That(returned.Driver.State.CurrentHostId, Is.EqualTo(2));
            Assert.That(returned.Driver.State.AuthorityEpoch, Is.EqualTo(2));
            Assert.That(returned.Driver.State.Players.Select(player => player.Id), Is.EquivalentTo(new ulong[] { 1, 2 }));
            Assert.That(replacement.Driver.State!.Phase, Is.EqualTo(SessionPhase.Arena));
            returned.Driver.Request(LobbyCommand.Start);
            int rejected = replacement.Driver.RejectedPackets;
            replacementGateway.Receive(40, returnGateway.Sent.Last().Payload.ToArray());
            replacement.Driver.Pump(0);
            Assert.That(replacement.Driver.State.Phase, Is.EqualTo(SessionPhase.Arena));
            Assert.That(replacement.Driver.RejectedPackets, Is.GreaterThan(rejected));
            if (access == LobbyAccess.Locked)
            {
                service.Lobbies[id] = service.Lobbies[id] with { MemberIds = new[] { User(1), User(2), User(3) }, Members = 3 };
                service.Notify(id, new(OnlineLobbyUpdateKind.Joined, User(3)));
                replacementGateway.ConnectPeer(50);
                Assert.That(replacement.AuthorizePeer(50, User(3), null), Is.True, "Authenticated control queries need no access code.");
                replacementGateway.ReceiveJoin(50, "Unauthorized");
                replacement.Driver.Pump(0);
                Assert.That(replacement.Driver.Authority!.PlayerId(50), Is.Zero, "EOS membership cannot bypass fresh Locked admission.");
            }
        }
        finally
        {
            store.Clear();
        }
    }

    /// <summary>Provider ownership notifications cannot bootstrap gameplay authority on a joining player.</summary>
    [Test]
    public void ProviderPromotionDoesNotGrantGameplayAuthority()
    {
        var service = new Service();
        using var host = service.Coordinator(1);
        using var client = service.Coordinator(2);
        host.Create("Migration", LobbyAccess.Public, null);
        client.Refresh();
        client.Join(host.Active!.Id, null);
        var before = client.Active!;
        service.Lobbies[before.Id] = before with { Owner = User(2) };
        service.Notify(before.Id, new(OnlineLobbyUpdateKind.Ownership));
        Assert.That(client.IsHost, Is.True);
        Assert.That(client.StartsGameplayAuthority, Is.False);
        Assert.That(client.Active!.Session, Is.EqualTo(before.Session));
        using var gateway = new Gateway();
        var binding = client.AttachTransport(gateway, 99, "Promoted member");
        Assert.That(binding.Driver.Authority, Is.Null);
    }

    /// <summary>Delayed provider snapshots retain safe membership data without regressing established Trackstorm routing.</summary>
    [Test]
    public void ProviderUpdatesCannotRegressMigratedAuthorityRouting()
    {
        string path = Path.Combine(Path.GetTempPath(), "trackstorm-migrated-resume-" + Guid.NewGuid().ToString("N") + ".json");
        var store = new ResumeLocatorStore(path);
        try
        {
            var service = new Service();
            using var host = service.Coordinator(1);
            using var client = new OnlineLobbyCoordinator(new Provider(service, User(2)), User(2), resumeStore: store);
            host.Create("Migration", LobbyAccess.Public, null);
            client.Refresh();
            client.Join(host.Active!.Id, null);
            OnlineLobby epochOne = client.Active!;

            var epochTwo = epochOne with { Owner = User(2), GameplayHost = User(2), AuthorityEpoch = 2 };
            service.Lobbies[epochOne.Id] = epochTwo;
            service.Notify(epochOne.Id, new(OnlineLobbyUpdateKind.Ownership));
            Assert.That(client.Active!.AuthorityEpoch, Is.EqualTo(2));
            Assert.That(client.Active.HostIdentity, Is.EqualTo(User(2)));

            var delayedEpochOne = epochOne with
            {
                Name = "Current members",
                Owner = User(3),
                Members = 3,
                MemberIds = new[] { User(1), User(2), User(3) },
                GameplayHost = User(1),
                AuthorityEpoch = 1,
            };
            service.Lobbies[epochOne.Id] = delayedEpochOne;
            service.Notify(epochOne.Id, new(OnlineLobbyUpdateKind.Joined, User(3)));
            Assert.That(client.Active!.Session, Is.EqualTo(epochOne.Session));
            Assert.That(client.Active.AuthorityEpoch, Is.EqualTo(2));
            Assert.That(client.Active.HostIdentity, Is.EqualTo(User(2)));
            Assert.That(client.Active.Owner, Is.EqualTo(User(3)));
            Assert.That(client.Active.MemberIds, Is.EqualTo(delayedEpochOne.MemberIds));

            var conflictingEpochTwo = delayedEpochOne with
            {
                Name = "Updated membership",
                Owner = User(4),
                Members = 4,
                MemberIds = new[] { User(1), User(2), User(3), User(4) },
                GameplayHost = User(3),
                AuthorityEpoch = 2,
            };
            service.Lobbies[epochOne.Id] = conflictingEpochTwo;
            service.Notify(epochOne.Id, new(OnlineLobbyUpdateKind.Joined, User(4)));
            Assert.That(client.Active.AuthorityEpoch, Is.EqualTo(2));
            Assert.That(client.Active.HostIdentity, Is.EqualTo(User(2)));
            Assert.That(client.Active.Owner, Is.EqualTo(User(4)));
            Assert.That(client.Active.Name, Is.EqualTo("Updated membership"));
            Assert.That(client.Active.MemberIds, Is.EqualTo(conflictingEpochTwo.MemberIds));

            using var gateway = new Gateway();
            gateway.ConnectPeer(10);
            var binding = client.AttachTransport(gateway, 10, "Client");
            var migratedState = new LobbySnapshot(
                epochOne.Session,
                2,
                epochOne.Session,
                SessionPhase.Lobby,
                new[] { new SessionPlayer(1, "Old host", false), new SessionPlayer(2, "Client", false) },
                currentHostId: 2,
                authorityEpoch: 2);
            gateway.ReceiveState(10, migratedState, 2);
            binding.Driver.Pump(0);
            client.Tick();
            Assert.That(store.Load(User(2).Value), Is.Null, "Lobby state must not persist a resume locator.");
            service.Lobbies[epochOne.Id] = delayedEpochOne;
            using var restarted = new OnlineLobbyCoordinator(new Provider(service, User(2)), User(2), resumeStore: store);
            restarted.Tick();
            Assert.That(restarted.Active, Is.Null);
            Assert.That(restarted.SavedResume, Is.Null);
        }
        finally
        {
            store.Clear();
        }
    }

    /// <summary>Names are bounded, canonical, non-empty on admission, and safe for plain presentation.</summary>
    [Test]
    public void NamesValidateAtCreateAndRename()
    {
        Assert.That(LobbyName.Sanitize("  Jo's\n  Game<>\u202e  "), Is.EqualTo("Jo's Game"));
        Assert.That(LobbyName.Sanitize(new string('x', 80)).Length, Is.EqualTo(48));
        Assert.That(LobbyName.Sanitize(string.Concat(Enumerable.Repeat("𐐀", 60))).EnumerateRunes().Count(), Is.EqualTo(48));
        var service = new Service();
        using var host = service.Coordinator(1);
        host.Create("<>\u202e", LobbyAccess.Public, null);
        Assert.That(host.Active, Is.Null);
        host.Create("  Test<>  ", LobbyAccess.Public, null);
        Assert.That(host.Active!.Name, Is.EqualTo("Test"));
        host.Rename("<>");
        Assert.That(host.Active.Name, Is.EqualTo("Test"));
        host.Rename("  New\n Name<>  ");
        Assert.That(host.Active.Name, Is.EqualTo("New Name"));
    }

    /// <summary>One keyed compatible list supports deterministic ordering and renamed-name search.</summary>
    [Test]
    public void BrowserFiltersOrdersAndReplacesRenames()
    {
        var browser = new LobbyBrowser();
        var a = Lobby("b", "Arena");
        var b = Lobby("a", "arena") with { Access = LobbyAccess.Locked, Credential = LobbyCredential.Create("test-code") };
        browser.Replace(new[] { a, b, Lobby("bad", "Bad") with { Protocol = "old" }, Lobby("full", "Full") with { Members = 8 } });
        Assert.That(browser.Rows.Select(row => row.Id), Is.EqualTo(new[] { "a", "b", "full" }));
        Assert.That(browser.Rows[0].Access, Is.EqualTo(LobbyAccess.Locked));
        Assert.That(browser.Rows[^1].Joinable, Is.False);
        browser.Search = "ARENA";
        Assert.That(browser.Rows.Count, Is.EqualTo(2));
        browser.Update(a with { Name = "Renamed" });
        Assert.That(browser.Rows.Count, Is.EqualTo(1));
        browser.Search = "renAM";
        Assert.That(browser.Rows.Single().Id, Is.EqualTo("b"));
        browser.Search = string.Empty;
        Assert.That(browser.Rows.Count, Is.EqualTo(3));
        browser.Search = "missing";
        Assert.That(browser.Rows, Is.Empty);
        browser.Replace(Array.Empty<OnlineLobby>());
        browser.Search = string.Empty;
        Assert.That(browser.Rows, Is.Empty);
    }

    /// <summary>Locked credentials gate EOS join and actual host roster admission; rename retains all authority.</summary>
    /// <param name="access">Access mode exercised through the shared coordination path.</param>
    [TestCase(LobbyAccess.Public)]
    [TestCase(LobbyAccess.Locked)]
    public void AdmissionAndRenamePreserveAuthority(LobbyAccess access)
    {
        var service = new Service();
        using var host = service.Coordinator(1);
        using var client = service.Coordinator(2);
        host.Create("Original", access, "secret-code");
        var original = host.Active!;
        client.Refresh();
        if (access == LobbyAccess.Locked)
        {
            client.Join(original.Id, "wrong-code");
            Assert.That(client.Active, Is.Null);
            Assert.That(service.Lobbies[original.Id].Members, Is.EqualTo(1));
        }

        client.Join(original.Id, access == LobbyAccess.Locked ? "secret-code" : null);
        Assert.That(client.Active, Is.Not.Null);
        using var gateway = new Gateway();
        var binding = host.AttachTransport(gateway, 0, "Host");
        gateway.ConnectPeer(20);
        Assert.That(binding.AuthorizePeer(20, User(2), access == LobbyAccess.Locked ? "secret-code" : null), Is.True);
        gateway.ReceiveJoin(20, "Client");
        binding.Driver.Pump(0);
        Assert.That(binding.Driver.State!.Players.Count, Is.EqualTo(2));
        Assert.That(binding.PlayerIds[User(1)], Is.EqualTo(1));
        Assert.That(binding.PlayerIds[User(2)], Is.EqualTo(2));
        Assert.That(binding.Driver.Authority!.SetReady(20, true), Is.True);
        binding.Driver.Request(LobbyCommand.Ready, true);
        var snapshot = binding.Driver.State;
        client.Rename("Forbidden");
        Assert.That(client.Status, Does.Contain("Only the host"));
        host.Rename("Renamed");
        Assert.That(host.Active!.Id, Is.EqualTo(original.Id));
        Assert.That(host.Active.Session, Is.EqualTo(original.Session));
        Assert.That(host.Active.Access, Is.EqualTo(access));
        Assert.That(host.Active.Credential?.ExportVerifier(), Is.EqualTo(original.Credential?.ExportVerifier()));
        Assert.That(binding.Driver.State, Is.SameAs(snapshot));
        Assert.That(client.Active!.Name, Is.EqualTo("Renamed"));
        client.Refresh();
        client.Browser.Search = "ORIGINAL";
        Assert.That(client.Browser.Rows, Is.Empty);
        client.Browser.Search = "RENAMED";
        Assert.That(client.Browser.Rows.Count, Is.EqualTo(1));
        Assert.That(binding.Driver.Request(LobbyCommand.Start), Is.True);
        Assert.That(binding.Driver.State!.Phase, Is.EqualTo(SessionPhase.Arena));
        host.Tick();
        Assert.That(host.Active.Open, Is.True);
        Assert.That(binding.Driver.Request(LobbyCommand.Return), Is.True);
        host.Tick();
        Assert.That(host.Active.Open, Is.True);
        Assert.That(binding.Driver.State.Players.All(player => !player.Ready), Is.True);
    }

    /// <summary>A transport join cannot bypass the host-side access check, even after EOS membership.</summary>
    [Test]
    public void WrongCredentialAndUnboundPeerNeverEnterAuthority()
    {
        var service = new Service();
        using var host = service.Coordinator(1);
        using var client = service.Coordinator(2);
        host.Create("Locked", LobbyAccess.Locked, "secret-code");
        client.Refresh();
        client.Join(host.Active!.Id, "secret-code");
        using var gateway = new Gateway();
        var binding = host.AttachTransport(gateway, 0, "Host");
        gateway.ConnectPeer(20);
        Assert.That(binding.AuthorizePeer(20, User(2), "wrong-code"), Is.False);
        gateway.ReceiveJoin(20, "Wrong");
        binding.Driver.Pump(0);
        gateway.ConnectPeer(21);
        gateway.ReceiveJoin(21, "Bypass");
        binding.Driver.Pump(0);
        Assert.That(binding.Driver.State!.Players.Count, Is.EqualTo(1));
        gateway.ConnectPeer(22);
        Assert.That(binding.AuthorizePeer(22, User(3), "secret-code"), Is.False);
        Assert.That(binding.PlayerIds.Count, Is.EqualTo(1));
        host.Leave();
        gateway.ConnectPeer(23);
        Assert.That(binding.AuthorizePeer(23, User(2), "secret-code"), Is.False);
        Assert.That(binding.PlayerIds, Is.Empty);
    }

    /// <summary>Eight EOS members and eight Core players are supported; a ninth is refused.</summary>
    [Test]
    public void CapacityIsEightIncludingHost()
    {
        var service = new Service();
        using var host = service.Coordinator(1);
        host.Create("Capacity", LobbyAccess.Public, null);
        using var gateway = new Gateway();
        var binding = host.AttachTransport(gateway, 0, "Host");
        var clients = new List<OnlineLobbyCoordinator>();
        try
        {
            for (int i = 2; i <= 9; i++)
            {
                var client = service.Coordinator(i);
                clients.Add(client);
                client.Refresh();
                client.Join(host.Active!.Id);
                if (i <= 8)
                {
                    Assert.That(client.Active, Is.Not.Null);
                    gateway.ConnectPeer((ulong)i);
                    Assert.That(binding.AuthorizePeer((ulong)i, User(i), null), Is.True);
                    gateway.ReceiveJoin((ulong)i, "Player");
                    binding.Driver.Pump(0);
                }
                else
                {
                    Assert.That(client.Active, Is.Null);
                    Assert.That(client.Status, Does.Contain("full"));
                }
            }

            Assert.That(host.Active!.Members, Is.EqualTo(8));
            Assert.That(binding.Driver.State!.Players.Count, Is.EqualTo(8));
        }
        finally
        {
            clients.ForEach(client => client.Dispose());
        }
    }

    /// <summary>Departure disconnects transport and lets the existing driver remove authoritative state.</summary>
    [Test]
    public void MemberLeaveUsesExistingAuthorityRemovalAndRejoinGetsFreshId()
    {
        var service = new Service();
        using var host = service.Coordinator(1);
        using var client = service.Coordinator(2);
        host.Create("Test", LobbyAccess.Public, null);
        client.Refresh();
        client.Join(host.Active!.Id);
        using var gateway = new Gateway();
        var binding = host.AttachTransport(gateway, 0, "Host");
        gateway.ConnectPeer(20);
        binding.AuthorizePeer(20, User(2), null);
        gateway.ReceiveJoin(20, "Client");
        binding.Driver.Pump(0);
        Assert.That(binding.Driver.Authority!.Execute(20, LobbyCommand.Leave, binding.Driver.State!.Session, binding.Driver.State.Match, binding.Driver.State.Phase, false, [20]), Is.True);
        client.Leave();
        Assert.That(binding.Driver.State!.Players.Count, Is.EqualTo(1), "Explicit gameplay Leave revokes the slot before EOS membership cleanup.");
        binding.Driver.Pump(0);
        Assert.That(binding.Driver.State.Players.Count, Is.EqualTo(1));
        client.Refresh();
        client.Join(host.Active!.Id);
        gateway.ConnectPeer(21);
        Assert.That(binding.AuthorizePeer(21, User(2), null), Is.True);
        gateway.ReceiveJoin(21, "Client");
        binding.Driver.Pump(0);
        Assert.That(binding.PlayerIds[User(2)], Is.EqualTo(3));
    }

    /// <summary>Closing and recreating expires old credentials and subscriptions, including forced late events.</summary>
    [Test]
    public void RepeatedLifecycleRejectsLateNotificationsAndExpiresCredential()
    {
        var service = new Service();
        using var host = service.Coordinator(1);
        using var client = service.Coordinator(2);
        for (int i = 0; i < 5; i++)
        {
            host.Create("Cycle", LobbyAccess.Locked, "old-code");
            string id = host.Active!.Id;
            client.Refresh();
            client.Join(id, "old-code");
            var late = service.Watches.Last().Changed;
            host.Leave();
            Assert.That(host.Active, Is.Null);
            Assert.That(client.Active, Is.Null);
            Assert.That(service.Watches.Count, Is.Zero);
            Assert.That(host.Browser.Rows.Any(row => row.Id == id), Is.False);
            Assert.That(client.Browser.Rows.Any(row => row.Id == id), Is.False);
            host.Create("Replacement", LobbyAccess.Locked, "new-code");
            late(null, new(OnlineLobbyUpdateKind.Closure));
            Assert.That(host.Active!.Name, Is.EqualTo("Replacement"));
            client.Refresh();
            client.Join(host.Active.Id, "old-code");
            Assert.That(client.Active, Is.Null);
            host.Leave();
        }

        Assert.That(service.Lobbies, Is.Empty);
    }

    /// <summary>Late create completion cleans the orphaned online lobby rather than reviving local state.</summary>
    [Test]
    public void LateCreateAndSearchAreRejected()
    {
        var service = new Service { Delay = true };
        using var host = service.Coordinator(1);
        host.Create("Late", LobbyAccess.Public, null);
        host.Leave();
        service.Flush();
        Assert.That(host.Active, Is.Null);
        Assert.That(service.Lobbies, Is.Empty);
        host.Refresh();
        host.Leave();
        service.Flush();
        Assert.That(host.Browser.Rows, Is.Empty);
    }

    /// <summary>Raw credentials do not escape the verifier through discovery, rows, or diagnostics.</summary>
    [Test]
    public void CredentialsRemainOutOfPresentationAndDiagnostics()
    {
        const string secret = "NEVER-PRINT-THIS";
        var verifier = LobbyCredential.Create(secret);
        Assert.That(verifier.Verify(secret), Is.True);
        Assert.That(verifier.Verify("wrong"), Is.False);
        Assert.That(LobbyCredential.Parse(verifier.ExportVerifier())!.Verify(secret), Is.True);
        Assert.That(LobbyCredential.Parse("bad"), Is.Null);
        var lobby = Lobby("one", "Test") with { Access = LobbyAccess.Locked, Credential = verifier };
        Assert.That(lobby + verifier.ToString() + verifier.ExportVerifier() + JsonSerializer.Serialize(lobby.Row), Does.Not.Contain(secret));
        var service = new Service();
        using var host = service.Coordinator(1);
        host.Create("Test", LobbyAccess.Locked, secret);
        Assert.That(host.Status + host.Active, Does.Not.Contain(secret));
    }

    /// <summary>Missing, incompatible, closed, and stale full selections recover without admission.</summary>
    [Test]
    public void UnjoinableSelectionsFailCleanly()
    {
        var service = new Service();
        using var client = service.Coordinator(2);
        client.Join("missing");
        Assert.That(client.Status, Does.Contain("not found"));
        foreach (var lobby in new[] { Lobby("bad", "Bad") with { Protocol = "old" }, Lobby("closed", "Closed") with { Open = false }, Lobby("full", "Full") with { Members = 8 } })
        {
            client.Browser.Replace(new[] { lobby });
            client.Join(lobby.Id);
            Assert.That(client.Active, Is.Null);
            Assert.That(client.Busy, Is.False);
        }
    }

    /// <summary>Callbacks from a prior visit cannot close a later visit to the same logical lobby.</summary>
    [Test]
    public void LateNotificationFromPreviousVisitIsRejected()
    {
        var service = new Service();
        using var host = service.Coordinator(1);
        using var client = service.Coordinator(2);
        host.Create("Stable", LobbyAccess.Public, null);
        client.Refresh();
        client.Join(host.Active!.Id);
        var late = service.Watches.Last().Changed;
        client.Leave();
        client.Refresh();
        client.Join(host.Active.Id);
        late(null, new(OnlineLobbyUpdateKind.Closure));
        Assert.That(client.Active, Is.Not.Null);
        Assert.That(service.Watches.Count, Is.EqualTo(2));
    }

    /// <summary>A timed-out operation cannot later restore membership or obsolete discovery results.</summary>
    [Test]
    public void TimeoutsRejectLateResultsAndPermitRecovery()
    {
        var service = new Service { Delay = true };
        var clock = new Clock();
        using var host = new OnlineLobbyCoordinator(new Provider(service, User(1)), User(1), clock);
        host.Create("Timeout", LobbyAccess.Public, null);
        clock.Advance();
        host.Tick();
        Assert.That(host.Status, Does.Contain("timed out"));
        service.Flush();
        Assert.That(host.Active, Is.Null);
        Assert.That(service.Lobbies, Is.Empty);
        host.Refresh();
        clock.Advance();
        host.Tick();
        Assert.That(host.Status, Does.Contain("search timed out"));
        service.Flush();
        Assert.That(host.Browser.Rows, Is.Empty);
        service.Delay = false;
        host.Create("Recovered", LobbyAccess.Public, null);
        Assert.That(host.Active, Is.Not.Null);
    }

    /// <summary>Failed close retains cleanup ownership and blocks replacement until retry succeeds.</summary>
    [Test]
    public void FailedCloseCanBeRetriedWithoutLeakingLobby()
    {
        var service = new Service();
        using var host = service.Coordinator(1);
        host.Create("Owned", LobbyAccess.Locked, "secret-code");
        service.FailLeave = true;
        host.Leave();
        Assert.That(host.Active, Is.Null);
        Assert.That(host.CanLeave, Is.True);
        host.Create("Replacement", LobbyAccess.Public, null);
        Assert.That(service.Lobbies.Count, Is.EqualTo(1));
        service.FailLeave = false;
        host.Leave();
        Assert.That(service.Lobbies, Is.Empty);
        Assert.That(host.CanLeave, Is.False);
    }

    /// <summary>Late join and rename results cannot restore a closed membership or duplicate subscriptions.</summary>
    [Test]
    public void LateJoinRenameAndDuplicateCompletionAreInert()
    {
        var service = new Service();
        using var host = service.Coordinator(1);
        using var client = service.Coordinator(2);
        host.Create("Original", LobbyAccess.Public, null);
        var duplicate = service.LastCreate!;
        duplicate();
        Assert.That(service.Watches.Count, Is.EqualTo(1));
        host.Rename("Still current");
        duplicate();
        Assert.That(host.Active!.Name, Is.EqualTo("Still current"));
        Assert.That(service.Lobbies.Count, Is.EqualTo(1));
        client.Refresh();
        service.Delay = true;
        client.Join(host.Active!.Id);
        client.Leave();
        service.Flush();
        Assert.That(client.Active, Is.Null);
        Assert.That(host.Active.Members, Is.EqualTo(1));
        host.Rename("Late rename");
        host.Leave();
        service.Flush();
        Assert.That(host.Active, Is.Null);
        Assert.That(service.Watches, Is.Empty);
        Assert.That(service.Lobbies, Is.Empty);
        service.Delay = false;
        host.Create("Replacement", LobbyAccess.Public, null);
        duplicate();
        Assert.That(host.Active!.Name, Is.EqualTo("Replacement"));
        Assert.That(service.Lobbies.Count, Is.EqualTo(1));
    }

    /// <summary>The online client's replica rejects an otherwise valid publication from a different session.</summary>
    [Test]
    public void ClientRequiresDiscoveredSessionBeforeAssigningPlayerId()
    {
        var service = new Service();
        using var host = service.Coordinator(1);
        using var client = service.Coordinator(2);
        host.Create("Session", LobbyAccess.Public, null);
        client.Refresh();
        client.Join(host.Active!.Id);
        using var gateway = new Gateway();
        gateway.ConnectPeer(10);
        var binding = client.AttachTransport(gateway, 10, "Client");
        var wrong = new LobbyAuthority(host.Active.Session + 1, "Host");
        wrong.Join(20, GameVersion.Current.ToString(), "Client");
        gateway.ReceiveState(10, wrong.State, 2);
        binding.Driver.Pump(0);
        Assert.That(binding.Driver.State, Is.Null);
        Assert.That(binding.PlayerIds, Is.Empty);
        var correct = new LobbyAuthority(host.Active.Session, "Host");
        correct.Join(20, GameVersion.Current.ToString(), "Client");
        gateway.ReceiveState(10, correct.State, 2);
        binding.Driver.Pump(0);
        Assert.That(binding.Driver.State!.Session, Is.EqualTo(host.Active.Session));
        Assert.That(binding.PlayerIds[User(2)], Is.EqualTo(2));
    }

    /// <summary>Canceled creation blocks replacement and retains the orphan identity if its first cleanup fails.</summary>
    [Test]
    public void CanceledCreateRetainsFailedCleanupForRetry()
    {
        var service = new Service { Delay = true };
        using var host = service.Coordinator(1);
        host.Create("Canceled", LobbyAccess.Public, null);
        host.Leave();
        host.Create("Premature replacement", LobbyAccess.Public, null);
        Assert.That(service.Lobbies.Count, Is.EqualTo(1));
        service.FailLeave = true;
        service.Flush();
        Assert.That(host.Active, Is.Null);
        Assert.That(host.CanLeave, Is.True);
        service.FailLeave = false;
        service.Delay = false;
        host.Leave();
        Assert.That(service.Lobbies, Is.Empty);
        host.Create("Recovered", LobbyAccess.Public, null);
        Assert.That(host.Active!.Name, Is.EqualTo("Recovered"));
    }

    /// <summary>Previously authenticated Locked members resume without retaining the access code; other identities cannot claim their slot.</summary>
    /// <param name="access">Public or Locked admission mode.</param>
    [TestCase(LobbyAccess.Public)]
    [TestCase(LobbyAccess.Locked)]
    public void MatchLongResumeUsesAuthenticatedIdentityAndRetainsOneMapping(LobbyAccess access)
    {
        var service = new Service();
        using var host = service.Coordinator(1);
        using var client = service.Coordinator(2);
        host.Create("Test", access, access == LobbyAccess.Locked ? "test-code" : null);
        client.Refresh();
        client.Join(host.Active!.Id, "test-code");
        using var gateway = new Gateway();
        var binding = host.AttachTransport(gateway, 0, "Host");
        gateway.ConnectPeer(20);
        Assert.That(binding.AuthorizePeer(20, User(2), "test-code"), Is.True);
        gateway.ReceiveJoin(20, "Original");
        binding.Driver.Pump(0);
        binding.Driver.Authority!.SetReady(0, true);
        binding.Driver.Authority.SetReady(20, true);
        Assert.That(binding.Driver.Authority.Start(0, [20]), Is.True);
        gateway.ConnectPeer(21);
        Assert.That(binding.AuthorizePeer(21, User(2), null), Is.False, "A second live connection cannot control the same player.");
        gateway.Disconnect(20);
        binding.Driver.Pump(0);
        Assert.That(binding.Driver.State!.Players.Single(p => p.Id == 2).Connected, Is.False);
        binding.Driver.Pump(181);
        Assert.That(binding.Driver.State.Players.Single(p => p.Id == 2).Connected, Is.False);
        gateway.ConnectPeer(22);
        Assert.That(binding.AuthorizePeer(22, User(3), "test-code"), Is.False);
        gateway.ConnectPeer(23);
        Assert.That(binding.AuthorizePeer(23, User(2), null), Is.True);
        gateway.ReceiveResume(23, host.Active.Session, 2, 1);
        binding.Driver.Pump(0);
        Assert.That(binding.PlayerIds[User(2)], Is.EqualTo(2));
        Assert.That(binding.Driver.Authority!.PlayerId(20), Is.Zero);
        Assert.That(binding.Driver.Authority.PlayerId(23), Is.EqualTo(2));
        Assert.That(binding.Driver.State.Players.Count, Is.EqualTo(2));
        Assert.That(binding.Driver.State.Players.Single(p => p.Id == 2).Name, Is.EqualTo("Original"));
        gateway.Disconnect(23);
        binding.Driver.Pump(0);
        Assert.That(binding.Driver.Authority.Return(0), Is.True);
        gateway.ConnectPeer(24);
        Assert.That(binding.AuthorizePeer(24, User(2), null), Is.True);
        gateway.ReceiveJoin(24, "Fresh");
        binding.Driver.Pump(0);
        Assert.That(binding.Driver.Authority.PlayerId(24) > 0, Is.EqualTo(access == LobbyAccess.Public), "Return clears retained authorization; fresh Locked admission requires the code.");
    }

    /// <summary>A restart hint is inspected before the explicit choice and local cleanup does not claim authoritative release.</summary>
    /// <param name="incompatible">Whether the saved session now advertises a different runtime version.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void RestartLocatorRejoinsKnownSessionAndEmitsResumeInsteadOfNewAdmission(bool incompatible)
    {
        string path = Path.Combine(Path.GetTempPath(), "trackstorm-resume-" + Guid.NewGuid().ToString("N") + ".json");
        var store = new ResumeLocatorStore(path);
        try
        {
            var service = new Service();
            using var host = service.Coordinator(1);
            host.Create("Hidden match", LobbyAccess.Public, null);
            service.Lobbies[host.Active!.Id] = host.Active with { Open = false };
            if (incompatible)
            {
                service.Lobbies[host.Active.Id] = service.Lobbies[host.Active.Id] with { Version = "invalid" };
            }

            service.Notify(host.Active.Id, new(OnlineLobbyUpdateKind.Metadata));
            var locator = new ResumeLocator(host.Active!.Id, host.Active.Session, 2, 4, User(2).Value, host.Active.AuthorityEpoch, User(1).Value);
            store.Save(locator);
            using var client = new OnlineLobbyCoordinator(new Provider(service, User(2)), User(2), resumeStore: store);
            client.Tick();
            if (incompatible)
            {
                Assert.That(client.Active, Is.Null);
                Assert.That(client.Status, Does.StartWith("Game version mismatch."));
                Assert.That(client.RetainedDecision, Is.EqualTo(RetainedSessionDecision.Failed));
                Assert.That(store.Load(User(2).Value), Is.EqualTo(locator), "A version mismatch does not invalidate the authoritative reservation or its retry locator.");
                Assert.That(service.Lobbies[host.Active.Id].Members, Is.EqualTo(1));
                return;
            }

            Assert.That(client.Active!.Session, Is.EqualTo(host.Active.Session));
            Assert.That(client.Active.Open, Is.False, "Resume uses the locator even when normal admission is closed.");
            using var gateway = new Gateway();
            gateway.ConnectPeer(1);
            var binding = client.AttachTransport(gateway, 1, "Changed local name");
            binding.Driver.Pump(0);
            Assert.That(binding.Driver.Reconnecting, Is.False, "Checking must not resume gameplay before the choice.");
            Assert.That(binding.Driver.LocalPlayerId, Is.EqualTo(2));
            Assert.That(binding.Driver.Generation, Is.EqualTo(4));
            ConfirmReservation(client, gateway, binding.Driver, 1, locator.Session, 2, 4);
            client.Leave();
            Assert.That(store.Load(User(2).Value), Is.Not.Null, "Local cleanup cannot claim an authoritative release.");
        }
        finally
        {
            store.Clear();
        }
    }

    /// <summary>An unavailable menu check has a bounded wait and preserves its hint for explicit retry.</summary>
    [Test]
    public void UnavailableRestartOffersRetryWithoutErasingTheReservationHint()
    {
        string path = Path.Combine(Path.GetTempPath(), "trackstorm-resume-" + Guid.NewGuid().ToString("N") + ".json");
        var store = new ResumeLocatorStore(path);
        var service = new Service();
        var clock = new Clock();
        var lobby = Lobby("retained-match", "Match") with { Open = false };
        store.Save(new ResumeLocator(lobby.Id, lobby.Session, 2, 4, User(2).Value, 1, User(1).Value));
        try
        {
            using var client = new OnlineLobbyCoordinator(new Provider(service, User(2)), User(2), clock, store);
            client.Tick();
            clock.Advance(181);
            client.Tick();
            Assert.That(client.RetainedDecision, Is.EqualTo(RetainedSessionDecision.Failed));
            Assert.That(store.Load(User(2).Value), Is.Not.Null);
            Assert.That(client.Status, Does.Contain("not confirmed"));
            service.Lobbies[lobby.Id] = lobby;
            clock.Advance(3);
            client.RetryRetained();
            Assert.That(client.Active!.Session, Is.EqualTo(lobby.Session));
            using var gateway = new Gateway();
            gateway.ConnectPeer(1);
            var binding = client.AttachTransport(gateway, 1, "Client");
            binding.Driver.Pump(0);
            Assert.That(binding.Driver.Failure, Is.Empty);
            Assert.That(binding.Driver.LocalPlayerId, Is.EqualTo(2));
            Assert.That(binding.Driver.Generation, Is.EqualTo(4));
            ConfirmReservation(client, gateway, binding.Driver, 1, lobby.Session, 2, 4);
        }
        finally
        {
            store.Clear();
        }
    }

    /// <summary>Leave and failed migration attempts validate the retained player on menu entry, while Return clears the hint.</summary>
    /// <param name="failedAttempt">Whether a safely failed migration attempt precedes departure.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void OrdinaryArenaDeparturePreservesManualResumeUntilReturn(bool failedAttempt)
    {
        string path = Path.Combine(Path.GetTempPath(), "trackstorm-resume-" + Guid.NewGuid().ToString("N") + ".json");
        var store = new ResumeLocatorStore(path);
        var service = new Service();
        var clock = new Clock();
        using var host = service.Coordinator(1);
        host.Create("Match", LobbyAccess.Public, null);
        try
        {
            using var client = new OnlineLobbyCoordinator(new Provider(service, User(2)), User(2), clock, store);
            client.Refresh();
            client.Join(host.Active!.Id, null);
            using var gateway = new Gateway();
            gateway.ConnectPeer(1);
            var binding = client.AttachTransport(gateway, 1, "Client");
            var state = new LobbySnapshot(host.Active.Session, 2, host.Active.Session + 1, SessionPhase.Arena, [new(1, "Host", false), new(2, "Client", false)]);
            gateway.ReceiveState(1, new LobbySnapshot(state.Session, 1, state.Session, SessionPhase.Lobby, state.Players), 2);
            gateway.ReceiveState(1, state, 2);
            binding.Driver.Pump(0);
            if (failedAttempt)
            {
                binding.Driver.FailMigration("trusted fence unavailable");
            }

            client.Leave();
            clock.Advance(181);
            client.Tick();
            Assert.That(client.Active, Is.Not.Null, "Menu entry checks the reservation before showing normal discovery.");
            Assert.That(client.RetainedDecision, Is.EqualTo(RetainedSessionDecision.Checking));
            Assert.That(store.Load(User(2).Value)!.Player, Is.EqualTo(2));
            gateway.ConnectPeer(3);
            binding = client.AttachTransport(gateway, 3, "Client");
            binding.Driver.Pump(0);
            ConfirmReservation(client, gateway, binding.Driver, 3, state.Session, 2, 1);
            gateway.ReceiveState(3, new LobbySnapshot(state.Session, 3, state.Match, SessionPhase.Arena, state.Players.Select(player => player.Id == 2 ? player with { Generation = 2 } : player)), 2);
            binding.Driver.Pump(0);
            client.Tick();
            gateway.ReceiveState(3, new LobbySnapshot(state.Session, 4, state.Match, SessionPhase.Lobby, binding.Driver.State!.Players), 2);
            binding.Driver.Pump(0);
            client.Tick();
            Assert.That(store.Load(User(2).Value), Is.Null, "Observed Return clears the match locator.");
        }
        finally
        {
            store.Clear();
        }
    }

    /// <summary>Public and Locked browsers publish canonical metadata and reject mismatches before credentials or provider Join.</summary>
    /// <param name="access">Lobby access mode.</param>
    [TestCase(LobbyAccess.Public)]
    [TestCase(LobbyAccess.Locked)]
    public void BrowserVersionGatePrecedesMembershipAndCredentials(LobbyAccess access)
    {
        var service = new Service();
        using var host = service.Coordinator(1);
        using var client = service.Coordinator(2);
        host.Create("Version test", access, "test-code");
        var lobby = host.Active!;
        Assert.That(lobby.DiscoveryAttributes["version"], Is.EqualTo(GameVersion.Current.ToString()));
        Assert.That(lobby.DiscoveryAttributes.Keys, Is.EquivalentTo(access == LobbyAccess.Public
            ? new[] { "name", "session", "access", "version" } : new[] { "name", "session", "access", "version", "verifier" }));
        string incompatible = new GameVersion(GameVersion.Current.Revision == 0 ? 1 : GameVersion.Current.Revision - 1).ToString();
        service.Lobbies[lobby.Id] = lobby with { Version = incompatible };
        client.Refresh();
        var row = client.Browser.Rows.Single();
        Assert.That(row.Version, Is.EqualTo(incompatible));
        Assert.That(row.Joinable, Is.False);
        Assert.That(row.VersionMismatch, Does.Contain(incompatible).And.Contain(GameVersion.Current.ToString()));
        client.Join(lobby.Id, "wrong");
        Assert.That(client.Status, Does.StartWith("Game version mismatch."));
        client.Join(lobby.Id, "test-code");
        Assert.That(client.Active, Is.Null);
        Assert.That(service.Lobbies[lobby.Id].Members, Is.EqualTo(1));
        service.Lobbies[lobby.Id] = lobby;
        client.Refresh();
        client.Join(lobby.Id, "test-code");
        Assert.That(client.Active, Is.Not.Null);
    }

    /// <summary>Missing native metadata cannot silently inherit this runtime's version.</summary>
    [Test]
    public void MissingVersionRemainsVisibleButCannotJoin()
    {
        var browser = new LobbyBrowser();
        browser.Replace([Lobby("missing", "Missing version") with { Version = string.Empty }]);
        Assert.That(browser.Rows.Single().Joinable, Is.False);
        Assert.That(browser.Rows.Single().VersionMismatch, Does.Contain("unknown/invalid"));
    }

    /// <summary>Authenticated membership and valid Locked credentials cannot bypass the host version gate.</summary>
    /// <param name="access">Lobby access mode.</param>
    [TestCase(LobbyAccess.Public)]
    [TestCase(LobbyAccess.Locked)]
    public void BrowserBypassCannotAllocateAuthoritativePlayer(LobbyAccess access)
    {
        var service = new Service();
        using var host = service.Coordinator(1);
        using var client = service.Coordinator(2);
        host.Create("Bypass", access, "test-code");
        client.Refresh();
        client.Join(host.Active!.Id, "test-code");
        using var gateway = new Gateway();
        var binding = host.AttachTransport(gateway, 0, "Host");
        gateway.ConnectPeer(20);
        Assert.That(binding.AuthorizePeer(20, User(2), "test-code"), Is.True);
        gateway.ReceiveJoin(20, "Guest", "invalid");
        binding.Driver.Pump(0);
        Assert.That(binding.Driver.State!.Players.Count, Is.EqualTo(1));
        Assert.That(binding.Driver.Authority!.FindPlayer(User(2).Value), Is.Zero);
        Assert.That(binding.PlayerIds.ContainsKey(User(2)), Is.False);
        Assert.That(gateway.Sent.Count, Is.EqualTo(1));
        Assert.That(LobbyCodec.IsVersionMismatch(gateway.Sent[0].Payload.Span), Is.True);
        binding.Driver.Pump(1);
        Assert.That(gateway.Connections[20], Is.EqualTo(TransportConnectionState.Disconnected));
        gateway.ConnectPeer(21);
        Assert.That(binding.AuthorizePeer(21, User(2), "test-code"), Is.True);
        gateway.ReceiveJoin(21, "Guest");
        binding.Driver.Pump(0);
        Assert.That(binding.Driver.Authority.PlayerId(21), Is.EqualTo(2));
    }

    /// <summary>Direct-IP packets use the same gate and preserve their clear failure after disconnect and timeout.</summary>
    /// <param name="revision">Older or newer client build.</param>
    [TestCase(3)]
    [TestCase(5)]
    public void DirectIpVersionRejectionReachesClientWithoutBootstrap(int revision)
    {
        using var hostWire = new Gateway();
        using var clientWire = new Gateway();
        hostWire.ConnectPeer(20);
        clientWire.ConnectPeer(10);
        var host = new LobbyNetworkDriver(hostWire, 100, 0, "Host", gameVersion: new GameVersion(4));
        var client = new LobbyNetworkDriver(clientWire, 0, 10, "Guest", gameVersion: new GameVersion(revision));
        client.Pump(0);
        hostWire.ReceivePacket(20, clientWire.Sent.Single().Payload.ToArray());
        host.Pump(0);
        Assert.That(host.Authority!.Peers, Is.Empty);
        Assert.That(host.State!.Players.Count, Is.EqualTo(1));
        Assert.That(hostWire.Sent.Count, Is.EqualTo(1), "Rejected clients must receive no roster or gameplay bootstrap.");
        clientWire.ReceivePacket(10, hostWire.Sent.Single().Payload.ToArray());
        client.Pump(0);
        Assert.That(client.Failure, Does.Contain("Lobby: 0.0.1.4").And.Contain($"Your version: 0.0.1.{revision}"));
        string failure = client.Failure;
        client.Pump(20);
        Assert.That(client.Failure, Is.EqualTo(failure));
        Assert.That(client.State, Is.Null);
        Assert.That(client.LocalPlayerId, Is.Zero);
        host.Pump(1);
        hostWire.ConnectPeer(21);
        hostWire.ReceiveJoin(21, "Compatible", "0.0.1.4");
        host.Pump(0);
        Assert.That(host.Authority.PlayerId(21), Is.EqualTo(2));
    }

    /// <summary>Returning packets are checked before rebind or arena checkpoint publication.</summary>
    /// <param name="revision">Older or newer returning build.</param>
    /// <param name="arena">Whether resume targets an existing arena.</param>
    /// <param name="command">Returning player's resume or reservation operation.</param>
    [TestCase(3, false, LobbyCommand.Resume)]
    [TestCase(5, false, LobbyCommand.Resume)]
    [TestCase(3, true, LobbyCommand.Resume)]
    [TestCase(5, true, LobbyCommand.Resume)]
    [TestCase(3, true, LobbyCommand.InspectReservation)]
    [TestCase(5, true, LobbyCommand.InspectReservation)]
    [TestCase(3, true, LobbyCommand.Abandon)]
    [TestCase(5, true, LobbyCommand.Abandon)]
    public void ResumeWireMismatchCannotReclaimAuthority(int revision, bool arena, LobbyCommand command)
    {
        using var wire = new Gateway();
        var host = new LobbyNetworkDriver(wire, 100, 0, "Host", identity: _ => "subject", gameVersion: new GameVersion(4));
        wire.ConnectPeer(20);
        wire.ReceiveJoin(20, "Guest", "0.0.1.4");
        host.Pump(0);
        if (arena)
        {
            host.Authority!.SetReady(0, true);
            host.Authority.SetReady(20, true);
            Assert.That(host.Authority.Start(0), Is.True);
        }

        wire.Disconnect(20);
        host.Pump(0);
        var before = host.State;
        wire.Sent.Clear();
        wire.ConnectPeer(21);
        wire.ReceivePacket(21, LobbyCodec.EncodeResume(100, 2, 1, gameVersion: new GameVersion(revision).ToString(), command: command));
        host.Pump(0);
        Assert.That(host.State, Is.SameAs(before));
        Assert.That(host.Authority!.Peers, Is.Empty);
        Assert.That(wire.Sent.Count, Is.EqualTo(1));
        Assert.That(LobbyCodec.IsVersionMismatch(wire.Sent.Single().Payload.Span), Is.True);
        host.Pump(1);
        wire.ConnectPeer(22);
        wire.ReceiveResume(22, 100, 2, 1, gameVersion: "0.0.1.4");
        host.Pump(0);
        Assert.That(host.Authority.PlayerId(22), Is.EqualTo(arena ? 2UL : 0UL));
        Assert.That(host.State!.Players.Count, Is.EqualTo(arena ? 2 : 1));
    }

    private static void ConfirmReservation(OnlineLobbyCoordinator coordinator, Gateway gateway, Trackstorm.Client.Networking.LobbyNetworkDriver driver, ulong peer, ulong session, ulong player, ulong generation, ulong epoch = 1)
    {
        Assert.That(LobbyCodec.DecodeCommand(gateway.Sent.Last().Payload.Span).Command, Is.EqualTo(LobbyCommand.InspectReservation));
        Assert.That(driver.State, Is.Null);
        gateway.Receive(peer, LobbyCodec.EncodeReservation(session, player, generation, epoch, ReservationResult.Available));
        driver.Pump(0);
        coordinator.Tick();
        Assert.That(coordinator.RetainedDecision, Is.EqualTo(RetainedSessionDecision.Choose));
        coordinator.DecideRetained(true);
        driver.Pump(0);
        Assert.That(LobbyCodec.DecodeCommand(gateway.Sent.Last().Payload.Span).Command, Is.EqualTo(LobbyCommand.Resume));
    }

    private static OnlineProductUserId User(int id) => new(id.ToString("x32"));
    private static OnlineLobby Lobby(string id, string name) => new(id, name, User(1), 100, LobbyAccess.Public, 1, 8, OnlineLobby.CurrentProtocol, true, null) { MemberIds = new[] { User(1) } };

    private sealed class Service
    {
        private readonly Queue<Action> _pending = new();
        private int _next;
        internal Dictionary<string, OnlineLobby> Lobbies { get; } = new();
        internal List<Watch> Watches { get; } = new();
        internal bool Delay { get; set; }
        internal bool FailLeave { get; set; }
        internal bool DelayProof { get; set; }
        internal bool NotifyMetadataOnProof { get; set; }
        internal int ProofRequests { get; set; }
        internal Action<bool>? LastProof { get; set; }
        internal List<Action<bool>> PendingProofs { get; } = new();
        internal Action? LastCreate { get; set; }
        internal OnlineLobbyCoordinator Coordinator(int user) => new(new Provider(this, User(user)), User(user));
        internal void Complete(Action callback)
        {
            if (Delay)
            {
                _pending.Enqueue(callback);
            }
            else
            {
                callback();
            }
        }

        internal void Flush()
        {
            while (_pending.TryDequeue(out var callback))
            {
                callback();
            }
        }

        internal void Notify(string id, OnlineLobbyUpdate update) => Notify(id, Lobbies.GetValueOrDefault(id), update);

        internal void Notify(string id, OnlineLobby? lobby, OnlineLobbyUpdate update)
        {
            foreach (var watch in Watches.Where(watch => watch.Id == id).ToArray())
            {
                watch.Changed(lobby, update);
            }
        }

        internal void NotifyMetadata(string id, OnlineLobby lobby)
        {
            foreach (var watch in Watches.Where(watch => watch.Id == id).ToArray())
            {
                watch.Changed(lobby, new(OnlineLobbyUpdateKind.Metadata));
            }
        }

        internal void NotifyIncompleteMembership(string id)
        {
            foreach (var watch in Watches.Where(watch => watch.Id == id).ToArray())
            {
                watch.Changed(null, new(OnlineLobbyUpdateKind.Joined));
            }
        }

        internal string NextId() => (++_next).ToString();
        internal void Retire(string id, OnlineProductUserId subject)
        {
            foreach (var watch in Watches.Where(watch => watch.Id == id).ToArray())
            {
                watch.Retired?.Invoke(subject);
            }
        }
    }

    private sealed class Watch(Service service, string id, Action<OnlineLobby?, OnlineLobbyUpdate> changed, Action<OnlineProductUserId>? retired) : IDisposable
    {
        internal string Id { get; } = id;
        internal Action<OnlineLobby?, OnlineLobbyUpdate> Changed { get; } = changed;
        internal Action<OnlineProductUserId>? Retired { get; } = retired;
        public void Dispose() => service.Watches.Remove(this);
    }

    private sealed class Provider(Service service, OnlineProductUserId user) : IOnlineLobbyProvider
    {
        private readonly List<IDisposable> _watches = new();

        public void SetJoinable(string id, bool open, Action<OnlineLobby?, string?> completed)
        {
            var current = service.Lobbies[id] with { Open = open };
            service.Lobbies[id] = current;
            service.Notify(id, new(OnlineLobbyUpdateKind.Metadata));
            completed(current, null);
        }

        public void Search(Action<IReadOnlyList<OnlineLobby>, string?> completed)
        {
            var result = service.Lobbies.Values.ToArray();
            service.Complete(() => completed(result, null));
        }

        public void Create(OnlineLobby lobby, Action<OnlineLobby?, string?> completed)
        {
            lobby = lobby with { Id = service.NextId(), MemberIds = new[] { user } };
            service.Lobbies[lobby.Id] = lobby;
            service.LastCreate = () => completed(lobby, null);
            service.Complete(service.LastCreate);
        }

        public void Join(string id, Action<OnlineLobby?, string?> completed)
        {
            var lobby = service.Lobbies.GetValueOrDefault(id);
            if (lobby?.Joinable != true)
            {
                service.Complete(() => completed(null, "Lobby full or not found."));
                return;
            }

            lobby = lobby with { Members = lobby.Members + 1, MemberIds = lobby.MemberIds.Append(user).ToArray() };
            service.Lobbies[id] = lobby;
            service.Notify(id, new(OnlineLobbyUpdateKind.Joined, user));
            service.Complete(() => completed(lobby, null));
        }

        public void ConfirmMembership(string id, Action<bool> completed)
        {
            service.ProofRequests++;
            service.LastProof = completed;
            if (service.NotifyMetadataOnProof && service.Lobbies.TryGetValue(id, out var current))
            {
                service.NotifyMetadata(id, current with { Members = 1, MemberIds = new[] { current.Owner } });
            }

            if (!service.DelayProof)
            {
                completed(service.Lobbies.TryGetValue(id, out var lobby) && lobby.MemberIds.Contains(user));
            }
            else
            {
                service.PendingProofs.Add(completed);
            }
        }

        public void Resume(string id, Action<OnlineLobby?, string?> completed)
        {
            var lobby = service.Lobbies.GetValueOrDefault(id);
            if (lobby is null || (lobby.Members == 8 && !lobby.MemberIds.Contains(user)))
            {
                service.Complete(() => completed(null, "Session unavailable"));
                return;
            }

            var members = lobby.MemberIds.Append(user).Distinct().ToArray();
            lobby = lobby with { Members = members.Length, MemberIds = members };
            service.Lobbies[id] = lobby;
            service.Notify(id, new(OnlineLobbyUpdateKind.Joined, user));
            service.Complete(() => completed(lobby, null));
        }

        public void Update(OnlineLobby lobby, Action<OnlineLobby?, string?> completed)
        {
            var current = service.Lobbies[lobby.Id];
            if (!current.Owner.Equals(user))
            {
                completed(null, "Only the host can rename.");
                return;
            }

            current = current with { Name = lobby.Name };
            service.Lobbies[lobby.Id] = current;
            service.Notify(lobby.Id, new(OnlineLobbyUpdateKind.Metadata));
            service.Complete(() => completed(current, null));
        }

        public void Leave(string id, bool destroy, Action<string?> completed)
        {
            if (service.FailLeave)
            {
                completed("Service failure.");
                return;
            }

            if (service.Lobbies.TryGetValue(id, out var lobby))
            {
                if (destroy)
                {
                    service.Lobbies.Remove(id);
                }
                else
                {
                    var members = lobby.MemberIds.Where(member => !member.Equals(user)).ToArray();
                    service.Lobbies[id] = lobby with { Members = members.Length, MemberIds = members };
                }

                service.Notify(id, new(destroy ? OnlineLobbyUpdateKind.Closure : OnlineLobbyUpdateKind.Departed, user));
            }

            service.Complete(() => completed(null));
        }

        public IDisposable Watch(string id, Action<OnlineLobby?, OnlineLobbyUpdate> changed, Action<OnlineProductUserId>? retired = null)
        {
            var watch = new Watch(service, id, changed, retired);
            service.Watches.Add(watch);
            _watches.Add(watch);
            return watch;
        }

        public void Dispose() => _watches.ForEach(watch => watch.Dispose());
    }

    private sealed class Clock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        internal void Advance(double seconds = 60) => _timestamp += (long)(seconds * TimeSpan.TicksPerSecond);
    }

    private sealed class Gateway : ITransportGateway
    {
        private readonly Dictionary<ulong, TransportConnectionState> _connections = new();
        private readonly Queue<TransportMessage> _received = new();
        public event Action<TransportConnectionChange>? ConnectionChanged;

        public bool IsListening => true;
        public IReadOnlyDictionary<ulong, TransportConnectionState> Connections => _connections;
        public TransportConnectionState ConnectionState => TransportConnectionState.Connected;
        internal List<TransportMessage> Sent { get; } = new();
        public void Listen(TransportEndpoint endpoint) => throw new NotSupportedException();
        public ulong Connect(TransportEndpoint endpoint) => throw new NotSupportedException();
        public void Disconnect(ulong peerId)
        {
            _connections[peerId] = TransportConnectionState.Disconnected;
            ConnectionChanged?.Invoke(new(peerId, TransportConnectionState.Disconnected, TransportDisconnectReason.Timeout, "Test loss"));
        }

        public void Poll()
        {
        }

        public void Stop() => _connections.Clear();
        public void Dispose() => Stop();
        public TransportStatistics GetStatistics(ulong peerId) => default;
        public void ConfigureSimulation(NetworkSimulation simulation)
        {
        }

        public void Send(TransportMessage message)
        {
            Sent.Add(message);
        }

        public bool TryReceive(out TransportMessage message) => _received.TryDequeue(out message);
        internal void ReceivePacket(ulong peer, byte[] payload) => _received.Enqueue(new TransportMessage(peer, payload, TransportDelivery.Reliable));
        internal void ConnectPeer(ulong peer) => _connections[peer] = TransportConnectionState.Connected;
        internal void Receive(ulong peer, byte[] payload) => _received.Enqueue(new(peer, payload, TransportDelivery.Reliable));
        internal void ReceiveJoin(ulong peer, string name, string? gameVersion = null) => _received.Enqueue(new TransportMessage(peer, LobbyCodec.EncodeCommand(LobbyCommand.Join, null, name: name, gameVersion: gameVersion), TransportDelivery.Reliable));
        internal void ReceiveResume(ulong peer, ulong session, ulong player, ulong generation, ulong epoch = 1, string? gameVersion = null) => _received.Enqueue(new TransportMessage(peer, LobbyCodec.EncodeResume(session, player, generation, epoch, gameVersion), TransportDelivery.Reliable));
        internal void ReceiveState(ulong peer, LobbySnapshot state, ulong player) => _received.Enqueue(new TransportMessage(peer, LobbyCodec.EncodeState(state, player), TransportDelivery.Reliable));
    }
}
