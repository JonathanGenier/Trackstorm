using System.Numerics;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Sessions;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Networking;

/// <summary>Complete portable authority continuation and epoch safety.</summary>
[TestFixture]
internal sealed class MigrationTests
{
    /// <summary>The read-only route survives checkpoint encoding but cannot alias the private session key.</summary>
    [Test]
    public void CheckpointRoutingIsOptionalBoundedAndSeparateFromLeaseSession()
    {
        var lobby = Lobby().Capture("host");
        string leaseSession = new('A', 64);
        string routingId = new('B', 64);
        var checkpoint = MigrationCheckpointCodec.Decode(MigrationCheckpointCodec.Encode(new(1, lobby, null, null, leaseSession, routingId)));
        Assert.That(checkpoint.RoutingId, Is.EqualTo(routingId));
        Assert.That(checkpoint.LeaseSession, Is.EqualTo(leaseSession));
        Assert.That(MigrationCheckpointCodec.Decode(MigrationCheckpointCodec.Encode(new(1, lobby, null, null))).RoutingId, Is.Null);
        foreach (string invalid in new[] { string.Empty, new string('B', 63), new string('B', 65), new string('G', 64), leaseSession.ToLowerInvariant() })
        {
            Assert.Throws<ArgumentException>(() => new MigrationCheckpoint(1, lobby, null, null, leaseSession, invalid));
        }

        byte[] oldVersion = MigrationCheckpointCodec.Encode(checkpoint);
        oldVersion[2] = 4;
        Assert.Throws<ArgumentException>(() => MigrationCheckpointCodec.Decode(oldVersion));
    }

    /// <summary>Former hosts remain retained until Return ends the match.</summary>
    /// <param name="resume">Whether the former host returns during the retained match.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void FormerHostReservationLastsThroughMatchAndClearsAtReturn(bool resume)
    {
        var lobby = new LobbyAuthority(100, "Host");
        lobby.Join(10, GameVersion.Current.ToString(), "Client", "client");
        lobby.SetReady(0, true);
        lobby.SetReady(10, true);
        Assert.That(lobby.Start(0, [10]), Is.True);
        var restored = LobbyAuthority.Restore(lobby.Capture("host"), 2, 2);
        restored.AdvanceTime(100000);
        Assert.That(restored.State.Players.Single(player => player.Id == 1).RetainedHost, Is.True);
        restored.AdvanceTime(200000);
        if (resume)
        {
            Assert.That(restored.Resume(50, GameVersion.Current.ToString(), 100, 1, 1, "host"), Is.True);
            Assert.That(restored.State.CurrentHostId, Is.EqualTo(2));
        }

        Assert.That(restored.Return(0), Is.True);
        Assert.That(restored.State.Players.Any(player => player.RetainedHost), Is.False);
        Assert.That(restored.State.Players.Any(player => player.Id == 1), Is.EqualTo(resume));
        if (resume)
        {
            restored.Disconnect(50);
            restored.AdvanceTime(400000);
            Assert.That(restored.State.Players.Any(player => player.Id == 1), Is.False);
        }
        else
        {
            Assert.That(restored.Resume(50, GameVersion.Current.ToString(), 100, 1, 1, "host"), Is.False);
        }
    }

    /// <summary>An ordinary disconnected client survives sequential authority restores without a deadline.</summary>
    [Test]
    public void OrdinaryReservationSurvivesSequentialMigrationUntilReturn()
    {
        var lobby = new LobbyAuthority(100, "Host");
        lobby.Join(10, GameVersion.Current.ToString(), "Successor", "successor");
        ulong player = lobby.Join(20, GameVersion.Current.ToString(), "Disconnected", "client");
        lobby.SetReady(0, true);
        lobby.SetReady(10, true);
        lobby.SetReady(20, true);
        Assert.That(lobby.Start(0), Is.True);
        lobby.Disconnect(20);
        lobby.AdvanceTime(100000);
        var second = LobbyAuthority.Restore(lobby.Capture("host"), 2, 2);
        second.AdvanceTime(200000);
        Assert.That(second.Resume(30, GameVersion.Current.ToString(), 100, 1, 1, "host"), Is.True);
        var third = LobbyAuthority.Restore(second.Capture("successor"), 1, 3);
        third.AdvanceTime(300000);
        Assert.That(third.State.Players.Count, Is.EqualTo(3));
        Assert.That(third.Resume(40, GameVersion.Current.ToString(), 100, player, 1, "wrong"), Is.False);
        Assert.That(third.Resume(40, GameVersion.Current.ToString(), 100, player, 1, "client"), Is.True);
        Assert.That(third.State.Players.Single(value => value.Id == player).Generation, Is.EqualTo(2));
        Assert.That(third.State.CurrentHostId, Is.EqualTo(1));
        Assert.That(third.State.AuthorityEpoch, Is.EqualTo(3));
        third.Disconnect(40);
        Assert.That(third.Return(0), Is.True);
        Assert.That(third.State.Players.Select(value => value.Id), Is.EqualTo(new ulong[] { 1 }));
        Assert.That(third.Capture("host").Subjects.Count, Is.EqualTo(1));
        Assert.That(third.Resume(50, GameVersion.Current.ToString(), 100, player, 2, "client"), Is.False);
    }

