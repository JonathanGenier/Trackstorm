using Trackstorm.Client.Development;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Development;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Transport.Tests;

internal sealed partial class OnlineLobbyTests
{
    [Test]
    public void SharedConfigurationConvergesRejectsForgedRequestsAndRetainsSessionOwnership()
    {
        var gateway = new Gateway();
        var host = new LobbyNetworkDriver(gateway, 99, 0, "Host");
        var peers = new List<(ulong Peer, Gateway Gateway, LobbyNetworkDriver Driver)>();
        void Join(ulong peer)
        {
            var remote = new Gateway();
            remote.ConnectPeer(1);
            gateway.ConnectPeer(peer);
            peers.Add((peer, remote, new LobbyNetworkDriver(remote, 0, 1, "Client")));
            Pump(); Pump();
        }
        void Pump()
        {
            foreach (var peer in peers)
            {
                peer.Driver.Pump(0.01);
                foreach (var message in peer.Gateway.Sent) gateway.Receive(peer.Peer, message.Payload.ToArray());
                peer.Gateway.Sent.Clear();
            }
            host.Pump(0.01);
            foreach (var message in gateway.Sent)
                peers.Single(peer => peer.Peer == message.RemotePeerId).Gateway.Receive(1, message.Payload.ToArray());
            gateway.Sent.Clear();
        }
        void Converge()
        {
            for (int i = 0; i < 4; i++) Pump();
            Assert.That(peers.All(peer => peer.Driver.Configuration == host.Configuration), Is.True);
        }
        Join(11); Join(12);
        Assert.That(peers.All(peer => peer.Driver.CanConfigure), Is.True);
        var first = peers[0].Driver;
        var second = peers[1].Driver;
        Assert.That(first.RequestConfiguration(new Dictionary<string, double> { ["vehicle.mass"] = 1200 }, out _), Is.True);
        Assert.That(second.RequestConfiguration(new Dictionary<string, double> { ["items.machine_gun_damage"] = 12 }, out _), Is.True);
        Assert.That(first.Configuration!.Configuration.Vehicle.Mass, Is.EqualTo(GameplayConfiguration.HostedDefaults.Vehicle.Mass), "Sending never changes client gameplay.");
        Converge();
        Assert.That(host.Configuration!.Configuration.Vehicle.Mass, Is.EqualTo(1200));
        Assert.That(host.Configuration.Configuration.Items.MachineGunDamage, Is.EqualTo(12));
        Assert.That(first.ConfigurationResult!.Value.Error, Is.Empty);
        ulong revision = host.Configuration.Revision;
        Assert.That(first.RequestConfiguration(new Dictionary<string, double> { ["vehicle.mass"] = -1, ["items.machine_gun_damage"] = 20 }, out _), Is.True);
        Converge();
        Assert.That(first.ConfigurationResult!.Value.Error, Is.Not.Empty);
        Assert.That(host.Configuration.Revision, Is.EqualTo(revision), "Rejected multi-field transactions are atomic.");
        Join(13); Converge();
        var stamp = DateTimeOffset.Parse("2026-09-26T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        string export = ConfigurationChangesExport.Format(host.Configuration.Configuration, GameVersion.Current.ToString(), stamp);
        Assert.That(peers.Select(peer => ConfigurationChangesExport.Format(peer.Driver.Configuration!.Configuration, GameVersion.Current.ToString(), stamp)), Is.All.EqualTo(export));
        for (int i = 0; i < 20; i++)
        {
            Assert.That(peers[i % 3].Driver.RequestConfiguration(new Dictionary<string, double> { ["vehicle.mass"] = 1000 + i }, out _), Is.True);
            Converge();
        }
        var defaults = GameplayOptions.All.Where(option => option.Group == "Vehicle").ToDictionary(option => option.Key, option => option.Read(GameplayConfiguration.HostedDefaults));
        Assert.That(second.RequestConfiguration(defaults, out _), Is.True);
        Converge();
        Assert.That(host.Configuration.Configuration.Vehicle.Mass, Is.EqualTo(GameplayConfiguration.HostedDefaults.Vehicle.Mass));
        Assert.That(host.Configuration.Configuration.Items.MachineGunDamage, Is.EqualTo(12), "Category reset preserves other groups.");
        Assert.That(first.RequestConfiguration(GameplayOptions.All.ToDictionary(option => option.Key, option => option.Read(GameplayConfiguration.HostedDefaults)), out _), Is.True);
        Converge();
        Assert.That(host.Configuration.Configuration, Is.EqualTo(GameplayConfiguration.HostedDefaults));
        revision = host.Configuration.Revision;
        byte[] edit = ConfigurationRequestCodec.Encode(100, host.State!.Match, new Dictionary<string, double> { ["vehicle.mass"] = 500 });
        byte[] Envelope(ulong epoch, ulong generation) => [(byte)'T', (byte)'D', 1, .. ConnectionEnvelope.Encode(99, generation, edit, epoch)];
        gateway.Receive(11, Envelope(2, 1));
        gateway.Receive(11, Envelope(1, 2));
        gateway.ConnectPeer(44);
        gateway.Receive(44, Envelope(1, 1));
        // A forged client publication cannot replace host state.
        gateway.Receive(11, [(byte)'T', (byte)'D', 1, .. ConnectionEnvelope.Encode(99, 1, [3, .. GameplayConfigurationCodec.Encode(99, new(999, GameplayConfiguration.HostedDefaults))])]);
        Converge();
        Assert.That(host.Configuration.Revision, Is.EqualTo(revision));
        Assert.That(host.RejectedPackets, Is.GreaterThanOrEqualTo(4));
        gateway.Receive(11, Envelope(1, 1));
        Converge();
        revision = host.Configuration.Revision;
        gateway.Receive(11, Envelope(1, 1));
        Converge();
        Assert.That(host.Configuration.Revision, Is.EqualTo(revision), "Repeated requests cannot apply twice.");
    }
}
