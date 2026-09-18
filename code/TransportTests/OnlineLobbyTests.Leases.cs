using Trackstorm.Client.Networking;
using Trackstorm.Client.Online;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Transport.Tests;

/// <summary>Production online composition with deterministic EOS and lease-service boundaries.</summary>
internal sealed partial class OnlineLobbyTests
{
    /// <summary>Production composition retires EOS authority before sending release, even with a renewal in flight.</summary>
    /// <param name="proofExpiry">Expire membership proof instead of delivering an explicit retirement callback.</param>
    /// <param name="delayedRenewal">Keep the final renewal response in flight across retirement.</param>
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void EosRetirementStopsHealthyLeaseAndAllowsOneSuccessor(bool proofExpiry, bool delayedRenewal)
    {
        using var session = new LeaseSession();
        session.Step(180);
        if (proofExpiry)
        {
            session.Service.DelayProof = true;
            // Keep Cloudflare and gameplay healthy until just before the existing EOS deadline.
            session.Step(300);
        }

        if (delayedRenewal)
        {
            session.HostLease.Delay = true;
            for (int frame = 0; session.HostLease.Pending is null && frame < 130; frame++)
            {
                session.Step(1);
            }

            Assert.That(session.HostLease.Pending, Is.Not.Null);
        }

        session.HostLease.Sending = operation =>
        {
            if (operation == "release")
            {
                Assert.That(session.Host.Driver.Migration!.Frozen, Is.True, "Gameplay must freeze before release.");
            }
        };
        if (!proofExpiry)
        {
            session.Service.Retire(session.HostCoordinator.Active!.Id, User(1));
        }

        for (int frame = 0; !session.HostCoordinator.AuthorityRetired && frame < 660; frame++)
        {
            session.Step(1);
        }

        session.Step(1);
        Assert.That(session.Host.Driver.Migration!.Frozen, Is.True);
        int renewals = session.HostLease.Operations.Count(operation => operation == "renew");
        if (delayedRenewal)
        {
            session.HostLease.Delay = false;
            session.HostLease.Pending!.SetResult(session.HostLease.Response);
        }

        session.Service.DelayProof = false;
        session.Service.PendingProofs.ForEach(proof => proof(true));
        session.Step(780);
        Assert.That(session.HostLease.Operations.Count(operation => operation == "renew"), Is.EqualTo(renewals));
        Assert.That(session.HostLease.Operations.Count(operation => operation == "release"), Is.EqualTo(1));
        Assert.That(session.Host.Driver.Migration.Frozen, Is.True);
        Assert.That(session.HostCoordinator.CoordinationAvailable, Is.False);
        Assert.That(session.Client.Driver.Failure, Is.Empty);
        Assert.That(session.Client.Driver.State!.AuthorityEpoch, Is.EqualTo(2));
        Assert.That(session.Client.Driver.State.CurrentHostId, Is.EqualTo(2));
        Assert.That(session.ClientLease.Operations.Count(operation => operation == "takeover"), Is.EqualTo(1));
        Assert.That(session.Store.Read(session.Host.Driver.Migration.LeaseSession!)!.Holder, Is.EqualTo(User(2).Value));
    }

    /// <summary>Healthy clients stay idle; a temporary EOS delay and remote disconnect do not retire the host.</summary>
    [Test]
    public void HealthyCompositionRenewsOnlyHostAndRecoversTemporaryCoordinationDelay()
    {
        using var session = new LeaseSession();
        session.Step(1200);
        Assert.That(session.ClientLease.Operations, Is.Empty);
        Assert.That(session.HostLease.Operations.Count(operation => operation == "renew"), Is.InRange(9, 10));
        Assert.That(session.Host.Driver.Migration!.Frozen, Is.False);
        session.Service.DelayProof = true;
        session.Step(300);
        session.Service.PendingProofs.ForEach(proof => proof(true));
        session.Service.DelayProof = false;
        session.Step(120);
        Assert.That(session.HostCoordinator.AuthorityRetired, Is.False);
        Assert.That(session.Host.Driver.Migration.Frozen, Is.False);
        Assert.That(session.ClientLease.Operations, Is.Empty);

        session.HostWire.DropOutgoing = session.ClientWire.DropOutgoing = true;
        session.Step(300);
        Assert.That(session.Host.Driver.Migration.Frozen, Is.False);
        Assert.That(session.HostLease.Operations, Does.Not.Contain("release"));
        Assert.That(session.ClientLease.Operations.Count(operation => operation == "read"), Is.GreaterThanOrEqualTo(3));
        Assert.That(session.Client.Driver.Authority, Is.Null);
        Assert.That(session.Client.Driver.Migration!.HostProgressAt!(), Is.Not.Null);
        session.Step(600);
        session.Step(1200, host: false);
        Assert.That(session.Client.Driver.Authority, Is.Null, "Later host renewal invalidates the pre-partition checkpoint.");
        Assert.That(session.Client.Driver.Failure, Is.Not.Empty);
    }

