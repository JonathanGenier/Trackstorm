using System.Numerics;
using Trackstorm.Core.Events;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Sessions;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests;

/// <summary>Behavioral coverage of event identity, committed authority outcomes and bounded history.</summary>
[TestFixture]
internal sealed class EventStreamTests
{
    /// <summary>Equal timestamps have distinct order; eviction cannot permit replay of an old identity.</summary>
    [Test]
    public void OrderingRetentionAndReplayRemainBounded()
    {
        var stream = new EventStream(2);
        stream.AdvanceTime(1234);
        stream.Record(EventCategory.Session, "Joined", actor: 1);
        var first = stream.Entries.Single();
        stream.Record(EventCategory.Item, "Used", actor: 1, cause: "Missile");
        stream.AdvanceTime(100);
        stream.Record(EventCategory.Damage, "Applied", target: 2, amount: 0.125);
        Assert.Multiple(() =>
        {
            Assert.That(stream.Entries.Select(entry => entry.Sequence), Is.EqualTo(new ulong[] { 2, 3 }));
            Assert.That(stream.Entries.All(entry => entry.Milliseconds == 1234), Is.True);
            Assert.That(stream.Accept(first), Is.False);
            Assert.That(stream.Entries.Count, Is.EqualTo(2));
        });
        stream.Record(EventCategory.Network, "Recovery state", local: true);
        Assert.That(stream.LastSequence, Is.EqualTo(3));
    }

    /// <summary>Codec preserves exact fields and rejects malformed, unordered and oversized payloads.</summary>
    [Test]
    public void CodecPreservesStructuredDataAndRejectsInvalidBatches()
    {
        var stream = new EventStream { PlayerName = id => id == 1 ? "Patrick<>" : "Jonathan" };
        stream.Record(EventCategory.Damage, "Applied", 1, 2, "Missile", amount: 0.125, hp: 99.875, maxHP: 100, life: 3, tick: 7);
        var entry = stream.Entries.Single();
        Assert.That(EventCodec.Decode(EventCodec.Encode(stream.Entries)).Single(), Is.EqualTo(entry));
        Assert.That(entry.ActorName, Is.EqualTo("Patrick"));
        Assert.Throws<ArgumentException>(() => EventCodec.Encode([entry, entry]));
        Assert.Throws<ArgumentException>(() => EventCodec.Encode([entry with { Amount = double.NaN }]));
        Assert.Throws<ArgumentException>(() => EventCodec.Decode([(byte)'T', (byte)'E', 1, (byte)'{']));
        Assert.Throws<ArgumentException>(() => EventCodec.Encode(Enumerable.Repeat(entry, 17).ToArray()));
        Assert.Throws<ArgumentException>(() => EventCodec.Encode([entry with { Local = true }]));
    }

