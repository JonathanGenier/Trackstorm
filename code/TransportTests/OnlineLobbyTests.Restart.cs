using Trackstorm.Client.Networking;
using Trackstorm.Client.Online;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Transport.Tests;

/// <summary>Retained ordinary-player restart through authenticated checkpoints and trusted routing.</summary>
internal sealed partial class OnlineLobbyTests
{
    /// <summary>A healthy client learns read-only routing before disconnecting and survives a later host migration.</summary>
    /// <param name="access">Initial session access policy.</param>
    [TestCase(LobbyAccess.Public)]
    [TestCase(LobbyAccess.Locked)]
    public void OrdinaryClientRestartsAfterLaterMigrationWithStaleEosRouting(LobbyAccess access)
    {
        string path = Path.Combine(Path.GetTempPath(), "trackstorm-ordinary-routing-" + Guid.NewGuid().ToString("N") + ".json");
        var resumeStore = new ResumeLocatorStore(path);
        var clock = new Clock();
        var service = new Service();
        using var leases = new LeaseStore(clock);
        var coordinators = new OnlineLobbyCoordinator[4];
        var transports = new EosP2pTransport[4];
        var bindings = new OnlineSessionBinding[4];
        var vehicles = new VehicleNetworkDriver?[4];
        var leaseTransports = Enumerable.Range(1, 4).Select(id => new LeaseTransport(leases, User(id).Value)).ToArray();
        var wires = Enumerable.Range(1, 4).Select(id => new EosP2pWire(User(id))).ToArray();
        var routes = Enumerable.Range(0, 4).ToDictionary(i => User(i + 1), i => wires[i]);
        var running = Enumerable.Repeat(true, 4).ToArray();
        void Step(int frames)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                clock.Advance(1.0 / 60);
                for (int i = 0; i < bindings.Length; i++)
                {
                    if (!running[i])
                    {
                        continue;
                    }

                    coordinators[i].Tick();
                    if (vehicles[i] is { } arena)
                    {
                        arena.Advance(default, state => new(state.Movement.Physics, System.Numerics.Vector3.UnitY));
                    }
                    else
                    {
                        bindings[i].Driver.Pump(1.0 / 60);
                    }
                }
            }
        }

        try
        {
            for (int i = 0; i < coordinators.Length; i++)
            {
                int index = i;
                coordinators[i] = new(new Provider(service, User(i + 1)), User(i + 1), clock, i == 1 ? resumeStore : null) { LeaseFactory = () => leaseTransports[index] };
                wires[i].Routes = routes;
                if (i == 0)
                {
                    coordinators[i].Create("Ordinary restart", access, "test-code");
                }
                else
                {
                    coordinators[i].Refresh();
                    coordinators[i].Join(coordinators[0].Active!.Id, "test-code");
                }

                transports[i] = new(wires[i], User(i + 1), () => coordinators[index].Active, "test-code", clock);
                if (i == 0)
                {
                    transports[i].Listen(EosP2pTransport.Endpoint(coordinators[i].Active!, User(1)));
                }

                ulong server = i == 0 ? 0 : transports[i].Connect(EosP2pTransport.Endpoint(coordinators[i].Active!, User(1)));
                bindings[i] = coordinators[i].AttachTransport(transports[i], server, "Player " + (i + 1));
                transports[i].Authorize = bindings[i].AuthorizePeer;
            }

            Step(120);
            foreach (var binding in bindings)
            {
                binding.Driver.Request(LobbyCommand.Ready, true);
            }

            Step(2);
            Assert.That(bindings[0].Driver.Request(LobbyCommand.Start), Is.True);
            Step(2);
            for (int i = 0; i < vehicles.Length; i++)
            {
                vehicles[i] = new(transports[i], i == 0 ? bindings[i].Driver.State!.Match : 0, bindings[i].Driver.ServerPeer, bindings[i].Driver);
            }

            Assert.That(vehicles[0]!.Host!.Items.Grant(vehicles[0]!.Host!.World, 2, Core.Items.HeldItem.Missile), Is.True);
            Step(360);
            var saved = resumeStore.Load(User(2).Value)!;
            Assert.That(saved, Is.Not.Null);
            Assert.That(saved.RoutingId, Is.Not.Null, "Healthy clients must receive the read-only locator without observing the lease service.");
            Assert.That(saved.RoutingId, Is.EqualTo(bindings[0].RoutingId));
            Assert.That(leaseTransports[1].Operations, Is.Empty);
            Assert.That(bindings[1].Driver.Authority, Is.Null);
            string persisted = File.ReadAllText(path);
            string leaseSession = bindings[0].Driver.Migration!.LeaseSession!;
            Assert.That(persisted, Does.Not.Contain(leaseSession));
            Assert.That(persisted, Does.Not.Contain(leases.Read(leaseSession)!.Token));

            // Keep two eligible survivors: a larger roster cannot use the two-player election exception.
            running[1] = false;
            transports[1].Stop();
            wires[0].Changed!(User(2), TransportConnectionState.Disconnected, TransportDisconnectReason.Timeout);
            Step(900);
            Assert.That(bindings[0].Driver.State!.Players.Single(player => player.Id == 2).Connected, Is.False);
            var retainedVehicle = vehicles[0]!.Host!.World.GetVehicle(2)!;
            var retainedItem = vehicles[0]!.Host!.Items.Slots.Single(slot => slot.Vehicle == 2);
            running[0] = false;
            wires[0].DropOutgoing = true;
            Step(1200);
            Assert.That(bindings[2].Driver.Authority, Is.Not.Null, bindings[2].Driver.Migration!.Diagnostics);
            Assert.That(bindings[2].Driver.State!.AuthorityEpoch, Is.EqualTo(2));
            Assert.That(bindings[3].Driver.State!.AuthorityEpoch, Is.EqualTo(2));
            Assert.That(service.Lobbies[saved.Lobby].AuthorityEpoch, Is.EqualTo(1));
            Assert.That(service.Lobbies[saved.Lobby].HostIdentity, Is.EqualTo(User(1)));

            var restartLease = new LeaseTransport(leases, User(2).Value);
            using var restarted = new OnlineLobbyCoordinator(new Provider(service, User(2)), User(2), clock, resumeStore) { LeaseFactory = () => restartLease };
            restarted.Tick();
            restarted.ResumeRetained();
            for (int frame = 0; frame < 180 && restarted.Active is null; frame++)
            {
                restarted.Tick();
                Step(1);
            }

            Assert.That(restarted.Active!.HostIdentity, Is.EqualTo(User(3)));
            Assert.That(restarted.Active.AuthorityEpoch, Is.EqualTo(2));
            Assert.That(restartLease.Operations, Is.EqualTo(new[] { "route" }));
            var returnWire = new EosP2pWire(User(2)) { Routes = routes };
            routes[User(2)] = returnWire;
            using var returnTransport = new EosP2pTransport(returnWire, User(2), () => restarted.Active, time: clock);
            ulong returnPeer = returnTransport.Connect(EosP2pTransport.Endpoint(restarted.Active, restarted.Active.HostIdentity));
            var returned = restarted.AttachTransport(returnTransport, returnPeer, "Retained client");
            returnTransport.Authorize = returned.AuthorizePeer;
            var commands = new List<LobbyCommand>();
            for (int frame = 0; frame < 180 && returned.Driver.State is null; frame++)
            {
                restarted.Tick();
                returned.Driver.Pump(1.0 / 60);
                if (restarted.RetainedDecision == RetainedSessionDecision.Choose)
                {
                    restarted.DecideRetained(true);
                }

                foreach (var packet in wires[2].Packets)
                {
                    if (packet.Peer.Equals(User(2)) && packet.Bytes.Length > EosPacketAssembly.Header + 3 && packet.Bytes[0] == 4 &&
                        LobbyCodec.IsLobby(packet.Bytes.AsSpan(EosPacketAssembly.Header)) && packet.Bytes[EosPacketAssembly.Header + 3] == 1)
                    {
                        commands.Add(LobbyCodec.DecodeCommand(packet.Bytes.AsSpan(EosPacketAssembly.Header)).Command);
                    }
                }

                Step(1);
            }

            Assert.That(returned.Driver.State?.Phase, Is.EqualTo(SessionPhase.Arena));
            Assert.That(commands, Does.Contain(LobbyCommand.Resume));
            Assert.That(commands, Does.Not.Contain(LobbyCommand.Join));
            var returnVehicles = new VehicleNetworkDriver(returnTransport, 0, returnPeer, returned.Driver);
            for (int frame = 0; frame < 180; frame++)
            {
                restarted.Tick();
                returnVehicles.Advance(default, state => new(state.Movement.Physics, System.Numerics.Vector3.UnitY));
                Step(1);
            }

            Assert.That(returned.Driver.LocalPlayerId, Is.EqualTo(saved.Player));
            Assert.That(returned.Driver.Generation, Is.EqualTo(saved.Generation + 1));
            Assert.That(returned.Driver.State!.Session, Is.EqualTo(saved.Session));
            Assert.That(returned.Driver.State.Match, Is.EqualTo(bindings[0].Driver.State!.Match));
            Assert.That(returned.Driver.State.CurrentHostId, Is.EqualTo(3));
            Assert.That(returned.Driver.Authority, Is.Null);
            Assert.That(returnVehicles.Host, Is.Null);
            Assert.That(returned.Driver.Reconnecting, Is.False);
            Assert.That(returned.Driver.NeedsArenaCheckpoint, Is.False);
            Assert.That(returnVehicles.LocalState!.LifeId, Is.EqualTo(retainedVehicle.LifeId));
            Assert.That(returnVehicles.LocalState.Damage.CurrentHP, Is.EqualTo(retainedVehicle.Damage.CurrentHP));
            Assert.That(returnVehicles.LocalItem, Is.EqualTo(retainedItem));
            Assert.That(returned.Driver.State.Players.Select(player => player.Id), Is.EquivalentTo(new ulong[] { 1, 2, 3, 4 }));
            Assert.That(vehicles[2]!.Host!.World.State.Vehicles.Select(vehicle => vehicle.VehicleId), Is.EquivalentTo(new ulong[] { 1, 2, 3, 4 }));
            int rejected = bindings[2].Driver.RejectedPackets;
            returned.Driver.Request(LobbyCommand.Return);
            Step(2);
            Assert.That(bindings[2].Driver.RejectedPackets, Is.GreaterThan(rejected));
            Assert.That(bindings[2].Driver.State!.Phase, Is.EqualTo(SessionPhase.Arena));
            Assert.That(resumeStore.Load(User(2).Value)!.RoutingId, Is.EqualTo(saved.RoutingId));
            Assert.That(restartLease.Operations, Is.EqualTo(new[] { "route" }), "Resumed healthy clients do not poll, create, renew or take over authority.");
        }
        finally
        {
            foreach (var coordinator in coordinators)
            {
                coordinator?.Dispose();
            }

            foreach (var transport in transports)
            {
                transport?.Dispose();
            }

            resumeStore.Clear();
        }
    }
}
