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

    private sealed class LeaseSession : IDisposable
    {
        private readonly Clock _clock = new();
        private readonly OnlineLobbyCoordinator _clientCoordinator;
        private readonly EosP2pTransport _hostTransport;
        private readonly EosP2pTransport _clientTransport;

        internal LeaseSession()
        {
            Store = new(_clock);
            HostLease = new(Store, User(1).Value);
            ClientLease = new(Store, User(2).Value);
            HostCoordinator = new(new Provider(Service, User(1)), User(1), _clock) { LeaseFactory = () => HostLease };
            _clientCoordinator = new(new Provider(Service, User(2)), User(2), _clock) { LeaseFactory = () => ClientLease };
            HostCoordinator.Create("Lease composition", LobbyAccess.Public, null);
            _clientCoordinator.Refresh();
            _clientCoordinator.Join(HostCoordinator.Active!.Id);
            HostWire = new(User(1));
            ClientWire = new(User(2));
            HostWire.Other = ClientWire;
            ClientWire.Other = HostWire;
            _hostTransport = new(HostWire, User(1), () => HostCoordinator.Active, time: _clock);
            _clientTransport = new(ClientWire, User(2), () => _clientCoordinator.Active, time: _clock);
            _hostTransport.Listen(EosP2pTransport.Endpoint(HostCoordinator.Active!, User(1)));
            Host = HostCoordinator.AttachTransport(_hostTransport, 0, "Host");
            _hostTransport.Authorize = Host.AuthorizePeer;
            ulong server = _clientTransport.Connect(EosP2pTransport.Endpoint(_clientCoordinator.Active!, User(1)));
            Client = _clientCoordinator.AttachTransport(_clientTransport, server, "Client");
            _clientTransport.Authorize = Client.AuthorizePeer;
        }

        internal Service Service { get; } = new();
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
