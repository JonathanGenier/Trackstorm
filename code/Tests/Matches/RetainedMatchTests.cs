using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Sessions;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Matches;

/// <summary>Session reservations and authoritative scoring share the complete arena/results lifetime.</summary>
[TestFixture]
internal sealed class RetainedMatchTests
{
    /// <summary>Loss and Leave preserve scored participants through late resume, Finished and the next generation.</summary>
    /// <param name="intentional">Whether the player explicitly leaves.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void RetentionSurvivesScoringResumeAndFinishedUntilNextArena(bool intentional)
    {
        var lobby = new LobbyAuthority(100, "Host");
        for (ulong peer = 2; peer <= 8; peer++)
        {
            Assert.That(lobby.Join(peer, GameVersion.Current.ToString(), $"Player {peer}", $"subject-{peer}"), Is.EqualTo(peer));
            lobby.SetReady(peer, true);
        }

        lobby.SetReady(0, true);
        Assert.That(lobby.Start(0), Is.True);
        var host = new HostVehicleSession(lobby.State.Match, matchConfiguration: new() { CountdownTicks = 1 });
        foreach (var peer in lobby.Peers)
        {
            host.JoinPlayer(peer.Key, peer.Value);
        }

        host.Step(default, state => new(state.ObservedPhysics, Vector3.UnitY));
        host.Step(default, state => new(state.ObservedPhysics, Vector3.UnitY));
        Assert.That(host.World.State.Match!.Phase, Is.EqualTo(MatchPhase.Active));
        Score(host, 3, 2);
        Score(host, 2, 1);
        PlayerScore retained = host.World.State.Match!.Players.Single(score => score.Player == 2);
        Assert.That((retained.Kills, retained.Deaths), Is.EqualTo((1, 1)));
        MatchStanding rank = MatchRanking.Create(host.World.State.Match, lobby.State.Players.Select(player => player.Id)).Single(row => row.PlayerId == 2);
        Assert.That(intentional ? lobby.Remove(2) : lobby.Disconnect(2), Is.True);
        host.Suspend(2);
        lobby.AdvanceTime(1000000);
        Assert.That(lobby.FindPlayer("subject-2"), Is.EqualTo(2));
        Assert.That(lobby.Join(20, GameVersion.Current.ToString(), "Replacement", "new-subject"), Is.Zero);
        Assert.That(host.CanJoin, Is.False);
        Assert.That(host.World.State.Match.Players.Single(score => score.Player == 2), Is.EqualTo(retained));
        Assert.That(MatchRanking.Create(host.World.State.Match, lobby.State.Players.Select(player => player.Id)).Single(row => row.PlayerId == 2), Is.EqualTo(rank));
        ulong before = host.World.State.Tick;
        host.Step(default, state => new(state.ObservedPhysics, Vector3.UnitY));
        Assert.That(host.World.State.Tick, Is.EqualTo(before + 1), "Remaining players never wait for resume.");
        Score(host, 4, 1);
        Assert.That(host.World.State.Match.Players.Single(score => score.Player == 1).Kills, Is.EqualTo(2));
        Assert.That(lobby.Resume(20, GameVersion.Current.ToString(), 100, 2, 1, "subject-2"), Is.True);
        Assert.That(host.ResumePlayer(20, 2), Is.True);
        Assert.That(lobby.State.Players.Count(player => player.Id == 2), Is.EqualTo(1));
        Assert.That(host.World.State.Match.Players.Single(score => score.Player == 2), Is.EqualTo(retained));
        Assert.That(MatchRanking.Create(host.World.State.Match, lobby.State.Players.Select(player => player.Id)).Single(row => row.PlayerId == 2), Is.EqualTo(rank));
        lobby.Disconnect(20);
        host.Suspend(20);
        foreach (ulong victim in new ulong[] { 5, 6, 7 })
        {
            Score(host, victim, 1);
        }

        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["match.duration_ticks"] = host.World.State.Tick - host.World.State.Match!.ActiveStartedAtTick!.Value + 1 }, out _), Is.True);
        host.Step(default, state => new(state.ObservedPhysics, Vector3.UnitY));
        MatchState final = host.World.State.Match;
        FinalMatchResults results = final.FinalResults!;
        Assert.That((final.Phase, final.KillTarget, final.Winner), Is.EqualTo((MatchPhase.Finished, 5, 1ul)));
        Assert.That(final.Players.Single(score => score.Player == 2), Is.EqualTo(retained));
        Assert.That(MatchRanking.Create(final, lobby.State.Players.Select(player => player.Id)).Count, Is.EqualTo(8));
        lobby.AdvanceTime(2000000);
        Assert.That(lobby.Resume(21, GameVersion.Current.ToString(), 100, 2, 2, "subject-2"), Is.True, "Finished still owns the reservation.");
        Assert.That(host.ResumePlayer(21, 2), Is.True);
        host.Step(default, state => new(state.ObservedPhysics, Vector3.UnitY));
        Assert.That(host.World.State.Match, Is.SameAs(final));
        Assert.That(host.World.State.Match!.FinalResults, Is.SameAs(results));
        Assert.That(results.Standings.Single(row => row.PlayerId == 2).Kills, Is.EqualTo(retained.Kills));
        lobby.Disconnect(21);
        host.Suspend(21);
        Assert.That(lobby.Return(0), Is.True);
        Assert.That(lobby.FindPlayer("subject-2"), Is.Zero);
        Assert.That(lobby.Resume(22, GameVersion.Current.ToString(), 100, 2, 3, "subject-2"), Is.False);
        lobby.SetReady(0, true);
        foreach (ulong peer in lobby.Peers.Keys)
        {
            lobby.SetReady(peer, true);
        }

        Assert.That(lobby.Start(0), Is.True);
        var next = new HostVehicleSession(lobby.State.Match);
        foreach (var peer in lobby.Peers)
        {
            next.JoinPlayer(peer.Key, peer.Value);
        }

        Assert.That(next.SessionId, Is.GreaterThan(host.SessionId));
        Assert.That(next.World.State.Match!.Players.Any(score => score.Player == 2), Is.False);
        Assert.That(next.World.State.Match.FinalResults, Is.Null);
        Assert.That(results.Standings.Count, Is.EqualTo(8), "An Application Flow handoff survives session Return and a new match.");
        Assert.That(next.World.State.Match.Players.All(score => score.Kills == 0 && score.Deaths == 0 && score.Wins == 0 && score.ProcessedLife == 0), Is.True);
        Assert.That(MatchRanking.Create(next.World.State.Match, lobby.State.Players.Select(player => player.Id)).Select(row => row.PlayerId), Is.EqualTo(new ulong[] { 1, 3, 4, 5, 6, 7, 8 }));
        Assert.That(lobby.Join(22, GameVersion.Current.ToString(), "Returning", "subject-2"), Is.GreaterThan(8));
    }

    private static void Score(HostVehicleSession host, ulong victim, ulong killer)
    {
        var world = host.World;
        ulong tick = world.State.Tick + 1;
        var frame = new InputFrame(tick, 0, 0, 0, 0, 0, 0);
        world.Step(frame, world.State.Vehicles.Select(state => new VehicleStepRequest(state.VehicleId, frame, new VehicleObservation(state.ObservedPhysics, Vector3.UnitY), state.VehicleId == victim ? [new VehicleEffectRequest(new DamageEffect(1000, Vector3.Zero, Vector3.Zero), new DamageContext("missile", killer, "retention test"))] : [])).ToArray());
    }
}
