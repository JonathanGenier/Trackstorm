using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Sessions;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Transport.Tests;

/// <summary>Application-owned teardown and fresh match generations over a retained session.</summary>
internal sealed partial class VehicleNetworkDriverTests
{
    /// <summary>Three real scoring cycles reset match state while transport, identity and host tuning survive Return.</summary>
    [Test]
    public void ThreeFinishedCyclesDisposeMatchStateAndKeepSessionOwnership()
    {
        using var wire = new DriverGateway();
        var lobby = StartJoinHost(wire);
        var retainedResults = new List<FinalMatchResults>();
        for (int cycle = 0; cycle < 3; cycle++)
        {
            ulong generation = lobby.State!.Match;
            var driver = new VehicleNetworkDriver(wire, generation, lobby: lobby, applicationEntry: true);
            var world = driver.Host!.World;
            Assert.That(world.State.Tick, Is.Zero);
            Assert.That(world.MatchEntry, Is.Null);
            Assert.That(world.State.Match!.Phase, Is.EqualTo(MatchPhase.Waiting));
            Assert.That(world.State.Match.CountdownAtTick, Is.Null);
            Assert.That(world.State.Match.Winner, Is.Null);
            Assert.That(world.State.Match.Players.All(row => row.Kills == 0 && row.Deaths == 0 && row.Wins == 0 && row.ProcessedLife == 0), Is.True);
            Assert.That(world.State.Vehicles.All(vehicle => vehicle.LifeId == 1 && vehicle.CanInteract && vehicle.RespawnAtTick is null), Is.True);
            Assert.That(driver.Host.Items.Slots.All(slot => slot.Item == HeldItem.None), Is.True);
            Assert.That(driver.Host.Items.Missiles, Is.Empty);
            Assert.That(driver.FinalResults, Is.Null);
            Assert.That(driver.EntryReady, Is.False);
            ReleaseEntry(wire, lobby, driver);
            Assert.That(driver.TryConfigure(new Dictionary<string, double> { ["match.kill_target"] = 1 }, out _), Is.True);
            while (driver.Match!.Phase != MatchPhase.Active)
            {
                driver.Advance(default, Observe);
            }

            int finishes = 0;
            driver.MatchReceived += match => finishes += match.Phase == MatchPhase.Finished ? 1 : 0;
            Assert.That(driver.GiveDeveloperItem(HeldItem.Missile), Is.True);
            Assert.That(driver.RequestItemUse(), Is.True);
            driver.Advance(default, Observe);
            var frame = new InputFrame(world.State.Tick + 1, 0, 0, 0, 0, 0, 0);
            world.Step(frame, world.State.Vehicles.Select(vehicle => new VehicleStepRequest(vehicle.VehicleId, frame, Observe(vehicle), vehicle.VehicleId == 2
                ? [new VehicleEffectRequest(new DamageEffect(10000, System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero), new DamageContext("missile", 1, "finish cycle"))] : [])).ToArray());
            driver.Advance(default, Observe);
            FinalMatchResults result = driver.FinalResults!;
            Assert.That(result.Outcome.Winner, Is.EqualTo(1));
            Assert.That(result.Standings, Is.EqualTo(new FinalMatchStanding[] { new(1, 1, 1, 0, 1), new(2, 2, 0, 1, 0) }));
            for (int tick = 0; tick < 200; tick++)
            {
                driver.Advance(Drive(), Observe);
            }

            Assert.That(finishes, Is.EqualTo(1));
            Assert.That(driver.FinalResults, Is.SameAs(result));
            Assert.That(lobby.State.Phase, Is.EqualTo(SessionPhase.Arena), "Game Loop never navigates on completion.");
            retainedResults.Add(result);
            var sessionState = lobby.State;
            var configuration = lobby.Authority!.Configuration;
            driver.Dispose();
            driver.Dispose();
            driver.Advance(Drive(), Observe);
            Assert.That(lobby.State, Is.SameAs(sessionState), "Disposal must not release session reservations or navigate.");
            Assert.That(driver.Host, Is.Null);
            Assert.That(driver.Match, Is.Null);
            Assert.That(driver.FinalResults, Is.Null);
            Assert.That(driver.EntryContext, Is.Null);
            Assert.That(driver.Latest, Is.Null);
            Assert.That(driver.ItemState, Is.Null);
            Assert.That(driver.EntryReady, Is.False);
            Assert.That(driver.IsActive, Is.False);
            Assert.That(driver.RequestItemUse(), Is.False);
            Assert.That(lobby.ActivateJoin, Is.Null);
            Assert.That(lobby.ArenaAdmissionOpen, Is.Null);
            Assert.That(wire.Connections.Count, Is.EqualTo(1));
            Assert.That(lobby.Authority.Return(0), Is.True);
            Assert.That(lobby.Authority.Configuration, Is.SameAs(configuration));
            Assert.That(lobby.State.Players.Select(player => player.Id), Is.EqualTo(new ulong[] { 1, 2 }));
            lobby.Authority.SetReady(0, true);
            lobby.Authority.SetReady(2, true);
            Assert.That(lobby.Authority.Start(0), Is.True);
            Assert.That(lobby.State.Match, Is.GreaterThan(generation));
        }

        Assert.That(retainedResults.All(result => result.Standings[0].Kills == 1), Is.True);
    }

    /// <summary>Late disposal cannot detach a newer generation's callbacks or release an offline reservation.</summary>
    [Test]
    public void DisposalDetachesOnlyOwnedCallbacksAndPreservesReservations()
    {
        using var wire = new DriverGateway();
        var lobby = StartJoinHost(wire);
        lobby.Migration = new SessionMigration(lobby, wire, "host", _ => "existing", (_, _) => 0);
        var old = new VehicleNetworkDriver(wire, lobby.State!.Match, lobby: lobby);
        lobby.Authority!.Disconnect(2);
        var reservation = lobby.State;
        old.Dispose();
        Assert.That(lobby.State, Is.SameAs(reservation));
        Assert.That(lobby.Authority.FindPlayer("existing"), Is.EqualTo(2));
        Assert.That(lobby.Migration.CaptureArena, Is.Null);
        Assert.That(lobby.Migration.RestoreArena, Is.Null);
        Assert.That(lobby.Migration.ObservedTick, Is.Null);
        Assert.That(lobby.Migration.MapConfiguration, Is.Null);
        Assert.That(lobby.Authority.Return(0), Is.True);
        Assert.That(lobby.Authority.FindPlayer("existing"), Is.Zero);
        lobby.Authority.SetReady(0, true);
        lobby.Authority.Start(0);
        using var next = new VehicleNetworkDriver(wire, lobby.State.Match, lobby: lobby);
        var capture = lobby.Migration.CaptureArena;
        old.Dispose();
        Assert.That(lobby.Migration.CaptureArena, Is.SameAs(capture));
        Assert.That(lobby.ArenaAdmissionOpen, Is.Not.Null);
    }
}