    /// <summary>A two-player checkpoint permits exactly one survivor; a larger roster cannot use that exception.</summary>
    [Test]
    public void TwoPlayerLobbyElectionRemovesFormerHostAndSupportsSequentialMigration()
    {
        var lobby = new LobbyAuthority(100, "Host");
        lobby.Join(10, GameVersion.Current.ToString(), "Client", "client");
        lobby.SetReady(0, true);
        lobby.SetReady(10, true);
        var checkpoint = new MigrationCheckpoint(1, lobby.Capture("host"), null, null);
        string digest = MigrationCheckpointCodec.Digest(MigrationCheckpointCodec.Encode(checkpoint));
        var election = new MigrationElection(checkpoint, digest);
        Assert.Throws<InvalidOperationException>(() => election.Commit());
        Assert.That(election.Vote(2, 100, 1, 2, digest), Is.True);
        var replacement = LobbyAuthority.Restore(checkpoint.Lobby, election.Candidate, election.Commit());
        Assert.Throws<InvalidOperationException>(() => election.Commit());
        Assert.That(replacement.State.Session, Is.EqualTo(100));
        Assert.That(replacement.State.Players.Select(player => player.Id), Is.EqualTo(new ulong[] { 2 }));
        Assert.That(replacement.State.Players.All(player => !player.Ready), Is.True);
        Assert.That(replacement.Resume(50, GameVersion.Current.ToString(), 100, 1, 1, "host"), Is.False);
        Assert.That(replacement.Join(50, GameVersion.Current.ToString(), "Former host", "host"), Is.EqualTo(3));
        var second = new MigrationCheckpoint(2, replacement.Capture("client"), null, null);
        string secondDigest = MigrationCheckpointCodec.Digest(MigrationCheckpointCodec.Encode(second));
        var next = new MigrationElection(second, secondDigest);
        Assert.That(next.Candidate, Is.EqualTo(3));
        Assert.That(next.Vote(3, 100, 2, 3, secondDigest), Is.True);
        LobbyAuthority sequential = LobbyAuthority.Restore(second.Lobby, 3, next.Commit());
        Assert.That(sequential.State.AuthorityEpoch, Is.EqualTo(3));
        Assert.That(sequential.State.Players.Select(player => player.Id), Is.EqualTo(new ulong[] { 3 }));

    }

