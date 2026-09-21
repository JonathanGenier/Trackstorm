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
    /// <summary>Ordinary startup browses normally and never invokes retained-session lookup or membership recovery.</summary>
    [Test]
    public void StartupWithoutLocatorNeverRequestsRetainedResume()
    {
        var service = new Service();
        service.Lobbies["available"] = Lobby("available", "Available");
        using var client = new OnlineLobbyCoordinator(new Provider(service, User(2)), User(2));

        client.Tick();
        client.Refresh();

        Assert.That(client.Active, Is.Null);
        Assert.That(client.HasRetainedDecision, Is.False);
        Assert.That(service.LookupRequests, Is.Zero);
        Assert.That(service.ResumeRequests, Is.Zero);
        Assert.That(client.Browser.Rows.Select(row => row.Id), Does.Contain("available"));
    }

    /// <summary>A locator for an ended lobby is retired by read-only lookup without changing EOS service state.</summary>
    [Test]
    public void MissingStartupLobbyClearsHintWithoutMembershipOrOfflineStatus()
    {
        var service = new Service();
        service.Lobbies["available"] = Lobby("available", "Available");
        var store = new ResumeLocatorStore(Path.Combine(Path.GetTempPath(), "trackstorm-missing-startup-" + Guid.NewGuid().ToString("N") + ".json"));
        store.Save(new ResumeLocator("ended", 999, 2, 1, User(2).Value, 1, User(1).Value));
        try
        {
            using var client = new OnlineLobbyCoordinator(new Provider(service, User(2)), User(2), resumeStore: store);
            client.Tick();
            client.Refresh();

            Assert.That(client.Active, Is.Null);
            Assert.That(client.HasRetainedDecision, Is.False);
            Assert.That(client.Status, Is.EqualTo("Previous session is no longer available."));
            Assert.That(client.Status, Does.Not.Contain("EOS").IgnoreCase);
            Assert.That(store.Load(User(2).Value), Is.Null);
            Assert.That(service.LookupRequests, Is.EqualTo(1));
            Assert.That(service.ResumeRequests, Is.Zero);
            Assert.That(client.Browser.Rows.Select(row => row.Id), Does.Contain("available"));
        }
        finally
        {
            store.Clear();
        }
    }

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
            Assert.That(client.ShowsRetainedDecision, Is.False, "A local locator is not authoritative enough to own the UI.");
            client.Tick();
            Assert.That(client.Active, Is.Null, "Startup lookup must not restore EOS membership.");
            Assert.That(service.LookupRequests, Is.EqualTo(1));
            Assert.That(service.ResumeRequests, Is.Zero);
            client.ResumeRetained();
            Assert.That(service.ResumeRequests, Is.EqualTo(1), "Membership recovery requires an explicit validation action.");
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
            Assert.That(client.ShowsRetainedDecision, Is.True, "Authority confirmation must expose the player decision.");
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
            Assert.That(view.Rows[0], Is.EqualTo(new StandingsRow(2, 1, "Retained winner", 0, 5, 0, "--", true, false, false)));
            if (dropReply)
            {
                clock.Advance(21);
                client.Tick();
                Assert.That(client.RetainedDecision, Is.EqualTo(RetainedSessionDecision.Failed));
                Assert.That(client.ShowsRetainedDecision, Is.False, "A failed validation must return control to the lobby UI.");
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

    /// <summary>A version mismatch preserves both reservation and locator so a compatible restart can reclaim the same player.</summary>
    [Test]
    public void VersionMismatchPreservesLocatorAndCompatibleRetryReclaimsSamePlayer()
    {
        var service = new Service();
        using var host = service.Coordinator(1);
        using var original = service.Coordinator(2);
        host.Create("Version retry", LobbyAccess.Public, null);
        original.Refresh();
        original.Join(host.Active!.Id);
        using var hostGateway = new Gateway();
        var hosting = host.AttachTransport(hostGateway, 0, "Host");
        hostGateway.ConnectPeer(20);
        Assert.That(hosting.AuthorizePeer(20, User(2), null), Is.True);
        hostGateway.ReceiveJoin(20, "Retained player");
        hosting.Driver.Pump(0);
        var authority = hosting.Driver.Authority!;
        authority.SetReady(0, true);
        authority.SetReady(20, true);
        Assert.That(authority.Start(0), Is.True);
        var vehicles = new VehicleNetworkDriver(hostGateway, authority.State.Match, lobby: hosting.Driver);
        vehicles.Advance(default, state => new(state.Movement.Physics, System.Numerics.Vector3.UnitY));
        hostGateway.Disconnect(20);
        hosting.Driver.Pump(0);
        vehicles.Advance(default, state => new(state.Movement.Physics, System.Numerics.Vector3.UnitY));
        original.Leave();

        var locator = new ResumeLocator(host.Active.Id, host.Active.Session, 2, 1, User(2).Value, 1, User(1).Value);
        var store = new ResumeLocatorStore(Path.Combine(Path.GetTempPath(), "trackstorm-version-retry-" + Guid.NewGuid().ToString("N") + ".json"));
        store.Save(locator);
        try
        {
            ulong revision = authority.State.Revision;
            LobbySnapshot retained = authority.State;
            using (var incompatible = new OnlineLobbyCoordinator(new Provider(service, User(2)), User(2), resumeStore: store))
            {
                incompatible.Tick();
                Assert.That(incompatible.Active, Is.Null);
                Assert.That(service.ResumeRequests, Is.Zero, "Startup lookup must not rejoin the saved EOS lobby.");
                incompatible.ResumeRetained();
                using var incompatibleGateway = new Gateway();
                incompatibleGateway.ConnectPeer(1);
                var binding = incompatible.AttachTransport(incompatibleGateway, 1, "Client");
                binding.Driver.Pump(0);
                var hosted = new GameVersion(GameVersion.Current.Release, GameVersion.Current.Revision + 1);
                incompatibleGateway.Receive(1, LobbyCodec.EncodeVersionMismatch(hosted));
                binding.Driver.Pump(0);
                incompatible.Tick();

                Assert.That(binding.Driver.State, Is.Null, "An incompatible client cannot bind gameplay state.");
                Assert.That(incompatible.RetainedDecision, Is.EqualTo(RetainedSessionDecision.Failed));
                Assert.That(incompatible.Status, Is.EqualTo(GameVersion.Current.MismatchMessage(hosted.ToString())));
                Assert.That(store.Load(User(2).Value), Is.EqualTo(locator));
                Assert.That(authority.State, Is.SameAs(retained));
                Assert.That(authority.State.Revision, Is.EqualTo(revision));
                Assert.That(authority.HasReservation(locator.Session, locator.Player, locator.Generation, locator.Identity), Is.True);
                Assert.That(vehicles.Host!.World.State.Vehicles.Select(vehicle => vehicle.VehicleId), Is.EquivalentTo(new ulong[] { 1, 2 }));
            }

            using var compatible = new OnlineLobbyCoordinator(new Provider(service, User(2)), User(2), resumeStore: store);
            Assert.That(compatible.RetainedDecision, Is.EqualTo(RetainedSessionDecision.Checking));
            compatible.Tick();
            Assert.That(compatible.Active, Is.Null);
            int membershipRequests = service.ResumeRequests;
            compatible.ResumeRetained();
            Assert.That(service.ResumeRequests, Is.EqualTo(membershipRequests + 1));
            using var compatibleGateway = new Gateway();
            compatibleGateway.ConnectPeer(1);
            var returned = compatible.AttachTransport(compatibleGateway, 1, "Changed name");
            hostGateway.ConnectPeer(31);
            Assert.That(hosting.AuthorizePeer(31, User(2), null), Is.True);
            returned.Driver.Pump(0);
            hostGateway.Receive(31, compatibleGateway.Sent.Last().Payload.ToArray());
            hosting.Driver.Pump(0);
            compatibleGateway.Receive(1, hostGateway.Sent.Last(message => message.RemotePeerId == 31 && LobbyCodec.IsReservation(message.Payload.Span)).Payload.ToArray());
            returned.Driver.Pump(0);
            compatible.Tick();
            Assert.That(compatible.RetainedDecision, Is.EqualTo(RetainedSessionDecision.Choose));
            compatible.DecideRetained(true);
            returned.Driver.Pump(0);
            hostGateway.Receive(31, compatibleGateway.Sent.Last().Payload.ToArray());
            hosting.Driver.Pump(0);

            Assert.That(authority.PlayerId(31), Is.EqualTo(locator.Player));
            Assert.That(authority.State.Players.Count, Is.EqualTo(2));
            Assert.That(authority.State.Players.Count(player => player.Id == locator.Player), Is.EqualTo(1));
            Assert.That(authority.State.Players.Single(player => player.Id == locator.Player).Generation, Is.EqualTo(2));
            compatibleGateway.ReceiveState(1, authority.State, locator.Player);
            returned.Driver.Pump(0);
            Assert.That(returned.Driver.LocalPlayerId, Is.EqualTo(locator.Player));
            Assert.That(returned.Driver.State!.Players.Select(player => player.Id), Is.EquivalentTo(new ulong[] { 1, 2 }));
            vehicles.Advance(default, state => new(state.Movement.Physics, System.Numerics.Vector3.UnitY));
            Assert.That(vehicles.Host!.World.State.Vehicles.Select(vehicle => vehicle.VehicleId), Is.EquivalentTo(new ulong[] { 1, 2 }));
            Assert.That(vehicles.Host.World.State.Vehicles.Select(vehicle => vehicle.VehicleId).Distinct().Count(), Is.EqualTo(2));
            var standings = MatchStandingsView.From(authority.State, vehicles.Host.World.State.Match!, locator.Player, InputButtons.None, _ => 42);
            Assert.That(standings.Rows.Select(row => row.PlayerId), Is.EquivalentTo(new ulong[] { 1, 2 }));
            Assert.That(standings.Rows.Count(row => row.PlayerId == locator.Player), Is.EqualTo(1));
        }
        finally
        {
            store.Clear();
        }
    }

    /// <summary>A stale claim is cleared only by the current host, while foreign/replayed results cannot unlock the browser.</summary>
    /// <param name="changedSession">Whether membership metadata already proves this is a replacement session.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void StaleReservationReturnsToBrowserAfterBoundHostResponse(bool changedSession)
    {
        var service = new Service();
        service.Lobbies["match"] = Lobby("match", "Match") with { Session = changedSession ? 101ul : 100ul };
        var store = new ResumeLocatorStore(Path.Combine(Path.GetTempPath(), "trackstorm-stale-" + Guid.NewGuid().ToString("N") + ".json"));
        store.Save(new ResumeLocator("match", 100, 2, 1, User(2).Value, 1, User(1).Value));
        try
        {
            using var client = new OnlineLobbyCoordinator(new Provider(service, User(2)), User(2), resumeStore: store);
            Assert.That(client.ShowsRetainedDecision, Is.False, "A stale local locator must not expose the decision before validation.");
            client.Tick();
            if (changedSession)
            {
                Assert.That(client.HasRetainedDecision, Is.False);
                Assert.That(client.Active, Is.Null);
                Assert.That(client.CanResumeRetained, Is.False);
                Assert.That(store.Load(User(2).Value), Is.Null, "A rejected session must not preserve a stale hint through cleanup.");
                return;
            }

            client.ResumeRetained();
            using var gateway = new Gateway();
            gateway.ConnectPeer(1);
            var binding = client.AttachTransport(gateway, 1, "Client");
            binding.Driver.Pump(0);
            gateway.Receive(9, LobbyCodec.EncodeReservation(100, 2, 1, 1, ReservationResult.Missing));
            gateway.Receive(1, LobbyCodec.EncodeReservation(100, 2, 2, 1, ReservationResult.Missing));
            binding.Driver.Pump(0);
            client.Tick();
            Assert.That(client.RetainedDecision, Is.EqualTo(RetainedSessionDecision.Checking));
            gateway.Receive(1, LobbyCodec.EncodeReservation(100, 2, 1, 1, ReservationResult.Missing));
            binding.Driver.Pump(0);
            client.Tick();
            Assert.That(client.HasRetainedDecision, Is.False);
            Assert.That(client.ShowsRetainedDecision, Is.False);
            Assert.That(store.Load(User(2).Value), Is.Null);
            Assert.That(client.Status, Does.Contain("no longer available"));
        }
        finally
        {
            store.Clear();
        }
    }
}