    /// <summary>Production client observation starts on crash, takes over once and becomes idle after transient recovery.</summary>
    [Test]
    public void ProductionObservationStopsOnRecoveryAndRestartsForTrueCrash()
    {
        using var session = new LeaseSession();
        session.Step(180);
        session.StartArena();
        session.HostWire.DropOutgoing = session.ClientWire.DropOutgoing = true;
        session.Step(150);
        Assert.That(session.ClientLease.Operations, Does.Contain("read"));
        session.HostWire.DropOutgoing = session.ClientWire.DropOutgoing = false;
        session.HostWire.Changed!(User(2), TransportConnectionState.Disconnected, TransportDisconnectReason.Timeout);
        session.Step(300);
        Assert.That(session.Client.Driver.Migration!.Frozen, Is.False);
        int reads = session.ClientLease.Operations.Count;
        session.Step(300);
        Assert.That(session.ClientLease.Operations.Count, Is.EqualTo(reads));
        session.Step(780, host: false);
        Assert.That(session.Client.Driver.Failure, Is.Empty);
        Assert.That(session.Client.Driver.State!.AuthorityEpoch, Is.EqualTo(2));
        Assert.That(session.ClientLease.Operations.Count(operation => operation == "takeover"), Is.EqualTo(1));
    }

    /// <summary>Missing the first service observations cannot hide later old-host progress behind an expired lease.</summary>
    [Test]
    public void FirstObservationAfterOutageCannotRestorePrePartitionCheckpoint()
    {
        using var session = new LeaseSession();
        session.Step(180);
        Assert.That(session.ClientLease.Operations, Is.Empty);
        session.HostWire.DropOutgoing = session.ClientWire.DropOutgoing = true;
        session.ClientLease.Offline = true;
        session.Step(480);
        Assert.That(session.Host.Driver.Migration!.Frozen, Is.False);
        session.Step(660, host: false);
        Assert.That(session.Store.Read(session.Host.Driver.Migration.LeaseSession!)!.RemainingSeconds, Is.Zero);
        session.ClientLease.Offline = false;
        session.Step(150, host: false);
        Assert.That(session.Client.Driver.Authority, Is.Null);
        Assert.That(session.ClientLease.Operations, Does.Not.Contain("takeover"));
        Assert.That(session.Client.Driver.Failure, Is.Not.Empty);
    }

    /// <summary>Permanent EOS retirement freezes actual authoritative simulation before releasing the fence.</summary>
    [Test]
    public void EosRetirementFreezesArenaBeforeLeaseRelease()
    {
        using var session = new LeaseSession();
        session.Step(180);
        session.StartArena();
        ulong tick = session.HostVehicles!.Host!.World.State.Tick;
        session.HostLease.Sending = operation =>
        {
            if (operation == "release")
            {
                Assert.That(session.Host.Driver.Migration!.Frozen, Is.True);
                Assert.That(session.HostVehicles.Host.World.State.Tick, Is.EqualTo(tick));
            }
        };
        session.Service.Retire(session.HostCoordinator.Active!.Id, User(1));
        session.Step(780);
        Assert.That(session.HostVehicles.Host.World.State.Tick, Is.EqualTo(tick));
        Assert.That(session.Host.Driver.Request(LobbyCommand.Return), Is.False);
        Assert.That(session.Client.Driver.Failure, Is.Empty);
        Assert.That(session.Client.Driver.State!.AuthorityEpoch, Is.EqualTo(2));
        Assert.That(session.ClientVehicles!.Host!.World.State.Tick, Is.GreaterThan(tick));
        Assert.That(session.Client.Driver.State.Phase, Is.EqualTo(SessionPhase.Arena));
    }

