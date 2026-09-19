using Trackstorm.Client.Hud;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Online;
using Trackstorm.Core.Input;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Transport.Tests;

/// <summary>Explicit retained-session choices through the existing authenticated lobby protocol.</summary>
internal sealed partial class OnlineLobbyTests
{
    /// <summary>Authenticated inspection changes nothing; explicit release is acknowledged once and preserves final history.</summary>
    /// <param name="access">Existing admission policy.</param>
    /// <param name="dropReply">Whether the release acknowledgement is lost before local confirmation.</param>
    [TestCase(LobbyAccess.Public, false)]
    [TestCase(LobbyAccess.Locked, false)]
    [TestCase(LobbyAccess.Locked, true)]
    public void MenuAbandonmentRequiresConfirmationAndPreservesHistory(LobbyAccess access, bool dropReply)
    {
        var service = new Service();
        var clock = new Clock();
        using var host = service.Coordinator(1);
        using var original = service.Coordinator(2);
        host.Create("Retained match", access, "test-code");
        original.Refresh();
        original.Join(host.Active!.Id, "test-code");
        using var hostGateway = new Gateway();
        var hosting = host.AttachTransport(hostGateway, 0, "Host");
        hostGateway.ConnectPeer(20);
        Assert.That(hosting.AuthorizePeer(20, User(2), "test-code"), Is.True);
        hostGateway.ReceiveJoin(20, "Retained winner");
        hosting.Driver.Pump(0);
        var authority = hosting.Driver.Authority!;
        authority.SetReady(0, true);
        authority.SetReady(20, true);
        authority.Start(0);
        var vehicles = new VehicleNetworkDriver(hostGateway, authority.State.Match, lobby: hosting.Driver);
        vehicles.Advance(default, state => new(state.Movement.Physics, System.Numerics.Vector3.UnitY));
        Assert.That(vehicles.Host!.World.State.Vehicles.Count, Is.EqualTo(2));
        var previous = vehicles.Host.World.State;
        var scored = new MatchState(previous.Tick, previous.Match!.Revision + 1, previous.Match.KillTarget, MatchPhase.Active, null, null, [new(1, 0, 1, 0, 1), new(2, 1, 1, 0, 1)]);
        vehicles.Host.World.Restore(new Core.Simulation.SimulationState(previous.Tick, previous.LastInput, previous.Vehicles, scored));
        hostGateway.Disconnect(20);
        hosting.Driver.Pump(0);
        vehicles.Advance(default, state => new(state.Movement.Physics, System.Numerics.Vector3.UnitY));
        var originalScore = vehicles.Host.World.State.Match!.Players.Single(player => player.Player == 2);
        ulong revision = authority.State.Revision;
        var store = new ResumeLocatorStore(Path.Combine(Path.GetTempPath(), "trackstorm-decision-" + Guid.NewGuid().ToString("N") + ".json"));
        store.Save(new ResumeLocator(host.Active.Id, host.Active.Session, 2, 1, User(2).Value, 1, User(1).Value));
        try
        {
            using var client = new OnlineLobbyCoordinator(new Provider(service, User(2)), User(2), clock, store);
            Assert.That(client.RetainedDecision, Is.EqualTo(RetainedSessionDecision.Checking));
            client.Tick();
            using var gateway = new Gateway();
            gateway.ConnectPeer(1);
            var binding = client.AttachTransport(gateway, 1, "Changed name");
            hostGateway.ConnectPeer(30);
            Assert.That(hosting.AuthorizePeer(30, User(2), null), Is.True);
            binding.Driver.Pump(0);
            hostGateway.Receive(30, gateway.Sent.Last().Payload.ToArray());
            hosting.Driver.Pump(0);
            byte[] available = hostGateway.Sent.Last(message => message.RemotePeerId == 30).Payload.ToArray();
            gateway.Receive(1, available);
            gateway.Receive(1, available);
            binding.Driver.Pump(0);
            client.Tick();
            Assert.That(client.RetainedDecision, Is.EqualTo(RetainedSessionDecision.Choose));
            Assert.That(binding.Driver.State, Is.Null);
            Assert.That(binding.PlayerIds, Is.Empty);
            Assert.That(authority.State.Revision, Is.EqualTo(revision));
            clock.Advance(181);
            client.Tick();
            Assert.That(client.RetainedDecision, Is.EqualTo(RetainedSessionDecision.Choose), "No automatic resume or decision expiry.");
            client.DecideRetained(false);
            client.DecideRetained(false);
            client.DecideRetained(true);
            binding.Driver.Pump(0);
            Assert.That(gateway.Sent.Count, Is.EqualTo(2));
            Assert.That(client.RetainedDecision, Is.EqualTo(RetainedSessionDecision.Leaving));
            Assert.That(store.Load(User(2).Value), Is.Not.Null);
            byte[] abandon = gateway.Sent.Last().Payload.ToArray();
            hostGateway.Receive(30, abandon);
            hostGateway.Receive(30, abandon);
            hosting.Driver.Pump(0);
            Assert.That(authority.State.Revision, Is.EqualTo(revision + 1));
            Assert.That(authority.FindPlayer(User(2).Value), Is.Zero);
            Assert.That(authority.State.Players.Count, Is.EqualTo(1));
            Assert.That(authority.State.Departed.Single().Name, Is.EqualTo("Retained winner"));
            vehicles.Advance(default, state => new(state.Movement.Physics, System.Numerics.Vector3.UnitY));
            Assert.That(vehicles.Host.World.State.Vehicles.Count, Is.EqualTo(1), "The released reservation cannot retain a duplicate vehicle.");
            Assert.That(vehicles.Host.World.State.Match!.Players.Single(player => player.Player == 2), Is.EqualTo(originalScore));
            Assert.That(authority.Resume(40, GameVersion.Current.ToString(), host.Active.Session, 2, 1, User(2).Value), Is.False);
            var match = new MatchState(10, 1, 5, MatchPhase.Finished, null, 2, [new(1, 0, 5, 0, 5), new(2, 5, 0, 1, 0)]);
            var view = MatchStandingsView.From(authority.State, match, 1, InputButtons.None, _ => 42);
            Assert.That(view.WinnerName, Is.EqualTo("Retained winner"));
            Assert.That(view.Rows[0], Is.EqualTo(new StandingsRow(2, 1, "Retained winner", 5, 0, "--", true, false, false)));
            if (dropReply)
            {
                clock.Advance(21);
                client.Tick();
                Assert.That(client.RetainedDecision, Is.EqualTo(RetainedSessionDecision.Failed));
                Assert.That(client.Status, Does.Contain("not confirmed"));
                Assert.That(store.Load(User(2).Value), Is.Not.Null);
                client.DismissRetainedFailure();
                Assert.That(client.CanResumeRetained, Is.True);
            }
            else
            {
                gateway.Receive(1, hostGateway.Sent.Last(message => message.RemotePeerId == 30).Payload.ToArray());
                binding.Driver.Pump(0);
                service.Delay = true;
                client.Tick();
                Assert.That(client.RetainedDecision, Is.EqualTo(RetainedSessionDecision.None));
                Assert.That(client.Active, Is.Null);
                Assert.That(client.Status, Does.StartWith("Left match."));
                Assert.That(store.Load(User(2).Value), Is.Null);
                service.Flush();
                Assert.That(client.Status, Does.StartWith("Left match."), "Delayed membership cleanup preserves the authoritative outcome.");
                client.Tick();
                Assert.That(client.Active, Is.Null, "Confirmed abandonment never starts another resume.");
            }
        }
        finally
        {
            store.Clear();
        }
    }

