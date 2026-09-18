using System.Numerics;
using Trackstorm.Client.Hud;
using Trackstorm.Core.Events;
using Trackstorm.Core.Input;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Sessions;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Transport.Tests;

/// <summary>Player-facing outcomes driven by the production journal and authorities.</summary>
[TestFixture]
internal sealed class ActivityFeedTests
{
    /// <summary>Authority-owned presence outcomes retain names after the player leaves.</summary>
    [Test]
    public void PresenceUsesCapturedNamesAndOnlyCommittedTransitions()
    {
        var lobby = new LobbyAuthority(10, "Host");
        using var feed = new ActivityFeedView();
        feed.Update(lobby.Events, true, 0);
        ulong player = lobby.Join(4, "Guest<>", "private-subject");
        lobby.SetReady(0, true);
        lobby.SetReady(4, true);
        Assert.That(lobby.Start(0, [4]), Is.True);
        lobby.Disconnect(4);
        lobby.Disconnect(4);
        lobby.AdvanceTime(30);
        Assert.That(lobby.Resume(5, 10, player, 1, "private-subject"), Is.True);
        lobby.Remove(5);
        Assert.That(feed.Entries.Select(row => row.Text), Is.EqualTo(new[]
        {
            "Guest joined the game", "Guest disconnected", "Guest reconnected", "Guest disconnected",
        }));
        Assert.That(feed.Entries.Select(row => row.Tone), Is.EqualTo(new[]
        {
            ActivityFeedTone.Arrival, ActivityFeedTone.Departure, ActivityFeedTone.Arrival, ActivityFeedTone.Departure,
        }));
    }

    /// <summary>Rows expire separately and live bursts survive journal history eviction.</summary>
    [Test]
    public void ExpiryIsIndependentAndBurstsEvictOldestWithoutWaitingForRendering()
    {
        var stream = new EventStream(2) { PlayerName = id => $"Name {id}" };
        using var feed = new ActivityFeedView();
        feed.Update(stream, true, 0);
        stream.Record(EventCategory.Session, "Joined", actor: 1);
        feed.Update(stream, true, 2);
        stream.Record(EventCategory.Session, "Joined", actor: 2);
        feed.Update(stream, true, 3);
        Assert.That(feed.Entries.Select(row => row.Text), Is.EqualTo(new[] { "Name 2 joined the game" }));
        for (ulong id = 3; id <= 30; id++)
        {
            stream.Record(EventCategory.Session, "Joined", actor: id);
        }

        for (int index = 0; index < 2000; index++)
        {
            stream.Record(EventCategory.Developer, "Setting changed");
        }

        Assert.That(feed.Entries.Select(row => row.Text), Is.EqualTo(Enumerable.Range(26, 5).Select(id => $"Name {id} joined the game")));
        feed.Update(stream, true, 5);
        Assert.That(feed.Entries, Is.Empty);
    }

    /// <summary>Unknown, local and internal events never pass the player-facing allowlist.</summary>
    [Test]
    public void FilteringIsAllowlistedAndNeverLeaksDiagnosticFields()
    {
        var stream = new EventStream();
        using var feed = new ActivityFeedView();
        feed.Update(stream, true, 0);
        foreach (EventCategory category in Enum.GetValues<EventCategory>())
        {
            stream.Record(category, "Unknown future event", target: 1, amount: 1, cause: "private detail", context: "internal detail");
        }

        stream.Record(EventCategory.Developer, "Kill");
        stream.Record(EventCategory.Session, "Joined", local: true);
        stream.Record(EventCategory.Network, "Grace entered", actor: 1);
        Assert.That(feed.Entries, Is.Empty);
        stream.Record(EventCategory.Lifecycle, "Dead", target: 1, cause: "private detail", context: "internal detail");
        Assert.That(feed.Entries.Single().Text, Is.EqualTo("Player 1 died"));
    }