    /// <summary>Two same-tick hits retain both exact applied values, including the clamped lethal hit.</summary>
    [Test]
    public void EveryCommittedHitIsRecordedWithoutSnapshotReconstruction()
    {
        var host = new HostVehicleSession(10);
        var world = host.World;
        var input = new InputFrame(1, 0, 0, 0, 0, 0, 0);
        var state = world.GetVehicle(1);
        world.Step(input, [new VehicleStepRequest(1, input, new VehicleObservation(state.Movement.Physics, Vector3.Zero), [new VehicleEffectRequest(new DamageEffect(0.125f, Vector3.Zero, Vector3.Zero), new DamageContext("missile", 2, "radial-explosion")), new VehicleEffectRequest(new DamageEffect(1000, Vector3.Zero, Vector3.Zero), new DamageContext("collision", 0, "world-or-prop"))])]);
        var hits = world.Events.Entries.Where(entry => entry.Category == EventCategory.Damage).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(hits.Select(entry => entry.Amount), Is.EqualTo(new double?[] { 0.125, 99.875 }));
            Assert.That(hits.Select(entry => entry.RemainingHP), Is.EqualTo(new double?[] { 99.875, 0 }));
            Assert.That(hits[0].Actor, Is.EqualTo(2));
            Assert.That(hits[1].Cause, Is.EqualTo("map collision"));
            Assert.That(world.Events.Entries.Count(entry => entry.Kind == "Dead"), Is.EqualTo(1));
        });
        ulong sequence = world.Events.LastSequence;
        world.Restore(world.State);
        Assert.That(world.Events.LastSequence, Is.EqualTo(sequence));
        Assert.Throws<ArgumentException>(() => world.Step(input, []));
        Assert.That(world.Events.LastSequence, Is.EqualTo(sequence));
    }

    /// <summary>Applied healing is clamped and follows its single confirmed Wrench use.</summary>
    [Test]
    public void WrenchUseRecordsExactHealAndNeverRepeatsOnNextStep()
    {
        var host = new HostVehicleSession(10);
        var world = host.World;
        var input = new InputFrame(1, 0, 0, 0, 0, 0, 0);
        var state = world.GetVehicle(1);
        world.Step(input, [new VehicleStepRequest(1, input, Observe(state), [new VehicleEffectRequest(new DamageEffect(10, Vector3.Zero, Vector3.Zero), new DamageContext("explosion", 0, "test"))])]);
        Assert.That(host.GiveItem(0, HeldItem.Wrench), Is.True);
        var slot = host.Items.Slots.Single();
        Assert.That(host.UseItem(0, 10, slot.Life, slot.Token), Is.True);
        Assert.That(host.UseItem(0, 10, slot.Life, slot.Token), Is.False);
        host.Step(default, Observe);
        var use = world.Events.Entries.Single(entry => entry.Kind == "Used");
        var heal = world.Events.Entries.Single(entry => entry.Category == EventCategory.Healing);
        Assert.Multiple(() =>
        {
            Assert.That(heal.Amount, Is.EqualTo(10));
            Assert.That(heal.Cause, Is.EqualTo("Wrench"));
            Assert.That(use.Sequence, Is.LessThan(heal.Sequence));
        });
        host.Step(default, Observe);
        Assert.That(world.Events.Entries.Count(entry => entry.Category == EventCategory.Healing), Is.EqualTo(1));
    }

    /// <summary>Reservation and identity reuse emit one transition each and never include authenticated subjects.</summary>
    [Test]
    public void ReconnectEventsUseSafeIdentityAndSingleTransitions()
    {
        var lobby = new LobbyAuthority(10, "Host");
        ulong player = lobby.Join(4, GameVersion.Current.ToString(), "Guest", "secret-authenticated-subject");
        lobby.SetReady(0, true);
        lobby.SetReady(4, true);
        Assert.That(lobby.Start(0, [4]), Is.True);
        lobby.Disconnect(4);
        lobby.Disconnect(4);
        lobby.AdvanceTime(30);
        Assert.That(lobby.Resume(5, GameVersion.Current.ToString(), 10, player, 1, "secret-authenticated-subject"), Is.True);
        lobby.Disconnect(5);
        lobby.AdvanceTime(90);
        lobby.AdvanceTime(100000);
        Assert.That(lobby.Events.Entries.Any(entry => entry.Kind == "Match reservation ended"), Is.False);
        lobby.Return(0);
        Assert.Multiple(() =>
        {
            Assert.That(lobby.Events.Entries.Count(entry => entry.Kind == "Reconnected"), Is.EqualTo(1));
            Assert.That(lobby.Events.Entries.Count(entry => entry.Kind == "Match reservation ended"), Is.EqualTo(1));
            Assert.That(lobby.Events.Entries.Any(entry => entry.ToString().Contains("secret-authenticated-subject", StringComparison.Ordinal)), Is.False);
        });
    }

    /// <summary>Only real committed collision damage is journaled despite duplicate contacts and cooldown ticks.</summary>
    [Test]
    public void CollisionContactsAndCooldownDoNotDuplicateDamage()
    {
        var host = new HostVehicleSession(10);
        var contact = new VehicleContact(-Vector3.UnitX * 20, Vector3.UnitX, 10000, 0);
        VehicleObservation Collision(VehicleSnapshot state) => new(state.Movement.Physics, Vector3.UnitY, [contact, contact]);
        host.Step(default, Collision);
        host.Step(default, Collision);
        Assert.That(host.World.Events.Entries.Count(entry => entry.Category == EventCategory.Damage), Is.EqualTo(1));
        Assert.That(host.World.Events.Entries.Single(entry => entry.Category == EventCategory.Damage).Cause, Is.EqualTo("map collision"));
    }

    /// <summary>Kill events reuse scored deaths, occur before match completion, and cannot replay from a wreck.</summary>
    [Test]
    public void ScoredKillIsAttributedOnceBeforeMatchFinish()
    {
        var host = new HostVehicleSession(10, matchConfiguration: new Core.Matches.MatchConfiguration { MinimumPlayers = 1, CountdownTicks = 1, KillTarget = 1 });
        host.Join(20);
        host.Step(default, Observe);
        host.Step(default, Observe);
        var input = new InputFrame(3, 0, 0, 0, 0, 0, 0);
        host.World.Step(input, host.World.State.Vehicles.Select(state => new VehicleStepRequest(state.VehicleId, input, Observe(state), state.VehicleId == 2 ? [new VehicleEffectRequest(new DamageEffect(1000, Vector3.Zero, Vector3.Zero), new DamageContext("missile", 1, "radial-explosion"))] : [])).ToArray());
        var kill = host.World.Events.Entries.Single(entry => entry.Kind == "Kill");
        Assert.Multiple(() =>
        {
            Assert.That(kill.Actor, Is.EqualTo(1));
            Assert.That(kill.Target, Is.EqualTo(2));
            Assert.That(kill.Cause, Is.EqualTo("Missile"));
            Assert.That(host.World.Events.Entries.Single(entry => entry.Kind == "Dead").Context, Is.EqualTo("Scored kill"));
            Assert.That(kill.Sequence, Is.LessThan(host.World.Events.Entries.Single(entry => entry.Kind == "Finished").Sequence));
        });
        host.Step(default, Observe);
        Assert.That(host.World.Events.Entries.Count(entry => entry.Kind == "Kill"), Is.EqualTo(1));
    }

    /// <summary>Death removal observes the old inventory and emits exactly one committed item outcome.</summary>
    [Test]
    public void HeldItemRemovalOnDeathIsRecordedOnce()
    {
        var host = new HostVehicleSession(10);
        host.GiveItem(0, HeldItem.Missile);
        var state = host.World.GetVehicle(1);
        var input = new InputFrame(1, 0, 0, 0, 0, 0, 0);
        host.Items.Step(host.World, input, [new VehicleStepRequest(1, input, Observe(state), [new VehicleEffectRequest(new DamageEffect(1000, Vector3.Zero, Vector3.Zero), new DamageContext("explosion", 0, "test"))])], (_, _) => null);
        host.Step(default, Observe);
        var removed = host.World.Events.Entries.Single(entry => entry.Category == EventCategory.Item && entry.Kind == "Removed");
        Assert.That(removed.Target, Is.EqualTo(1));
        Assert.That(removed.Cause, Is.EqualTo("Missile"));
    }

    private static VehicleObservation Observe(VehicleSnapshot state) => new(state.Movement.Physics, Vector3.Zero);
}