    /// <summary>A stale claim is cleared only by the current host, while foreign/replayed results cannot unlock the browser.</summary>
    /// <param name="changedSession">Whether membership metadata already proves this is a replacement session.</param>
    /// <param name="wireMismatch">Whether the host rejects the runtime version after metadata was accepted.</param>
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public void StaleReservationReturnsToBrowserAfterBoundHostResponse(bool changedSession, bool wireMismatch)
    {
        var service = new Service();
        service.Lobbies["match"] = Lobby("match", "Match") with { Session = changedSession ? 101ul : 100ul };
        var store = new ResumeLocatorStore(Path.Combine(Path.GetTempPath(), "trackstorm-stale-" + Guid.NewGuid().ToString("N") + ".json"));
        store.Save(new ResumeLocator("match", 100, 2, 1, User(2).Value, 1, User(1).Value));
        try
        {
            using var client = new OnlineLobbyCoordinator(new Provider(service, User(2)), User(2), resumeStore: store);
            client.Tick();
            if (changedSession)
            {
                Assert.That(client.HasRetainedDecision, Is.False);
                Assert.That(client.Active, Is.Null);
                Assert.That(client.CanResumeRetained, Is.False);
                Assert.That(store.Load(User(2).Value), Is.Null, "A rejected session must not preserve a stale hint through cleanup.");
                return;
            }

            using var gateway = new Gateway();
            gateway.ConnectPeer(1);
            var binding = client.AttachTransport(gateway, 1, "Client");
            binding.Driver.Pump(0);
            if (wireMismatch)
            {
                var hosted = new GameVersion(GameVersion.Current.Revision + 1);
                gateway.Receive(1, LobbyCodec.EncodeVersionMismatch(hosted));
                binding.Driver.Pump(0);
                client.Tick();
                Assert.That(client.HasRetainedDecision, Is.False);
                Assert.That(store.Load(User(2).Value), Is.Null);
                Assert.That(client.Status, Is.EqualTo(GameVersion.Current.MismatchMessage(hosted.ToString())));
                return;
            }

            gateway.Receive(9, LobbyCodec.EncodeReservation(100, 2, 1, 1, ReservationResult.Missing));
            gateway.Receive(1, LobbyCodec.EncodeReservation(100, 2, 2, 1, ReservationResult.Missing));
            binding.Driver.Pump(0);
            client.Tick();
            Assert.That(client.RetainedDecision, Is.EqualTo(RetainedSessionDecision.Checking));
            gateway.Receive(1, LobbyCodec.EncodeReservation(100, 2, 1, 1, ReservationResult.Missing));
            binding.Driver.Pump(0);
            client.Tick();
            Assert.That(client.HasRetainedDecision, Is.False);
            Assert.That(store.Load(User(2).Value), Is.Null);
            Assert.That(client.Status, Does.Contain("no longer available"));
        }
        finally
        {
            store.Clear();
        }
    }
}