    /// <summary>A crashed host resumes against fenced authority even while EOS still advertises itself.</summary>
    /// <param name="access">Original admission policy.</param>
    [TestCase(LobbyAccess.Public)]
    [TestCase(LobbyAccess.Locked)]
    public void CrashedFormerHostResolvesTrustedRouteWhileEosRemainsStale(LobbyAccess access)
    {
        var store = new ResumeLocatorStore(Path.Combine(Path.GetTempPath(), "trackstorm-stale-route-" + Guid.NewGuid().ToString("N") + ".json"));
        try
        {
            using var session = new LeaseSession(access, store);
            session.Step(120);
            session.StartArena();
            Assert.That(session.HostVehicles!.Host!.Items.Grant(session.HostVehicles.Host.World, 1, Core.Items.HeldItem.Missile), Is.True);
            session.Step(360);
            var saved = store.Load(User(1).Value)!;
            Assert.That(saved, Is.Not.Null);
            Assert.That(saved.RoutingId, Is.Not.Null);
            Assert.That(saved.RoutingId, Is.Not.EqualTo(session.Host.Driver.Migration!.LeaseSession));
            session.HostWire.DropOutgoing = true;
            session.Step(900, host: false);
            Assert.That(session.Client.Driver.Authority, Is.Not.Null);
            Assert.That(session.Client.Driver.State!.AuthorityEpoch, Is.EqualTo(2));
            var retainedVehicle = session.ClientVehicles!.Host!.World.GetVehicle(1)!;
            var retainedItem = session.ClientVehicles.Host.Items.Slots.Single(slot => slot.Vehicle == 1);
            Assert.That(session.Service.Lobbies[saved.Lobby].HostIdentity, Is.EqualTo(User(1)));
            Assert.That(session.Service.Lobbies[saved.Lobby].AuthorityEpoch, Is.EqualTo(1));
            using var restarted = new OnlineLobbyCoordinator(new Provider(session.Service, User(1)), User(1), session.Clock, store)
            {
                LeaseFactory = () => new LeaseTransport(session.Store, User(1).Value),
            };
            for (int frame = 0; frame < 240 && restarted.Active is null; frame++)
            {
                restarted.Tick();
                session.Step(1, host: false);
            }

            Assert.That(restarted.Active, Is.Not.Null, restarted.Status);
            Assert.That(restarted.Active!.HostIdentity, Is.EqualTo(User(2)));
            Assert.That(restarted.Active.AuthorityEpoch, Is.EqualTo(2));
            Assert.That(restarted.StartsGameplayAuthority, Is.False);
            Assert.That(session.Service.Lobbies[saved.Lobby].Owner, Is.EqualTo(User(1)), "EOS ownership has never moved.");
            var wire = new EosP2pWire(User(1)) { Other = session.ClientWire };
            session.ClientWire.Other = wire;
            using var transport = new EosP2pTransport(wire, User(1), () => restarted.Active, time: session.Clock);
            ulong peer = transport.Connect(EosP2pTransport.Endpoint(restarted.Active, restarted.Active.HostIdentity));
            var binding = restarted.AttachTransport(transport, peer, "Former host");
            transport.Authorize = binding.AuthorizePeer;
            var commands = new List<LobbyCommand>();
            for (int frame = 0; frame < 180 && binding.Driver.State is null; frame++)
            {
                restarted.Tick();
                binding.Driver.Pump(1.0 / 60);
                foreach (var packet in session.ClientWire.Packets)
                {
                    if (packet.Bytes.Length > EosPacketAssembly.Header + 3 && packet.Bytes[0] == 4 &&
                        LobbyCodec.IsLobby(packet.Bytes.AsSpan(EosPacketAssembly.Header)) && packet.Bytes[EosPacketAssembly.Header + 3] == 1)
                    {
                        commands.Add(LobbyCodec.DecodeCommand(packet.Bytes.AsSpan(EosPacketAssembly.Header)).Command);
                    }
                }

                session.Step(1, host: false);
            }

            Assert.That(binding.Driver.State?.Phase, Is.EqualTo(SessionPhase.Arena));
            Assert.That(commands, Does.Contain(LobbyCommand.Resume));
            Assert.That(commands, Does.Not.Contain(LobbyCommand.Join));
            var vehicles = new VehicleNetworkDriver(transport, 0, peer, binding.Driver);
            for (int frame = 0; frame < 180; frame++)
            {
                restarted.Tick();
                vehicles.Advance(default, state => new(state.Movement.Physics, System.Numerics.Vector3.UnitY));
                session.Step(1, host: false);
            }

            Assert.That(binding.Driver.Authority, Is.Null);
            Assert.That(vehicles.Host, Is.Null);
            Assert.That(binding.Driver.LocalPlayerId, Is.EqualTo(saved.Player));
            Assert.That(binding.Driver.Generation, Is.EqualTo(saved.Generation + 1), "Core Resume advances the retained generation; Join cannot admit an arena player.");
            Assert.That(binding.Driver.Reconnecting, Is.False, binding.Driver.Failure);
            Assert.That(binding.Driver.NeedsArenaCheckpoint, Is.False);
            Assert.That(binding.Driver.State!.Session, Is.EqualTo(saved.Session));
            Assert.That(binding.Driver.State.CurrentHostId, Is.EqualTo(2));
            Assert.That(binding.Driver.State.AuthorityEpoch, Is.EqualTo(2));
            Assert.That(binding.Driver.State.Players.Select(player => player.Id), Is.EquivalentTo(new ulong[] { 1, 2 }));
            Assert.That(vehicles.LocalVehicleId, Is.EqualTo(saved.Player));
            Assert.That(vehicles.LocalState, Is.Not.Null);
            Assert.That(vehicles.LocalState!.LifeId, Is.EqualTo(retainedVehicle.LifeId));
            Assert.That(vehicles.LocalState.Damage.CurrentHP, Is.EqualTo(retainedVehicle.Damage.CurrentHP));
            Assert.That(vehicles.LocalState.Lifecycle, Is.EqualTo(retainedVehicle.Lifecycle));
            Assert.That(vehicles.LocalItem, Is.EqualTo(retainedItem));
            Assert.That(session.ClientVehicles!.Host!.World.State.Vehicles.Select(vehicle => vehicle.VehicleId), Is.EquivalentTo(new ulong[] { 1, 2 }));
            int rejected = session.Client.Driver.RejectedPackets;
            binding.Driver.Request(LobbyCommand.Return);
            session.Step(2, host: false);
            Assert.That(session.Client.Driver.RejectedPackets, Is.GreaterThan(rejected), "Core rejects host-only commands from the former host.");
            Assert.That(session.Client.Driver.State.Phase, Is.EqualTo(SessionPhase.Arena));
        }
        finally
        {
            store.Clear();
        }
    }

