using Trackstorm.Client.Networking;
using Trackstorm.Client.Online;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Transport.Tests;

/// <summary>Active discovery and the existing Public/Locked access boundary.</summary>
internal sealed partial class OnlineLobbyTests
{
    /// <summary>Quit/Leave during incomplete fresh bootstrap cannot manufacture a resume reservation.</summary>
    [Test]
    public void IncompleteFreshBootstrapDoesNotSaveResumeLocator()
    {
        var store = new ResumeLocatorStore(Path.Combine(Path.GetTempPath(), "trackstorm-fresh-" + Guid.NewGuid().ToString("N") + ".json"));
        var service = new Service();
        using var host = service.Coordinator(1);
        host.Create("Active", LobbyAccess.Public, null);
        try
        {
            using var client = new OnlineLobbyCoordinator(new Provider(service, User(2)), User(2), new Clock(), store);
            client.Refresh();
            client.Join(host.Active!.Id);
            using var wire = new Gateway();
            wire.ConnectPeer(1);
            var binding = client.AttachTransport(wire, 1, "New");
            var state = new LobbySnapshot(host.Active.Session, 2, host.Active.Session + 1, SessionPhase.Arena, [new(1, "Host", false), new(2, "New", false)]);
            wire.Receive(1, LobbyCodec.EncodeState(state, 2, activated: false));
            binding.Driver.Pump(0);
            Assert.That(binding.Driver.CanResume, Is.False);
            client.Tick();
            client.PreserveResumeOnShutdown();
            client.Leave();
            Assert.That(store.Load(User(2).Value), Is.Null);
            Assert.That(client.CanResumeRetained, Is.False);
        }
        finally
        {
            store.Clear();
        }
    }

    /// <summary>Active lobbies remain discoverable; only Locked access adds the established credential gate.</summary>
    /// <param name="access">Existing access mode.</param>
    [TestCase(LobbyAccess.Public)]
    [TestCase(LobbyAccess.Locked)]
    public void ActiveMatchUsesNormalDiscoveryAndAutomaticAccessGate(LobbyAccess access)
    {
        var service = new Service();
        using var host = service.Coordinator(1);
        using var joining = service.Coordinator(2);
        host.Create("Active arena", access, "test-code");
        using var wire = new Gateway();
        var binding = host.AttachTransport(wire, 0, "Host");
        binding.Driver.Authority!.SetReady(0, true);
        Assert.That(binding.Driver.Authority.Start(0), Is.True);
        var arena = new VehicleNetworkDriver(wire, binding.Driver.State!.Match, lobby: binding.Driver);
        binding.Driver.Pump(0);
        host.Tick();
        joining.Refresh();
        Assert.That(joining.Browser.Rows.Single().Joinable, Is.True);
        if (access == LobbyAccess.Locked)
        {
            joining.Join(host.Active!.Id, "wrong-code");
            Assert.That(joining.Active, Is.Null);
        }

        joining.Join(host.Active!.Id, access == LobbyAccess.Locked ? "test-code" : null);
        Assert.That(joining.Active, Is.Not.Null);
        wire.ConnectPeer(50);
        if (access == LobbyAccess.Locked)
        {
            Assert.That(binding.AuthorizePeer(50, User(2), "wrong-code"), Is.False);
            Assert.That(binding.Driver.State.Players.Count, Is.EqualTo(1));
            wire.ConnectPeer(50);
        }

        Assert.That(binding.AuthorizePeer(50, User(2), access == LobbyAccess.Locked ? "test-code" : null), Is.True);
        Assert.That(binding.AuthorizePeer(50, User(2), null), Is.True, "A duplicate authenticated callback preserves the validated binding.");
        wire.ReceiveJoin(50, "New player");
        wire.ReceiveJoin(50, "Duplicate callback");
        binding.Driver.Pump(0);
        Assert.That(binding.Driver.State.Players.Count, Is.EqualTo(2));
        Assert.That(binding.Driver.Authority.IsPendingJoin(50), Is.True);
        Assert.That(binding.PlayerIds[User(2)], Is.EqualTo(2));
        Assert.That(arena.Host!.World.State.Vehicles.Count, Is.EqualTo(1));

        for (ulong peer = 60; peer < 66; peer++)
        {
            binding.Driver.Authority.Join(peer, GameVersion.Current.ToString(), "Reserved", $"subject-{peer}");
        }

        host.Tick();
        Assert.That(host.Active.Open, Is.False, "Core occupancy hides a full session even when EOS membership has room.");
        binding.Driver.Authority.Disconnect(60);
        host.Tick();
        Assert.That(host.Active.Open, Is.True, "Aborted pending admission releases authoritative capacity.");
        binding.Driver.Authority.AdmissionOpen = false;
        host.Tick();
        Assert.That(host.Active.Open, Is.False, "Closing/terminal admission is reflected in discovery.");
    }
}