    /// <summary>Stream acceptance owns duplicate protection; presentation binding never reads history.</summary>
    [Test]
    public void ReplicaDeduplicationAndRebindingNeverReplayHistory()
    {
        var host = new EventStream { PlayerName = _ => "Guest" };
        var replica = new EventStream(1);
        using var hostFeed = new ActivityFeedView();
        using var clientFeed = new ActivityFeedView();
        hostFeed.Update(host, true, 0);
        clientFeed.Update(replica, true, 0);
        host.Record(EventCategory.Network, "Reconnected", actor: 2);
        RuntimeEvent entry = EventCodec.Decode(EventCodec.Encode(host.Entries)).Single();
        Assert.That(replica.Accept(entry), Is.True);
        Assert.That(replica.Accept(entry), Is.False);
        Assert.That(clientFeed.Entries, Is.EqualTo(hostFeed.Entries));
        clientFeed.Update(replica, true, 5);
        Assert.That(replica.Accept(entry), Is.False);
        clientFeed.Update(replica, false, 0);
        clientFeed.Update(replica, true, 0);
        Assert.That(clientFeed.Entries, Is.Empty);
        using var reconstructed = new ActivityFeedView();
        reconstructed.Update(replica, true, 0);
        Assert.That(reconstructed.Entries, Is.Empty);
        clientFeed.Update(new EventStream(), true, 0);
        replica.Accept(entry with { Sequence = 2 });
        Assert.That(clientFeed.Entries, Is.Empty, "Old session is unsubscribed");
        Assert.That(reconstructed.Entries.Count, Is.EqualTo(1));
        reconstructed.Dispose();
        replica.Accept(entry with { Sequence = 3 });
        Assert.That(reconstructed.Entries, Is.Empty);
    }

    /// <summary>Actual lethal simulation outcomes project consistently even across separate event packets.</summary>
    /// <param name="attacker">Lethal source identity.</param>
    /// <param name="cause">Shared damage cause.</param>
    /// <param name="active">Whether scoring is active.</param>
    /// <param name="expected">Player-facing wording.</param>
    [TestCase(0ul, "collision", true, "Victim died")]
    [TestCase(2ul, "missile", true, "Victim died")]
    [TestCase(1ul, "missile", true, "Killer killed Victim with Missile")]
    [TestCase(1ul, "collision", true, "Killer killed Victim")]
    [TestCase(1ul, "missile", false, "Victim died")]
    public void CombatUsesSharedScoredAttributionAndOneRowPerDeath(ulong attacker, string cause, bool active, string expected)
    {
        var host = new HostVehicleSession(10, matchConfiguration: new MatchConfiguration { MinimumPlayers = 1, CountdownTicks = active ? 1u : 100u, KillTarget = 1 });
        host.Join(20);
        host.World.Events.PlayerName = id => id == 1 ? "Killer" : "Victim";
        using var feed = new ActivityFeedView();
        feed.Update(host.World.Events, true, 0);
        host.Step(default, Observe);
        host.Step(default, Observe);
        var input = new InputFrame(3, 0, 0, 0, 0, 0, 0);
        host.World.Step(input, host.World.State.Vehicles.Select(state => new VehicleStepRequest(state.VehicleId, input, Observe(state), state.VehicleId == 2 ? [new VehicleEffectRequest(new DamageEffect(1000, Vector3.Zero, Vector3.Zero), new DamageContext(cause, attacker, "impact"))] : [])).ToArray());
        Assert.That(feed.Entries.Single().Text, Is.EqualTo(expected));
        host.World.Restore(host.World.State);
        host.Step(default, Observe);
        Assert.That(feed.Entries.Count, Is.EqualTo(1), "Restore and respawning never replay death");
        var replica = new EventStream();
        using var peer = new ActivityFeedView();
        peer.Update(replica, true, 0);
        foreach (RuntimeEvent entry in host.World.Events.Entries)
        {
            // Separate packets preserve the same outcome even if death and kill are in different batches.
            replica.Accept(EventCodec.Decode(EventCodec.Encode([entry])).Single());
        }

        Assert.That(peer.Entries.Single().Text, Is.EqualTo(expected));
    }

    /// <summary>Lobby history stays hidden while match-end removal becomes a concise departure.</summary>
    [Test]
    public void InactiveFeedConsumesNothingAndMatchEndHasPlayerWording()
    {
        var lobby = new LobbyAuthority(10, "Host");
        using var feed = new ActivityFeedView();
        feed.Update(lobby.Events, false, 0);
        lobby.Join(4, "Guest", "subject");
        feed.Update(lobby.Events, true, 0);
        Assert.That(feed.Entries, Is.Empty);
        lobby.SetReady(0, true);
        lobby.SetReady(4, true);
        Assert.That(lobby.Start(0, [4]), Is.True);
        lobby.Disconnect(4);
        lobby.AdvanceTime(100000);
        Assert.That(feed.Entries.Select(row => row.Text), Is.EqualTo(new[] { "Guest disconnected" }));
        lobby.Return(0);
        Assert.That(feed.Entries.Select(row => row.Text), Is.EqualTo(new[] { "Guest disconnected", "Guest left the game" }));
        feed.Update(lobby.Events, false, 0);
        Assert.That(feed.Entries, Is.Empty);
    }

    private static VehicleObservation Observe(VehicleSnapshot state) => new(state.Movement.Physics, Vector3.Zero);
}