    /// <summary>The full player/projectile population fits the complete external checkpoint budget.</summary>
    [Test]
    public void EightPlayersAndFullProjectilePopulationFitCheckpointBudget()
    {
        var lobby = new LobbyAuthority(100, "Host");
        var host = new HostVehicleSession(101);
        for (ulong id = 2; id <= 8; id++)
        {
            lobby.Join(id * 10, GameVersion.Current.ToString(), "Player" + id, "subject" + id);
            host.JoinPlayer(id * 10, id);
            lobby.SetReady(id * 10, true);
        }

        lobby.SetReady(0, true);
        Assert.That(lobby.Start(0), Is.True);
        host.RegisterSpawns(PrototypeArena.Configuration);
        for (int batch = 0; batch < ItemAuthority.MaximumProjectiles / 8; batch++)
        {
            for (ulong id = 1; id <= 8; id++)
            {
                Assert.That(host.Items.Grant(host.World, id, HeldItem.Missile), Is.True);
                Assert.That(host.UseItem(id == 1 ? 0 : id * 10, 101, 1, host.Items.Slots.Single(slot => slot.Vehicle == id).Token), Is.True);
            }

            host.Step(default, Observe);
        }

        Assert.That(host.Items.Missiles.Count, Is.EqualTo(ItemAuthority.MaximumProjectiles));
        for (ulong id = 1; id <= 8; id++)
        {
            Assert.That(host.Items.Grant(host.World, id, HeldItem.Missile), Is.True);
        }

        var checkpoint = new MigrationCheckpoint(1, lobby.Capture("host"), new ResumeCheckpoint(new ItemPublication(1, host.Snapshot(), host.Items.Slots, host.Items.Missiles, [], host.Spawns!.States), host.World.State.Match!, null, host.Configuration), host.CaptureAuthority());
        byte[] bytes = MigrationCheckpointCodec.Encode(checkpoint);
        Assert.That(MigrationCheckpointCodec.Decode(bytes).Arena!.Items.Missiles.Count, Is.EqualTo(ItemAuthority.MaximumProjectiles));
        TestContext.WriteLine($"Eight-player/full-projectile checkpoint: {bytes.Length} bytes.");
    }

    /// <summary>Authority changes once without depending on arrival order or provider identity ordering.</summary>
    [Test]
    public void ElectionRequiresExactUnanimousSurvivorAgreement()
    {
        var lobby = Lobby();
        var checkpoint = new MigrationCheckpoint(1, lobby.Capture("host"), null, null);
        string digest = MigrationCheckpointCodec.Digest(MigrationCheckpointCodec.Encode(checkpoint));
        var election = new MigrationElection(checkpoint, digest);
        Assert.That(election.Candidate, Is.EqualTo(2));
        Assert.That(election.Vote(3, 100, 1, 2, digest), Is.True);
        Assert.That(election.Vote(1, 100, 1, 2, digest), Is.False);
        Assert.That(election.Vote(2, 100, 2, 2, digest), Is.False);
        Assert.That(election.Vote(2, 100, 1, 3, digest), Is.False);
        Assert.Throws<InvalidOperationException>(() => election.Commit());
        Assert.That(election.Vote(2, 100, 1, 2, digest), Is.True);
        Assert.That(election.Commit(), Is.EqualTo(2));
        Assert.Throws<InvalidOperationException>(() => election.Commit());
    }

    /// <summary>Lobby restore drops the former host, preserves active survivors, and rejects old epochs.</summary>
    [Test]
    public void LobbyRestorePreservesIdentitiesAndRejectsOldAuthority()
    {
        var lobby = Lobby();
        lobby.SetReady(0, true);
        var restored = LobbyAuthority.Restore(lobby.Capture("host"), 2, 2, new Dictionary<ulong, ulong> { [3] = 80 });
        Assert.That(restored.State.Session, Is.EqualTo(100));
        Assert.That(restored.State.CurrentHostId, Is.EqualTo(2));
        Assert.That(restored.State.Players.All(player => !player.Ready), Is.True);
        Assert.That(restored.State.Players.Select(player => player.Id), Is.EqualTo(new ulong[] { 2, 3 }));
        Assert.That(restored.Resume(90, GameVersion.Current.ToString(), 100, 1, 1, "host"), Is.False);
        Assert.That(restored.Join(90, GameVersion.Current.ToString(), "Former host", "host"), Is.EqualTo(4));
        Assert.That(restored.PlayerId(80), Is.EqualTo(3));
        Assert.That(restored.Execute(80, LobbyCommand.Start, 100, 100, SessionPhase.Lobby, false, [80], 2), Is.False);
        Assert.That(restored.Execute(80, LobbyCommand.Ready, 100, 100, SessionPhase.Lobby, true, [80], 1), Is.False);
        Assert.That(restored.Execute(80, LobbyCommand.Ready, 100, 100, SessionPhase.Lobby, true, [80], 2), Is.True);
        byte[] packet = ConnectionEnvelope.Encode(100, 2, [1, 2, 3], 1);
        Assert.Throws<ArgumentException>(() => ConnectionEnvelope.Decode(packet, 100, 2, 2));
    }

