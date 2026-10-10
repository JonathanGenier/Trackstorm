using Trackstorm.Core.Sessions;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;
using System.Numerics;

namespace Trackstorm.Core.Tests.Sessions;

[TestFixture]
internal sealed class PracticeCarTests
{
    [Test]
    public void PracticePairReservesTwoSlotsWithoutTransportOrIdentity()
    {
        var lobby = new LobbyAuthority(100, "Host");
        lobby.AddPracticeCar(); lobby.AddPracticeCar();
        Assert.That(lobby.State.Players.Count, Is.EqualTo(3));
        Assert.That(lobby.State.CanStart, Is.True);
        Assert.That(lobby.Capture("host").Subjects.Keys, Is.EqualTo(new[] { 1UL }));
        Assert.That(LobbyCodec.DecodeState(LobbyCodec.EncodeState(lobby.State, 1)).State.Players.Count(player => player.PracticeCar), Is.EqualTo(2));
        for (ulong peer = 10; peer < 15; peer++) { Assert.That(lobby.Join(peer, GameVersion.Current.ToString(), "Human", "subject" + peer), Is.Not.Zero); }
        Assert.That(lobby.Join(15, GameVersion.Current.ToString(), "Overflow"), Is.Zero);
        Assert.That(lobby.Peers.Values, Does.Not.Contain(2UL));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MigrationRetainsCarButOnlyHumansCanVoteOrBecomeHost(bool arena)
    {
        var lobby = new LobbyAuthority(100, "Host");
        lobby.AddPracticeCar();
        ulong human = lobby.Join(10, GameVersion.Current.ToString(), "Successor", "successor");
        var checkpoint = MigrationCheckpointCodec.Decode(MigrationCheckpointCodec.Encode(new MigrationCheckpoint(1, lobby.Capture("host"), null, null)));
        var election = new MigrationElection(checkpoint, new string('A', 64));
        Assert.That(election.Candidate, Is.EqualTo(human));
        Assert.That(election.Vote(2, 100, 1, human, election.Digest), Is.False);
        Assert.That(election.Vote(human, 100, 1, human, election.Digest), Is.True);
        Assert.That(election.Agreed, Is.True);
        if (arena) { lobby.SetReady(10, true); Assert.That(lobby.Start(0, [10]), Is.True); }
        Assert.Throws<ArgumentException>(() => LobbyAuthority.Restore(lobby.Capture("host"), 2, 2));
        var restored = LobbyAuthority.Restore(lobby.Capture("host"), human, 2);
        Assert.That(restored.State.Players.Where(player => player.PracticeCar), Is.EqualTo(lobby.State.Players.Where(player => player.PracticeCar)));
        if (arena) { Assert.That(restored.Return(0), Is.True); }
        Assert.That(restored.State.CanStart, Is.True);
    }

    [Test]
    public void PracticeVehicleCannotBeReboundToANetworkPeer()
    {
        var host = new HostVehicleSession(100);
        host.DrivePracticeCar(2, true);
        host.DrivePracticeCar(2, true);
        Assert.That(host.World.State.Vehicles.Count, Is.EqualTo(2));
        host.DrivePracticeCar(3, true);
        Assert.Throws<InvalidOperationException>(() => host.DrivePracticeCar(4, true));
        Assert.That(host.ResumePlayer(20, 2), Is.False);
        Assert.That(host.Receive(20, 100, []), Is.False);
        host.ExpirePlayer(2);
        Assert.That(host.World.State.Vehicles.Count, Is.EqualTo(2));
    }

    [Test]
    public void RestoredArenaPreservesCarAndResumesHostDriving()
    {
        var lobby = new LobbyAuthority(100, "Host");
        lobby.AddPracticeCar();
        ulong human = lobby.Join(10, GameVersion.Current.ToString(), "Successor", "successor");
        lobby.SetReady(10, true); lobby.Start(0, [10]);
        var host = new HostVehicleSession(lobby.State.Match);
        host.DrivePracticeCar(2, true); host.DrivePracticeCar(3, true); host.JoinPlayer(10, human);
        var resume = new ResumeCheckpoint(new ItemPublication(1, host.Snapshot(), host.Items.Slots, host.Items.Missiles, []), host.World.State.Match!, null, host.Configuration);
        var checkpoint = MigrationCheckpointCodec.Decode(MigrationCheckpointCodec.Encode(new MigrationCheckpoint(1, lobby.Capture("host"), resume, host.CaptureAuthority())));
        var restored = HostVehicleSession.Restore(checkpoint.Arena!, checkpoint.Host!, human);
        restored.DrivePracticeCar(2, true); restored.DrivePracticeCar(3, true);
        Assert.That(restored.World.GetVehicle(2), Is.EqualTo(host.World.GetVehicle(2)));
        restored.Step(default, state => new VehicleObservation(state.Movement.Physics, Vector3.UnitY));
        Assert.That(restored.World.GetVehicle(2).Movement.Throttle, Is.GreaterThan(0));
        Assert.That(restored.World.GetVehicle(1).Movement.Throttle, Is.Zero);
        Assert.That(restored.ResumePlayer(30, 2), Is.False);
    }
}