    /// <summary>Routing outages and invalid observations cannot bypass self/epoch fences or erase retained identity.</summary>
    /// <param name="failure">Injected service failure.</param>
    [TestCase("outage")]
    [TestCase("self")]
    [TestCase("older")]
    [TestCase("conflict")]
    [TestCase("wrong-locator")]
    [TestCase("expired")]
    [TestCase("malformed-holder")]
    [TestCase("invalid-duration")]
    [TestCase("delayed")]
    [TestCase("cancelled")]
    public void RestartRoutingFailsClosedAndRetriesWithoutAuthority(string failure)
    {
        var clock = new Clock();
        var service = new Service();
        using var original = service.Coordinator(1);
        original.Create("Routing fences", LobbyAccess.Public, null);
        using var leases = new LeaseStore(clock);
        string key = new('A', 64);
        var first = leases.Execute("create", new(key, 0, string.Empty), User(1).Value)!;
        leases.Execute("release", new(key, 1, first.Token), User(1).Value);
        leases.Execute("takeover", new(key, 1, first.Token), User(2).Value);
        var store = new ResumeLocatorStore(Path.Combine(Path.GetTempPath(), "trackstorm-route-fence-" + Guid.NewGuid().ToString("N") + ".json"));
        var locator = new ResumeLocator(original.Active!.Id, original.Active.Session, 1, 1, User(1).Value, 1, User(1).Value, first.RoutingId);
        store.Save(locator);
        var delayed = new TaskCompletionSource<LeaseRoute?>();
        LeaseRoute? held = null;
        var transport = new LeaseTransport(leases, User(1).Value)
        {
            RoutingResponse = route =>
            {
                held = route;
                if (failure is "delayed" or "cancelled")
                {
                    return delayed.Task;
                }

                return Task.FromResult(failure switch
                {
                    "outage" => null,
                    "self" => route! with { Holder = User(1).Value },
                    "older" => route! with { Epoch = 0 },
                    "conflict" => route! with { Epoch = 1 },
                    "wrong-locator" => route! with { RoutingId = new string('B', 64) },
                    "expired" => route! with { RemainingSeconds = 0 },
                    "malformed-holder" => route! with { Holder = "invalid" },
                    "invalid-duration" => route! with { RemainingSeconds = double.NaN },
                    _ => route,
                });
            },
        };
        try
        {
            using var restarted = new OnlineLobbyCoordinator(new Provider(service, User(1)), User(1), clock, store) { LeaseFactory = () => transport };
            restarted.Tick();
            if (failure == "cancelled")
            {
                restarted.Leave();
                delayed.SetResult(held);
                restarted.Tick();
                Assert.That(restarted.Active, Is.Null);
                Assert.That(restarted.SavedResume, Is.Null);
                return;
            }

            if (failure == "delayed")
            {
                clock.Advance(3);
                delayed.SetResult(held);
            }

            restarted.Tick();
            Assert.That(restarted.Active, Is.Null);
            Assert.That(restarted.SavedResume, Is.EqualTo(locator));
            Assert.That(restarted.StartsGameplayAuthority, Is.False);
            transport.RoutingResponse = null;
            clock.Advance(2);
            restarted.Tick();
            restarted.Tick();
            Assert.That(restarted.Active!.HostIdentity, Is.EqualTo(User(2)));
            Assert.That(transport.Operations.All(operation => operation == "route"), Is.True, "Restart can only read routing, never create/renew/take over authority.");
        }
        finally
        {
            store.Clear();
        }
    }