    /// <summary>Checkpoint restores all existing gameplay codecs and rejects corrupt bytes before mutation.</summary>
    /// <param name="emptyMap">Whether the active map has no placed pickups.</param>
    /// <param name="heldItem">Identity retained on an ordinary participant.</param>
    [TestCase(false, HeldItem.Wrench)]
    [TestCase(true, HeldItem.Wrench)]
    [TestCase(false, HeldItem.Oil)]
    [TestCase(true, HeldItem.Nitro)]
    public void CompleteCheckpointRoundTripPreservesTokensRngAndWorld(bool emptyMap, HeldItem heldItem)
    {
        var lobby = Lobby();
        lobby.SetReady(0, true);
        lobby.SetReady(10, true);
        lobby.SetReady(20, true);
        Assert.That(lobby.Start(0), Is.True);
        var original = PrototypeArena.Configuration;
        var map = emptyMap ? new ArenaConfiguration(original.Minimum, original.Maximum, original.Players, [], original.Surfaces) : original;
        var host = new HostVehicleSession(lobby.State.Match, arena: map);
        host.JoinPlayer(10, 2);
        host.JoinPlayer(20, 3);
        host.RegisterSpawns(map);
        host.Items.Grant(host.World, 3, heldItem);
        host.Items.Grant(host.World, 3, HeldItem.Wrench);
        Assert.That(host.SwitchItem(20, host.SessionId, 1, 1), Is.True);
        host.Items.Grant(host.World, 2, HeldItem.Missile);
        host.UseItem(10, host.SessionId, 1, host.Items.Slots.Single(slot => slot.Vehicle == 2).Token);
        host.Step(default, Observe);
        Assert.That(host.Items.Missiles.Count, Is.EqualTo(1));
        var world = host.Snapshot();
        var match = host.World.State.Match!;
        var resume = new ResumeCheckpoint(new ItemPublication(2, world, host.Items.Slots, host.Items.Missiles, [], host.Spawns!.States), match, null, host.Configuration);
        var inconsistent = new LobbyRestoreState(lobby.State, 0, 3, lobby.Capture("host").Subjects, new(1, host.Configuration.Configuration));
        Assert.Throws<ArgumentException>(() => new MigrationCheckpoint(1, inconsistent, resume, host.CaptureAuthority()));
        byte[] encoded = MigrationCheckpointCodec.Encode(new MigrationCheckpoint(1, lobby.Capture("host"), resume, host.CaptureAuthority(), map: map));
        if (emptyMap)
        {
            Assert.Throws<ArgumentException>(() => MigrationCheckpointCodec.Decode(encoded, original));
        }

        var decoded = MigrationCheckpointCodec.Decode(encoded, map);
        var replacement = HostVehicleSession.Restore(decoded.Arena!, decoded.Host!, 2, arena: map);
        Assert.That(replacement.World.Arena, Is.SameAs(map));
        Assert.That(replacement.Spawns!.States.Count, Is.EqualTo(map.Items.Count));
        Assert.That(replacement.World.State.Vehicles.Select(v => v.VehicleId), Is.EquivalentTo(new ulong[] { 1, 2, 3 }));
        Assert.That(replacement.Items.Slots, Is.EqualTo(host.Items.Slots));
        Assert.That(replacement.Items.TokenHighWater, Is.EqualTo(host.Items.TokenHighWater));
        Assert.That(replacement.Items.Missiles, Is.EqualTo(host.Items.Missiles));
        Assert.That(replacement.Spawns!.RandomState, Is.EqualTo(host.Spawns.RandomState));
        Assert.That(replacement.Items.Events, Is.Empty);
        Assert.That(replacement.ResumePlayer(50, 1), Is.True);
        Assert.That(replacement.ResumePlayer(51, 1), Is.False);
        Assert.That(replacement.Items.Grant(replacement.World, 2, HeldItem.Wrench), Is.True);
        Assert.That(replacement.Items.Slots.Single(slot => slot.Vehicle == 2).Token, Is.GreaterThan(host.Items.TokenHighWater));
        replacement.Step(default, Observe, (_, _) => 0);
        Assert.That(replacement.World.State.Tick, Is.EqualTo(world.Tick + 1));
        Assert.That(replacement.Items.Events.Count(item => item.Impact), Is.EqualTo(1));
        Assert.That(replacement.Items.Missiles, Is.Empty);
        replacement.Step(default, Observe, (_, _) => 0);
        Assert.That(replacement.Items.Events, Is.Empty, "An already committed missile impact is not replayed.");
        encoded[^1] ^= 1;
        Assert.Throws<ArgumentException>(() => MigrationCheckpointCodec.Decode(encoded));
    }

