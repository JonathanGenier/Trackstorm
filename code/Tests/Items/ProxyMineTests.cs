using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class ProxyMineTests
{
    [Test]
    public void ContinuousFieldIsZeroOutsideWeakAtEdgeAndProgressivelyAggressive()
    {
        var tuning = new ItemConfiguration();
        float Force(float distance) => ProxyMineState.AttractionForce(distance, tuning);
        Assert.That(Force(25), Is.Zero);
        Assert.That(Force(24), Is.Zero);
        Assert.That(Force(23.999f), Is.LessThan(0.02));
        Assert.That(Force(22), Is.LessThan(Force(12) / 5));
        Assert.That(Force(12), Is.LessThan(Force(2) / 3));
        Assert.That(Force(0), Is.EqualTo(tuning.MineMaximumForce));
        for (float distance = 0; distance < 24; distance += 0.05f) { Assert.That(Force(distance), Is.GreaterThan(Force(distance + 0.05f))); }
    }

    [Test]
    public void DeploymentSeatsBeforeForceDrivenAccelerationAndKeepsInertia()
    {
        var host = new HostVehicleSession(99);
        Grant(host);
        Step(host);
        var installed = host.Items.Mines.Single();
        for (int i = 0; i < 29; i++) { Step(host); }
        Assert.That(host.Items.Mines.Single().Position, Is.EqualTo(installed.Position));
        Step(host);
        var moving = host.Items.Mines.Single();
        Assert.That(moving.Velocity.Z, Is.LessThan(0));
        Assert.That(Vector3.Distance(moving.Position, installed.Position), Is.InRange(0.0001, 0.02));
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["items.mine_maximum_force"] = 0, ["items.mine_minimum_force"] = 0 }, out _), Is.True);
        Step(host);
        Assert.That(host.Items.Mines.Single().Velocity.Z, Is.LessThan(0), "removing force does not delete momentum");
    }

    [Test]
    public void InstalledMineRemainsDormantAfterSeatingUntilAnInRangeVehiclePullsIt()
    {
        var host = new HostVehicleSession(99);
        Grant(host); Step(host);
        var installed = host.Items.Mines.Single();
        VehicleObservation Distant(VehicleSnapshot state) => new(new VehiclePhysicsState(installed.Position + Vector3.UnitX * 40, Quaternion.Identity, Vector3.Zero, Vector3.Zero), Vector3.UnitY);
        for (int i = 0; i < 180; i++) { host.Step(default, Distant, moveMine: Move); }
        Assert.That(host.Items.Mines.Single().Position, Is.EqualTo(installed.Position));
        Assert.That(host.Items.Mines.Single().Velocity, Is.EqualTo(Vector3.Zero));
        host.Step(default, state => new(new VehiclePhysicsState(installed.Position + Vector3.UnitX * 12, Quaternion.Identity, Vector3.Zero, Vector3.Zero), Vector3.UnitY), moveMine: Move);
        Assert.That(host.Items.Mines.Single().Velocity.X, Is.GreaterThan(0));
    }

    [Test]
    public void FailedPlacementCapAndStaleUseRetainExactCapability()
    {
        var host = new HostVehicleSession(99);
        Grant(host);
        var slot = host.Items.Slots.Single();
        host.Step(default, Observe, moveMine: Move);
        Assert.That(host.Items.Slots.Single(), Is.EqualTo(slot));
        Assert.That(host.Items.Mines, Is.Empty);
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.True);
        Step(host);
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.False);
        for (int i = 1; i < ItemAuthority.MaximumMines; i++) { Grant(host); Step(host); }
        Grant(host);
        var capped = host.Items.Slots.Single();
        Step(host);
        Assert.That(host.Items.Mines.Count, Is.EqualTo(ItemAuthority.MaximumMines));
        Assert.That(host.Items.Slots.Single(), Is.EqualTo(capped));
    }

    [Test]
    public void ContactAppliesOneAttributedDamageAndLargeImpulseToOnlyTheContactedVehicle()
    {
        var host = new HostVehicleSession(99, new ItemConfiguration { MineDamage = 35 });
        host.JoinPlayer(10, 2);
        Grant(host);
        Step(host);
        float hp = host.World.GetVehicle(2).Damage.CurrentHP;
        host.Step(default, Observe, moveMine: (_, candidate) => new(candidate, 2));
        Assert.That(host.Items.Mines, Is.Empty);
        var victim = host.World.GetVehicle(2);
        Assert.That(victim.Damage.CurrentHP, Is.EqualTo(hp - 35));
        Assert.That(victim.Damage.LastDamage!.Attribution.Source, Is.EqualTo("proxy-mine"));
        Assert.That(victim.Damage.LastDamage.Attribution.InstigatorId, Is.EqualTo(1));
        Assert.That(victim.Damage.LastDamage.Amount, Is.EqualTo(35));
        Assert.That(victim.Effects.Single().Effect.Impulse.Length(), Is.EqualTo(24000).Within(0.1));
        Assert.That(host.World.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(hp));
        Assert.That(host.Items.Events.Count(e => e.Impact), Is.EqualTo(1));
        Step(host);
        Assert.That(host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(hp - 35));
        Assert.That(host.Items.Events, Is.Empty);
    }

    [Test]
    public void OwnerCanTriggerDuringSeatingAndActualDamageIsClamped()
    {
        var host = new HostVehicleSession(99, new ItemConfiguration { MineDamage = 10000 });
        Grant(host);
        Step(host);
        float hp = host.World.GetVehicle(1).Damage.CurrentHP;
        host.Step(default, Observe, moveMine: (_, candidate) => new(candidate, 1));
        Assert.That(host.Items.Mines, Is.Empty);
        Assert.That(host.World.GetVehicle(1).Damage.LastDamage!.Amount, Is.EqualTo(hp));
        Assert.That(host.World.GetVehicle(1).Damage.Destroyed, Is.True);
    }

    [Test]
    public void InvalidAdapterAndRejectedWorldBatchCannotPartlyCommitDeployment()
    {
        var host = new HostVehicleSession(99);
        Grant(host);
        var slot = host.Items.Slots.Single();
        Assert.Throws<ArgumentException>(() => host.Step(default, Observe, placeMine: Place, moveMine: (_, candidate) => new(candidate with { Position = new(float.NaN, 0, 0) })));
        Assert.That(host.Items.Mines, Is.Empty);
        Assert.That(host.Items.Slots.Single(), Is.EqualTo(slot));
        var input = new InputFrame(1, 0, 0, 0, 0, 0, 0);
        Assert.Throws<ArgumentException>(() => host.Items.Step(host.World, input, [new VehicleStepRequest(1, default, Observe(host.World.GetVehicle(1)))], (_, _) => null, placeMine: Place, moveMine: Move));
        Assert.That(host.World.State.Tick, Is.Zero);
        Step(host);
        Assert.That(host.Items.Mines.Count, Is.EqualTo(1));
    }

    [Test]
    public void MovingAndSeatedMinesSurviveCodecResumeLateJoinMigrationAndDeployerRemoval()
    {
        var host = new HostVehicleSession(99);
        host.JoinPlayer(10, 2);
        Grant(host);
        for (int i = 0; i < 45; i++) { Step(host); }
        Grant(host);
        Step(host);
        var publication = new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], [], mines: host.Items.Mines);
        var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(publication, host.World.State.Match!, null, host.Configuration)));
        var restored = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 2);
        Assert.That(restored.Items.Mines, Is.EqualTo(host.Items.Mines));
        Assert.That(host.PrepareJoin(3, 2)!.Items.Mines, Is.EqualTo(host.Items.Mines));
        for (int i = 0; i < 5; i++) { Step(host); Step(restored); }
        Assert.That(restored.Items.Mines, Is.EqualTo(host.Items.Mines));
        restored.Items.RemovePlayer(1);
        Assert.That(restored.Items.Mines.Count, Is.EqualTo(2));
        Assert.That(new HostVehicleSession(100).Items.Mines, Is.Empty);
    }

    [Test]
    public void FullBoundCodecRejectsDuplicateInvalidAndFutureTokenState()
    {
        var host = new HostVehicleSession(99);
        var mines = Enumerable.Range(1, ItemAuthority.MaximumMines).Select(id => new ProxyMineState((ulong)id, 999, Vector3.Zero, new Vector3(0, 0, 10), Vector3.UnitY, 0)).ToArray();
        var state = new ItemPublication(1, host.Snapshot(), [], [], [], mines: mines);
        Assert.That(ItemCodec.DecodeState(ItemCodec.EncodeState(state)).Mines, Is.EqualTo(mines));
        Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), [], [], [], mines: [mines[0], mines[0]]));
        Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), [], [], [], mines: [mines[0] with { Velocity = new(100, 0, 0) }]));
        Assert.Throws<ArgumentException>(() => new ItemAuthority().Restore(state, 1, 15));
        byte[] bytes = ItemCodec.EncodeState(state);
        bytes[2] = 6;
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeState(bytes));
    }

    [Test]
    public void LethalMineUsesExistingKillRuleAndFinishedClearsOtherHazardsInSameCommit()
    {
        var host = new HostVehicleSession(99, new ItemConfiguration { MineDamage = 10000 },
            matchConfiguration: new() { MinimumPlayers = 1, CountdownTicks = 1, KillTarget = 1 });
        host.JoinPlayer(10, 2);
        Step(host); Step(host);
        Grant(host); Step(host);
        Grant(host); Step(host);
        ulong first = host.Items.Mines[0].Id;
        host.Step(default, Observe, moveMine: (_, candidate) => new(candidate, candidate.Id == first ? 2ul : 0ul));
        Assert.That(host.World.State.Match!.Phase, Is.EqualTo(Trackstorm.Core.Matches.MatchPhase.Finished));
        Assert.That(host.World.State.Match.Players.Single(p => p.Player == 1).Kills, Is.EqualTo(1));
        Assert.That(host.Items.Mines, Is.Empty);
        Step(host);
        Assert.That(host.World.State.Match.Players.Single(p => p.Player == 1).Kills, Is.EqualTo(1));
    }

    [Test]
    public void AllMineTuningUsesExistingCatalogCodecAndValidation()
    {
        var values = new Dictionary<string, double> { ["items.mine_damage"] = 42, ["items.mine_attraction_radius"] = 30,
            ["items.mine_minimum_force"] = 20, ["items.mine_maximum_force"] = 1500, ["items.mine_falloff"] = 3, ["items.mine_knockback"] = 30000 };
        Assert.That(GameplayOptions.TryApply(new(), values, out var configuration, out _), Is.True);
        var decoded = GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(99, new(1, configuration)));
        foreach (var value in values) { Assert.That(GameplayOptions.All.Single(option => option.Key == value.Key).Read(decoded.State.Configuration), Is.EqualTo(value.Value)); }
        Assert.That(ProxyMineState.AttractionForce(10, configuration.Items), Is.Not.EqualTo(ProxyMineState.AttractionForce(10, new())));
        Assert.That(GameplayOptions.TryApply(configuration, new Dictionary<string, double> { ["items.mine_maximum_force"] = 1 }, out _, out _), Is.False);
        Assert.Throws<ArgumentException>(() => (new ItemConfiguration { MineFalloff = float.NaN }).Validate());
    }

    private static void Grant(HostVehicleSession host)
    {
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.ProxyMine), Is.True);
        var slot = host.Items.Slots.Single(s => s.Vehicle == 1);
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.True);
    }
    private static void Step(HostVehicleSession host) => host.Step(default, Observe, placeMine: Place, moveMine: Move);
    private static ProxyMineMotion Move(ProxyMineState previous, ProxyMineState candidate) => new(candidate);
    private static ProxyMineState Place(ItemSlot slot, VehiclePhysicsState pose) => new(slot.Token, slot.Vehicle, pose.Position + Vector3.UnitZ * 4.5f, Vector3.Zero, Vector3.UnitY, 30);
    private static VehicleObservation Observe(VehicleSnapshot state) => new(state.Movement.Physics, Vector3.UnitY);
}
