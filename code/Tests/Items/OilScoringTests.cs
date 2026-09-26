using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class OilScoringTests
{
    [Test]
    public void ManyRetainedOwnersCanScoreTheSameEnemyWithoutTheFormerPatchAwardBound()
    {
        var host = Create();
        var w = host.World.State;
        var m = w.Match!;
        var players = m.Players.Concat(Enumerable.Range(3, 254).Select(id => new PlayerScore((ulong)id, 0, 0, 0, 0))).ToArray();
        host.World.Restore(new(w.Tick, w.LastInput, w.Vehicles, new(m.Tick, m.Revision + 1, m.KillTarget, m.Phase, null, null, players)));
        var patches = Enumerable.Range(3, 254).Select(id => new OilPatch((ulong)id, (ulong)id, Vector3.Zero, Vector3.UnitY, 3)).ToArray();
        host.Items.Restore(new(1, host.Snapshot(), [], [], [], patches: patches), 1, 256);
        Step(host, 2);
        Assert.That(host.World.State.Match!.Awards.Count, Is.EqualTo(254));
        Assert.That(host.World.State.Match.Awards.All(award => award.Points == 50), Is.True);
        var decoded = MatchCodec.Decode(MatchCodec.Encode(99, host.World.State.Match)).State;
        Assert.That(decoded.Awards, Is.EqualTo(host.World.State.Match.Awards));
        Step(host, 2);
        Assert.That(host.World.State.Match.Players.Where(player => player.Player >= 3).All(player => player.CircusScore == 50), Is.True);
    }

    [Test]
    public void AwardCountBeyondOneByteRoundTripsAllRetainedOwners()
    {
        var players = Enumerable.Range(1, 256).Select(id => new PlayerScore((ulong)id, 0, 0, 0, 0) { CircusScore = 50 }).ToArray();
        var awards = players.Select(p => new CircusScoreAward(p.Player, CircusScoreCategory.Oil, 50)).ToArray();
        var state = new MatchState(1, 1, 100, MatchPhase.Active, null, null, players, awards: awards);
        Assert.That(MatchCodec.Decode(MatchCodec.Encode(99, state)).State.Awards, Is.EqualTo(awards));
    }

    [Test]
    public void DistinctRivalBanksOnceSelfAndReentryNeverBank()
    {
        var host = Create();
        Step(host, 1);
        Assert.That(Score(host), Is.Zero);
        Step(host, 1, 2);
        Assert.That(Score(host), Is.EqualTo(50));
        Assert.That(host.World.State.Match!.Awards.Single(), Is.EqualTo(new CircusScoreAward(1, CircusScoreCategory.Oil, 50)));
        for (int i = 0; i < 130; i++) { Step(host, 1, 2); }
        Assert.That(Score(host), Is.EqualTo(50));
        Assert.That(host.World.GetVehicle(2).Movement.OilTicks, Is.EqualTo(105));
        Step(host);
        var w = host.World.State;
        var m = w.Match!;
        host.World.Restore(new(w.Tick, w.LastInput, w.Vehicles, new MatchState(m.Tick, m.Revision + 1, m.KillTarget, m.Phase, null, null,
            m.Players.Select(p => p.Player == 1 ? p with { Kills = 3, Deaths = 1, ProcessedLife = 1 } : p with { Deaths = 3, ProcessedLife = 1 }))));
        Step(host, 1, 2);
        Assert.That(Score(host), Is.EqualTo(50));
        var decoded = MatchCodec.Decode(MatchCodec.Encode(99, host.World.State.Match!)).State;
        Assert.That(decoded.Players, Is.EqualTo(host.World.State.Match!.Players));
        Assert.That(decoded.Awards, Is.Empty);
        host.JoinPlayer(20, 3);
        Step(host, 3);
        Assert.That(Score(host), Is.EqualTo(125), "Second distinct enemy uses the owner's current K/D.");
        Assert.That(host.Items.Patches, Is.Empty);
    }

    [Test]
    public void RestoreLateJoinAndRetiredOwnerPreserveTotalsAndEntryLatch()
    {
        var host = Create();
        Step(host, 2);
        var join = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(host.PrepareJoin(3, 3)!));
        Assert.That(join.Items.Patches.Single().Owner, Is.EqualTo(1));
        Assert.That(join.Match.Players.Single(p => p.Player == 1).CircusScore, Is.EqualTo(50));
        var m = host.World.State.Match!;
        var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(
            new ItemPublication(3, host.Snapshot(), host.Items.Slots, [], [], patches: host.Items.Patches, oilContacts: host.Items.OilContacts),
            new MatchState(m.Tick, m.Revision, m.KillTarget, m.Phase, null, null, m.Players), null, host.Configuration)));
        var restored = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 2);
        Assert.That(restored.World.State.Match!.Awards, Is.Empty);
        Step(restored, 2);
        Assert.That(Score(restored), Is.EqualTo(50));
        // The former host is a retained identity after authority replacement.
        restored.ExpirePlayer(1);
        Step(restored);
        Step(restored, 2);
        Assert.That(Score(restored), Is.EqualTo(50));
        Assert.That(restored.Items.Patches.Single().Owner, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LethalOrResetBatchCannotScoreAnUnappliedEffect(bool reset)
    {
        var host = Create();
        var input = new InputFrame(host.World.State.Tick + 1, 0, 0, 0, 0, 0, 0);
        var requests = host.World.State.Vehicles.Select(v => new VehicleStepRequest(v.VehicleId, input, Observe(v, v.VehicleId == 2),
            v.VehicleId == 2 && !reset ? [new VehicleEffectRequest(new DamageEffect(10000, Vector3.Zero, Vector3.Zero), new DamageContext("test", 0, "lethal"))] : [],
            reset && v.VehicleId == 2 ? Observe(v, true).Physics : null)).ToArray();
        host.Items.Step(host.World, input, requests, (_, _) => null);
        Assert.That(Score(host), Is.Zero);
        if (reset)
        {
            Step(host, 2);
            Assert.That(Score(host), Is.EqualTo(50));
        }
    }

    [Test]
    public void DeployerDeathRespawnAndRivalReconnectKeepOwnershipAndLatch()
    {
        var host = Create();
        var patch = host.Items.Patches.Single();
        var input = new InputFrame(host.World.State.Tick + 1, 0, 0, 0, 0, 0, 0);
        host.Items.Step(host.World, input, host.World.State.Vehicles.Select(v => new VehicleStepRequest(v.VehicleId, input, Observe(v, false),
            v.VehicleId == 1 ? [new VehicleEffectRequest(new DamageEffect(10000, Vector3.Zero, Vector3.Zero), new DamageContext("test", 0, "lethal"))] : [])).ToArray(), (_, _) => null);
        Assert.That(host.World.GetVehicle(1).CanInteract, Is.False);
        Step(host, 2);
        Assert.That(Score(host), Is.EqualTo(50), "Dead owner retains credit.");
        host.Suspend(10);
        Step(host, 2);
        Assert.That(host.ResumePlayer(20, 2), Is.True);
        Step(host, 2);
        Assert.That(Score(host), Is.EqualTo(50), "Rebind cannot replay an existing entry.");
        for (int i = 0; i < 400 && !host.World.GetVehicle(1).CanInteract; i++) { Step(host); }
        Assert.That(host.World.GetVehicle(1).LifeId, Is.EqualTo(2));
        Assert.That(host.Items.Patches.Single().Id, Is.EqualTo(patch.Id));
        Step(host, 1, 2);
        Assert.That(Score(host), Is.EqualTo(50), "Self entry and a previously affected rival earn nothing.");
    }

    [Test]
    public void OverlappingPatchEntriesRemainDistinctButDoNotRepeatWhileInside()
    {
        var host = Create();
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.Oil), Is.True);
        var slot = host.Items.Slots.Single();
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.True);
        host.Step(default, v => Observe(v, false), placeOil: (s, _) => new OilPatch(s.Token, s.Vehicle, Vector3.Zero, Vector3.UnitY, 3));
        Step(host, 2);
        Assert.That(Score(host), Is.EqualTo(100));
        Assert.That(host.World.GetVehicle(2).Movement.OilTicks, Is.EqualTo(105));
        Step(host, 2);
        Assert.That(Score(host), Is.EqualTo(100));
    }

    [Test]
    public void RejectedBatchCannotBankOrConsumeEntry()
    {
        var host = Create();
        var input = new InputFrame(host.World.State.Tick + 1, 0, 0, 0, 0, 0, 0);
        var invalid = host.World.State.Vehicles.Select(v => new VehicleStepRequest(v.VehicleId, default, Observe(v, true))).ToArray();
        Assert.Throws<ArgumentException>(() => host.Items.Step(host.World, input, invalid, (_, _) => null));
        Assert.That(Score(host), Is.Zero);
        Assert.That(host.Items.OilContacts, Is.Empty);
        Step(host, 2);
        Assert.That(Score(host), Is.EqualTo(50));
    }

    [TestCase(MatchMode.FirstToTarget, true)]
    [TestCase(MatchMode.Circus, false)]
    public void NonCircusOrPreActiveTriggersNeverBank(MatchMode mode, bool active)
    {
        var host = Create(mode, active);
        Step(host, 2);
        Assert.That(host.World.GetVehicle(2).Movement.OilTicks, Is.EqualTo(105));
        Assert.That(Score(host), Is.Zero);
    }

    private static HostVehicleSession Create(MatchMode mode = MatchMode.Circus, bool active = true)
    {
        var host = new HostVehicleSession(99, matchConfiguration: new MatchConfiguration { MinimumPlayers = 2, CountdownTicks = active ? 1ul : 1000ul, KillTarget = 100, Mode = mode });
        host.JoinPlayer(10, 2);
        Step(host); Step(host);
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.Oil), Is.True);
        var slot = host.Items.Slots.Single();
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.True);
        host.Step(default, v => Observe(v, false), placeOil: (s, _) => new OilPatch(s.Token, s.Vehicle, Vector3.Zero, Vector3.UnitY, 3));
        return host;
    }

    private static double Score(HostVehicleSession host) => host.World.State.Match!.Players.Single(p => p.Player == 1).CircusScore;
    private static void Step(HostVehicleSession host, params ulong[] inside) => host.Step(default, v => Observe(v, inside.Contains(v.VehicleId)));
    private static VehicleObservation Observe(VehicleSnapshot v, bool inside) => new(new VehiclePhysicsState(new Vector3(inside ? 0 : 6, 0.9f, 0), Quaternion.Identity, Vector3.Zero, Vector3.Zero), Vector3.UnitY);
}
