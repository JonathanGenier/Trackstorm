using System.Numerics;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class ShieldTests
{
    private static readonly DamageContext Hit = new("missile", 2, "host-observed-hit");
    private static readonly VehiclePhysicsState Placement = new(new(4, 2, 8), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f), Vector3.Zero, Vector3.Zero);

    [Test]
    public void CountdownRejectsNewDamageAndDeploymentWhileRestoringValidatedWallContinuation()
    {
        var host = new HostVehicleSession(99, matchConfiguration: new() { CountdownTicks = 600, MinimumPlayers = 1 });
        host.Step(default, Observe);
        Assert.That(host.World.State.Match!.Phase, Is.EqualTo(Trackstorm.Core.Matches.MatchPhase.Countdown));
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.Shield), Is.True);
        var held = host.Items.Shields.Single();
        Assert.That(host.Items.DamageShield(host.World, held.Id, 7, 300, Hit), Is.Null);
        Assert.That(host.Items.TransitionShield(host.World, held.Id, ShieldStage.RearShield, ShieldStage.WorldWall, Placement), Is.False);
        Assert.That(host.Items.Shields.Single(), Is.EqualTo(held));
        var retained = held with
        {
            Stage = ShieldStage.WorldWall, Life = 0, Token = 0, HP = 700, DamageSequence = 7,
            Position = Placement.Position, Orientation = Placement.Orientation, ExpiresAtTick = 7200,
        };
        var boundary = new ItemPublication(1, host.Snapshot(), host.Items.Slots.Select(slot => slot with { Item = HeldItem.None }), [], [], shields: [retained]);
        host.Items.Restore(ItemCodec.DecodeState(ItemCodec.EncodeState(boundary)), host.Items.Revision + 1, host.Items.TokenHighWater);
        Assert.That(host.Items.Shields.Single(), Is.EqualTo(retained));
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.None));
        Assert.That(host.World.State.Match.Phase, Is.EqualTo(Trackstorm.Core.Matches.MatchPhase.Countdown));
        Assert.That(host.Items.DamageShield(host.World, retained.Id, 8, 300, Hit), Is.Null);
    }

    [Test]
    public void OnePoolSurvivesSelectionAndExistingWallRecoveryContractWithoutVehicleDamage()
    {
        var host = Start();
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.Shield), Is.True);
        var original = host.Items.Shields.Single();
        var vehicle = host.World.GetVehicle(1).Damage;
        Assert.That(original.HP, Is.EqualTo(1000));
        Assert.That(host.Items.DamageShield(host.World, original.Id, 1, 125, Hit)!.Amount, Is.EqualTo(125));
        Assert.That(host.Items.DamageShield(host.World, original.Id, 1, 125, Hit), Is.Null);
        Assert.That(host.Items.TransitionShield(host.World, original.Id, ShieldStage.Held, ShieldStage.WorldWall, Placement), Is.False);
        Assert.That(original.Stage, Is.EqualTo(ShieldStage.RearShield));
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.Shield));
        Assert.That(host.Items.TransitionShield(host.World, original.Id, ShieldStage.Held, ShieldStage.RearShield), Is.False);
        Assert.That(host.Items.Shields.Single().HP, Is.EqualTo(875));
        Assert.That(host.Items.DamageShield(host.World, original.Id, 2, 75, Hit)!.DestroyedTransition, Is.False);
        Assert.That(host.Items.TransitionShield(host.World, original.Id, ShieldStage.RearShield, ShieldStage.WorldWall, Placement), Is.True);
        Assert.That(host.Items.TransitionShield(host.World, original.Id, ShieldStage.RearShield, ShieldStage.WorldWall, Placement), Is.False);
        Assert.That(host.Items.TransitionShield(host.World, original.Id, ShieldStage.WorldWall, ShieldStage.Held), Is.False);
        Assert.That(host.Items.Shields.Single().HP, Is.EqualTo(800));
        Assert.That(host.Items.Shields.Single().Id, Is.EqualTo(original.Id));
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.None));
        var lethal = host.Items.DamageShield(host.World, original.Id, 3, 5000, Hit)!;
        Assert.That(lethal.Amount, Is.EqualTo(800));
        Assert.That(lethal.DestroyedTransition, Is.True);
        ulong revision = host.Items.ReliableRevision;
        Assert.That(host.Items.DamageShield(host.World, original.Id, 3, 5000, Hit), Is.Null);
        Assert.That(host.Items.DamageShield(host.World, original.Id, 4, 5000, Hit), Is.Null);
        Assert.That(host.Items.Shields, Is.Empty);
        Assert.That(host.Items.ReliableRevision, Is.EqualTo(revision));
        Assert.That(host.World.GetVehicle(1).Damage, Is.EqualTo(vehicle));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DestroyingEitherPhysicalSlotPreservesOtherItemAndSelection(bool second)
    {
        var host = Start();
        host.Items.Grant(host.World, 1, second ? HeldItem.Wrench : HeldItem.Shield);
        host.Items.Grant(host.World, 1, second ? HeldItem.Shield : HeldItem.Wrench);
        var slot = host.Items.Slots.Single();
        var state = host.Items.Shields.Single();
        host.Items.DamageShield(host.World, state.Id, 1, 1000, Hit);
        Assert.That(host.Items.Slots.Single(), Is.EqualTo(second ? slot with { SecondItem = HeldItem.None } : slot with { Item = HeldItem.None }));
        Assert.That(host.Items.TransitionShield(host.World, state.Id, ShieldStage.Held, ShieldStage.RearShield), Is.False);
        Assert.That(host.UseItem(0, 99, slot.Life, state.Token), Is.False);
    }

    [Test]
    public void MultiplePoolsRemainIndependentAndDamageGuardsDoNotHeal()
    {
        var host = Start();
        host.Join(42);
        foreach (ulong owner in new ulong[] { 1, 1, 2 }) { Assert.That(host.Items.Grant(host.World, owner, HeldItem.Shield), Is.True); }
        var states = host.Items.Shields;
        host.Items.DamageShield(host.World, states[0].Id, 2, 300, Hit);
        Assert.That(host.Items.DamageShield(host.World, states[0].Id, 1, 500, Hit), Is.Null);
        Assert.That(host.Items.DamageShield(host.World, states[0].Id, 3, -100, Hit), Is.Null);
        Assert.That(host.Items.DamageShield(host.World, states[0].Id, 3, 0, Hit), Is.Null);
        Assert.That(host.Items.DamageShield(host.World, states[0].Id, 0, 100, Hit), Is.Null);
        Assert.Throws<ArgumentException>(() => host.Items.DamageShield(host.World, states[0].Id, 3, float.NaN, Hit));
        Assert.That(host.Items.Shields.Select(state => state.HP), Is.EqualTo(new float[] { 700, 1000, 1000 }));
        Assert.That(host.Items.Switch(host.World, 1, 1, 1), Is.True);
        Assert.That(host.Items.Shields.Select(state => state.HP), Is.EqualTo(new float[] { 700, 1000, 1000 }));
        var pools = host.Items.Shields;
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["damage.max_hp"] = 2000 }, out _), Is.True);
        Assert.That(host.Items.Shields, Is.EqualTo(pools), "vehicle health tuning cannot refill an item pool");
    }

    [TestCase(ShieldStage.Held)]
    [TestCase(ShieldStage.RearShield)]
    [TestCase(ShieldStage.WorldWall)]
    public void FullCheckpointJoinAndAuthorityRestorePreservePoolPoseAndDuplicateMemory(ShieldStage stage)
    {
        var host = Start();
        host.Items.Grant(host.World, 1, HeldItem.Shield);
        var state = host.Items.Shields.Single();
        host.Items.DamageShield(host.World, state.Id, 7, 325, Hit);
        if (stage == ShieldStage.Held) { host.Items.Switch(host.World, 1, state.Life, 1); }
        if (stage == ShieldStage.WorldWall) { host.Items.TransitionShield(host.World, state.Id, ShieldStage.RearShield, stage, Placement); }
        var items = ItemCodec.DecodeState(ItemCodec.EncodeState(Publication(host)));
        var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(items, host.World.State.Match!, null, host.Configuration)));
        var restored = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 1);
        Assert.That(restored.Items.Shields, Is.EqualTo(host.Items.Shields));
        Assert.That(restored.Items.Shields.Single().Stage, Is.EqualTo(stage));
        if (stage != ShieldStage.WorldWall)
        {
            var slot = restored.Items.Slots.Single();
            restored.Items.Switch(restored.World, 1, state.Life, slot.SelectionRevision + 1);
            Assert.That(restored.Items.Shields.Single().Stage, Is.EqualTo(stage == ShieldStage.Held ? ShieldStage.RearShield : ShieldStage.Held));
            restored.Items.Switch(restored.World, 1, state.Life, slot.SelectionRevision + 2);
            Assert.That(restored.Items.Shields, Is.EqualTo(host.Items.Shields));
        }
        Assert.That(restored.Items.DamageShield(restored.World, state.Id, 7, 325, Hit), Is.Null);
        Assert.That(host.PrepareJoin(3, 2)!.Items.Shields, Is.EqualTo(host.Items.Shields));
        Assert.That(restored.Items.DamageShield(restored.World, state.Id, 8, 25, Hit)!.Amount, Is.EqualTo(25));
        Assert.That(restored.Items.Shields.Single().HP, Is.EqualTo(650));
        var continued = new ItemAuthority();
        continued.Restore(items, 10, host.Items.TokenHighWater);
        Assert.That(continued.Shields, Is.EqualTo(items.Shields));
        Assert.Throws<ArgumentException>(() => continued.Restore(items, 10, 0));
    }

    [Test]
    public void RetainedRespawnChangesCapabilitiesWithoutRefillingOrReplacingEntity()
    {
        var host = Start(new() { DelayTicks = 1, ClearHeldItemOnDeath = false });
        host.Items.Grant(host.World, 1, HeldItem.Shield);
        host.Items.Grant(host.World, 1, HeldItem.Shield);
        var ids = host.Items.Shields.Select(state => state.Id).ToArray();
        host.Items.DamageShield(host.World, ids[0], 1, 400, Hit);
        host.Items.Step(host.World, new(host.World.State.Tick + 1, 0, 0, 0, 0, 0, 0), [new(1, new(host.World.State.Tick + 1, 0, 0, 0, 0, 0, 0), Observe(host.World.GetVehicle(1)), [new(new DamageEffect(1000, Vector3.Zero, Vector3.Zero), Hit)])], (_, _) => null);
        Assert.That(host.Items.Shields[0].HP, Is.EqualTo(600));
        host.Step(default, Observe);
        Assert.That(host.Items.Shields.Select(state => state.Id), Is.EqualTo(ids));
        Assert.That(host.Items.Shields[0].HP, Is.EqualTo(600));
        Assert.That(host.Items.Shields[0].Stage, Is.EqualTo(ShieldStage.RearShield));
        Assert.That(host.Items.Shields[0].Life, Is.EqualTo(host.World.GetVehicle(1).LifeId));
        Assert.That(host.Items.Shields[0].Token, Is.Not.EqualTo(ids[0]));
        Assert.That(ItemCodec.DecodeState(ItemCodec.EncodeState(Publication(host))).Shields, Is.EqualTo(host.Items.Shields));
    }

    [Test]
    public void DeathResetAndDepartureClearAttachmentsButKeepReleasedWalls()
    {
        var host = Start();
        host.Items.Grant(host.World, 1, HeldItem.Shield);
        var wall = host.Items.Shields.Single();
        host.Items.TransitionShield(host.World, wall.Id, ShieldStage.RearShield, ShieldStage.WorldWall, Placement);
        host.Items.Grant(host.World, 1, HeldItem.Shield);
        host.Items.Step(host.World, new(host.World.State.Tick + 1, 0, 0, 0, 0, 0, 0), [new(1, new(host.World.State.Tick + 1, 0, 0, 0, 0, 0, 0), Observe(host.World.GetVehicle(1)), [new(new DamageEffect(1000, Vector3.Zero, Vector3.Zero), Hit)])], (_, _) => null);
        Assert.That(host.Items.Shields.Select(state => state.Id), Is.EqualTo(new[] { wall.Id }));
        host.Items.RemovePlayer(1);
        Assert.That(host.Items.Shields.Single().Id, Is.EqualTo(wall.Id));
        host.Step(default, Observe);
        Assert.That(host.Items.Shields.Single().Id, Is.EqualTo(wall.Id));
    }

    [Test]
    public void InvalidPublicationsAndCodecLayoutsCannotManufactureHealthOrOwnership()
    {
        var host = Start();
        host.Items.Grant(host.World, 1, HeldItem.Shield);
        var state = host.Items.Shields.Single();
        foreach (var invalid in new[] { state with { HP = 1001 }, state with { HP = 0 }, state with { HP = float.NaN }, state with { HP = 999 }, state with { DamageSequence = 1 }, state with { Token = 999 }, state with { Life = 2 }, state with { Stage = (ShieldStage)100 }, state with { Orientation = default } })
        { Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], [], shields: [invalid])); }
        Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], []));
        Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], [], shields: [state with { Stage = ShieldStage.Held }]));
        Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], [], shields: [state, state]));
        byte[] bytes = ItemCodec.EncodeState(Publication(host));
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeState(bytes.AsSpan(0, bytes.Length - 1)));
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeState(bytes.Concat(new byte[] { 0 }).ToArray()));
        bytes[2] = 15;
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeState(bytes));
    }

    [Test]
    public void ExplicitResetClearsAttachmentsAndRejectedWorldBatchCannotClearThem()
    {
        var host = Start();
        host.Items.Grant(host.World, 1, HeldItem.Shield);
        var state = host.Items.Shields.Single();
        host.Items.DamageShield(host.World, state.Id, 1, 100, Hit);
        var input = new Trackstorm.Core.Input.InputFrame(host.World.State.Tick + 1, 0, 0, 0, 0, 0, 0);
        Assert.Throws<ArgumentException>(() => host.Items.Step(host.World, input, [], (_, _) => null));
        Assert.That(host.Items.Shields.Single().HP, Is.EqualTo(900));
        host.Items.Step(host.World, input, [new(1, input, Observe(host.World.GetVehicle(1)), [], reset: host.World.GetVehicle(1).ObservedPhysics)], (_, _) => null);
        Assert.That(host.Items.Shields, Is.Empty);
    }

    [Test]
    public void LiveEntityBoundRejectsGrantWithoutSpendingCapabilityAndDestructionReleasesCapacity()
    {
        var host = Start();
        for (int i = 0; i < ItemAuthority.MaximumShields; i++)
        {
            Assert.That(host.Items.Grant(host.World, 1, HeldItem.Shield), Is.True);
            ulong id = host.Items.Shields.Last().Id;
            host.Items.TransitionShield(host.World, id, ShieldStage.RearShield, ShieldStage.WorldWall, Placement);
        }
        ulong token = host.Items.TokenHighWater;
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.Shield), Is.False);
        Assert.That(host.Items.TokenHighWater, Is.EqualTo(token));
        host.Items.DamageShield(host.World, host.Items.Shields[0].Id, 1, 1000, Hit);
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.Shield), Is.True);
        Assert.That(host.Items.Shields.Last().HP, Is.EqualTo(1000));
        Assert.That(host.Items.Shields.Last().Id, Is.GreaterThan(token));
    }

    [Test]
    public void FinishedClearsLiveWallsAndAttachedSlotsAndCannotBeRevivedByFurtherActions()
    {
        var host = Start();
        host.Items.Grant(host.World, 1, HeldItem.Shield);
        ulong wall = host.Items.Shields.Single().Id;
        host.Items.TransitionShield(host.World, wall, ShieldStage.RearShield, ShieldStage.WorldWall, Placement);
        host.Items.Grant(host.World, 1, HeldItem.Shield);
        host.Items.Grant(host.World, 1, HeldItem.Wrench);
        var current = host.World.State;
        var match = current.Match!;
        var finished = new Trackstorm.Core.Matches.MatchState(current.Tick, match.Revision + 1, match.KillTarget,
            Trackstorm.Core.Matches.MatchPhase.Finished, null, 1, match.Players.Select(player => player with { Wins = 1, Stunts = null }), mode: match.Mode,
            activeStartedAtTick: match.ActiveStartedAtTick, durationTicks: match.DurationTicks);
        host.World.Restore(new(current.Tick, current.LastInput, current.Vehicles, finished));
        Assert.That(host.Items.DamageShield(host.World, wall, 1, 100, Hit), Is.Null);
        host.Step(default, Observe);
        Assert.That(host.Items.Shields, Is.Empty);
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.None));
        Assert.That(host.Items.Slots.Single().SecondItem, Is.EqualTo(HeldItem.Wrench));
    }

    private static HostVehicleSession Start(RespawnConfiguration? respawn = null)
    {
        var host = new HostVehicleSession(99, respawnConfiguration: respawn, matchConfiguration: new() { CountdownTicks = 1, MinimumPlayers = 1 });
        host.Step(default, Observe);
        host.Step(default, Observe);
        return host;
    }

    private static ItemPublication Publication(HostVehicleSession host) => new(1, host.Snapshot(), host.Items.Slots, [], [], shields: host.Items.Shields);
    private static VehicleObservation Observe(VehicleSnapshot state) => new(state.Movement.Physics, Vector3.UnitY);
}