    private sealed class LeaseSession : IDisposable
    {
        private readonly Clock _clock = new();
        private readonly OnlineLobbyCoordinator _clientCoordinator;
        private readonly EosP2pTransport _hostTransport;
        private readonly EosP2pTransport _clientTransport;

        internal LeaseSession(LobbyAccess access = LobbyAccess.Public, ResumeLocatorStore? resumeStore = null)
        {
            Store = new(_clock);
            HostLease = new(Store, User(1).Value);
            ClientLease = new(Store, User(2).Value);
            HostCoordinator = new(new Provider(Service, User(1)), User(1), _clock, resumeStore) { LeaseFactory = () => HostLease };
            _clientCoordinator = new(new Provider(Service, User(2)), User(2), _clock) { LeaseFactory = () => ClientLease };
            HostCoordinator.Create("Lease composition", access, "test-code");
            _clientCoordinator.Refresh();
            _clientCoordinator.Join(HostCoordinator.Active!.Id, "test-code");
            HostWire = new(User(1));
            ClientWire = new(User(2));
            HostWire.Other = ClientWire;
            ClientWire.Other = HostWire;
            _hostTransport = new(HostWire, User(1), () => HostCoordinator.Active, time: _clock);
            _clientTransport = new(ClientWire, User(2), () => _clientCoordinator.Active, "test-code", time: _clock);
            _hostTransport.Listen(EosP2pTransport.Endpoint(HostCoordinator.Active!, User(1)));
            Host = HostCoordinator.AttachTransport(_hostTransport, 0, "Host");
            _hostTransport.Authorize = Host.AuthorizePeer;
            ulong server = _clientTransport.Connect(EosP2pTransport.Endpoint(_clientCoordinator.Active!, User(1)));
            Client = _clientCoordinator.AttachTransport(_clientTransport, server, "Client");
            _clientTransport.Authorize = Client.AuthorizePeer;
        }

        internal Service Service { get; } = new();
        internal TimeProvider Clock => _clock;
        internal LeaseStore Store { get; }
        internal LeaseTransport HostLease { get; }
        internal LeaseTransport ClientLease { get; }
        internal OnlineLobbyCoordinator HostCoordinator { get; }
        internal OnlineSessionBinding Host { get; }
        internal OnlineSessionBinding Client { get; }
        internal EosP2pWire HostWire { get; }
        internal EosP2pWire ClientWire { get; }
        internal VehicleNetworkDriver? HostVehicles { get; private set; }
        internal VehicleNetworkDriver? ClientVehicles { get; private set; }

        public void Dispose()
        {
            _clientCoordinator.Dispose();
            HostCoordinator.Dispose();
            _hostTransport.Dispose();
            _clientTransport.Dispose();
            Store.Dispose();
        }

        internal void Step(int frames, bool host = true)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                _clock.Advance(1.0 / 60);
                if (host)
                {
                    HostCoordinator.Tick();
                    Pump(Host, HostVehicles);
                }

                _clientCoordinator.Tick();
                Pump(Client, ClientVehicles);
            }
        }

        internal void StartArena()
        {
            Host.Driver.Request(LobbyCommand.Ready, true);
            Client.Driver.Request(LobbyCommand.Ready, true);
            Step(2);
            Assert.That(Host.Driver.Request(LobbyCommand.Start), Is.True);
            Step(2);
            HostVehicles = new(_hostTransport, Host.Driver.State!.Match, 0, Host.Driver);
            ClientVehicles = new(_clientTransport, 0, Client.Driver.ServerPeer, Client.Driver);
            Step(240);
        }

        private static void Pump(OnlineSessionBinding binding, VehicleNetworkDriver? vehicles)
        {
            if (vehicles is null)
            {
                binding.Driver.Pump(1.0 / 60);
            }
            else
            {
                vehicles.Advance(default, state => new(state.Movement.Physics, System.Numerics.Vector3.UnitY));
            }
        }
    }
}