    /// <summary>Restoration continues the selector stream and cooldowns, rather than restarting from its seed.</summary>
    [Test]
    public void NextPickupMatchesUninterruptedAuthorityAfterRestore()
    {
        var host = new HostVehicleSession(101, randomizeItemSeed: true);
        host.JoinPlayer(10, 2);
        host.RegisterSpawns(PrototypeArena.Configuration);
        int matchSeed = host.Configuration.Configuration.Spawns.Seed;
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["spawns.wrench_weight"] = 3, ["vehicle.acceleration"] = 7 }, out _), Is.True);
        Place(host, 1, "item-01");
        Assert.That(host.Spawns!.TryPickup(host.World, "item-01", 1), Is.True);
        _ = host.ItemSelectionRandom.Next(7);
        var publication = new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], [], host.Spawns.States);
        var restored = HostVehicleSession.Restore(new ResumeCheckpoint(publication, host.World.State.Match!, null, host.Configuration), host.CaptureAuthority(), 2);
        Assert.That(restored.Configuration, Is.EqualTo(host.Configuration));
        Assert.That(restored.Configuration.Configuration.Spawns.Seed, Is.EqualTo(matchSeed));
        Assert.That(restored.Spawns!.RandomState, Is.EqualTo(host.Spawns.RandomState));
        Assert.That(restored.ItemSelectionRandom.Next(23), Is.EqualTo(host.ItemSelectionRandom.Next(23)), "The first generic authoritative draw after migration must continue exactly.");
        Assert.That(restored.Spawns!.States, Is.EqualTo(host.Spawns.States));
        foreach (var authority in new[] { host, restored })
        {
            Place(authority, 2, "item-02");
            Assert.That(authority.Spawns!.TryPickup(authority.World, "item-02", 2), Is.True);
            ulong random = authority.Spawns.RandomState;
            Assert.That(authority.Spawns.TryPickup(authority.World, "item-02", 2), Is.False);
            Assert.That(authority.Spawns.RandomState, Is.EqualTo(random));
        }

        Assert.That(restored.Items.Slots, Is.EqualTo(host.Items.Slots));
        Assert.That(restored.Spawns.RandomState, Is.EqualTo(host.Spawns.RandomState));
        Assert.That(restored.Spawns.Revision, Is.EqualTo(host.Spawns.Revision));

        var nextMatch = new HostVehicleSession(102, configuration: restored.Configuration.Configuration,
            configurationRevision: restored.Configuration.Revision, randomizeItemSeed: true);
        nextMatch.RegisterSpawns(PrototypeArena.Configuration);
        Assert.That(nextMatch.Configuration.Revision, Is.EqualTo(restored.Configuration.Revision + 1));
        Assert.That(nextMatch.ItemSelectionRandom.State, Is.EqualTo(unchecked((ulong)nextMatch.Configuration.Configuration.Spawns.Seed)));
        Assert.That(nextMatch.Spawns!.States.All(spawn => spawn.Available), Is.True);
        Assert.That(nextMatch.Spawns.Revision, Is.Zero);
    }

    /// <summary>Lobby tuning is session state and survives both serialization and successive authority transfers.</summary>
    [Test]
    public void LobbyConfigurationSurvivesSuccessiveMigrationAndRejectsClientEdits()
    {
        var lobby = new LobbyAuthority(100, "Host");
        lobby.Join(10, GameVersion.Current.ToString(), "Client", "client");
        var edits = new Dictionary<string, double> { ["vehicle.acceleration"] = 7, ["spawns.seed"] = 42 };
        Assert.That(lobby.TryConfigure(10, edits, out _), Is.False);
        Assert.That(lobby.TryConfigure(0, edits, out _), Is.True);
        var checkpoint = MigrationCheckpointCodec.Decode(MigrationCheckpointCodec.Encode(new MigrationCheckpoint(1, lobby.Capture("host"), null, null)));
        var successor = LobbyAuthority.Restore(checkpoint.Lobby, 2, 2);
        Assert.That(successor.Configuration, Is.EqualTo(lobby.Configuration));
        Assert.That(successor.Resume(50, GameVersion.Current.ToString(), 100, 1, 1, "host"), Is.False);
        Assert.That(successor.Join(50, GameVersion.Current.ToString(), "Former host", "host"), Is.EqualTo(3));
        Assert.That(successor.TryConfigure(50, edits, out _), Is.False);
        Assert.That(successor.TryConfigure(0, new Dictionary<string, double> { ["vehicle.acceleration"] = 9 }, out _), Is.True);
        var next = LobbyAuthority.Restore(successor.Capture("client"), 3, 3);
        Assert.That(next.Configuration, Is.EqualTo(successor.Configuration));
        Assert.That(next.Configuration.Revision, Is.EqualTo(2));
    }

    /// <summary>A restored lethal boundary respawns once without recounting the kill or finishing again.</summary>
    [Test]
    public void RestoredDeathPreservesAttributionScoreAndRespawnDeadline()
    {
        var host = new HostVehicleSession(101, respawnConfiguration: new RespawnConfiguration { DelayTicks = 4 }, matchConfiguration: new MatchConfiguration { CountdownTicks = 1, KillTarget = 1 });
        host.JoinPlayer(10, 2);
        host.JoinPlayer(20, 3);
        host.Step(default, Observe);
        host.Step(default, Observe);
        ulong tick = host.World.State.Tick + 1;
        var frame = new InputFrame(tick, 0, 0, 0, 0, 0, 0);
        host.World.Step(frame, host.World.State.Vehicles.Select(vehicle => new VehicleStepRequest(vehicle.VehicleId, frame, Observe(vehicle), vehicle.VehicleId == 3 ? [new VehicleEffectRequest(new DamageEffect(100, Vector3.Zero, Vector3.Zero), new DamageContext("missile", 2, "migration-test"))] : [])).ToArray());
        var match = host.World.State.Match!;
        var boundary = new MatchState(match.Tick, match.Revision, match.KillTarget, match.Phase, match.CountdownAtTick, match.Winner, match.Players);
        var dead = host.World.GetVehicle(3);
        var checkpoint = new ResumeCheckpoint(new ItemPublication(1, host.Snapshot(), [], [], []), boundary, null, host.Configuration);
        var restored = HostVehicleSession.Restore(ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(checkpoint)), host.CaptureAuthority(), 2);
        Assert.That(restored.World.GetVehicle(3).Damage, Is.EqualTo(dead.Damage));
        Assert.That(restored.World.GetVehicle(3).RespawnAtTick, Is.EqualTo(dead.RespawnAtTick));
        Assert.That(restored.World.State.Match!.Changes, Is.Empty);
        for (int i = 0; i < 6; i++)
        {
            restored.Step(default, Observe);
        }

        Assert.That(restored.World.GetVehicle(3).LifeId, Is.EqualTo(dead.LifeId + 1));
        Assert.That(restored.World.State.Match!.Winner, Is.EqualTo(2));
        Assert.That(restored.World.State.Match.Players.Single(player => player.Player == 2).Kills, Is.EqualTo(1));
        Assert.That(restored.World.State.Match.Players.Single(player => player.Player == 3).Deaths, Is.EqualTo(1));
        Assert.That(restored.World.State.Match.Revision, Is.EqualTo(boundary.Revision));
    }

    private static void Place(HostVehicleSession host, ulong id, string marker)
    {
        var world = host.World;
        var pose = new VehiclePhysicsState(PrototypeArena.Configuration.Items.Single(spawn => spawn.Id == marker).Position, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        var states = world.State.Vehicles.Select(state => state.VehicleId != id ? state : new VehicleSnapshot(id, state.LifeId, new VehicleState(world.State.Tick, pose, false, false, 0, 0), state.Damage, pose));
        world.Restore(new SimulationState(world.State.Tick, world.State.LastInput, states, world.State.Match));
    }

    private static LobbyAuthority Lobby()
    {
        var lobby = new LobbyAuthority(100, "Host");
        lobby.Join(10, GameVersion.Current.ToString(), "Second", "second");
        lobby.Join(20, GameVersion.Current.ToString(), "Third", "third");
        return lobby;
    }

    private static VehicleObservation Observe(VehicleSnapshot state) => new(new VehiclePhysicsState(state.Movement.Physics.Position, Quaternion.Identity, state.Movement.Physics.LinearVelocity, Vector3.Zero), Vector3.UnitY);
}
