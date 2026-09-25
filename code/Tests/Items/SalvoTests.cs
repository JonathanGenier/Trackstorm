using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class SalvoTests
{
    [Test]
    public void LaterRoundsFollowCurrentPositionAndHeadingWhileFlyingRoundsKeepTheirTarget()
    {
        var host = new HostVehicleSession(99);
        GrantUse(host); host.Step(default, Observe, ground: Ground);
        var target = host.Items.Missiles[0].Arc!.Target;
        var origin = host.World.GetVehicle(1).ObservedPhysics.Position;
        var moving = new VehiclePhysicsState(origin + Vector3.UnitX * 10, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.5f), Vector3.Zero, Vector3.Zero);
        for (int i = 0; i < 30; i++) { host.Step(default, _ => new(moving, Vector3.UnitY), ground: Ground); }
        var round = host.Items.Missiles.Single(m => m.Arc!.ElapsedTicks == 1);
        Assert.That(round.Arc!.Origin, Is.EqualTo(moving.Position + Vector3.UnitY * 3));
        Assert.That(round.Arc.Target, Is.EqualTo(Ground(SalvoFlight.Aim(moving, 65))));
        Assert.That(round.Arc.Target, Is.Not.EqualTo(target));
        Assert.That(host.Items.Missiles.Single(m => m.Arc!.ElapsedTicks > 1).Arc!.Target, Is.EqualTo(target));
    }

    [Test]
    public void FiveHalfSecondLaunchesProduceFiveImpactsForStationaryVehicle()
    {
        var host = new HostVehicleSession(99);
        GrantUse(host);
        var launches = new List<ulong>();
        var impacts = new List<ItemEvent>();
        Vector3? target = null;
        float highest = 0;
        for (int i = 0; i < 240; i++)
        {
            host.Step(default, Observe, ground: Ground);
            if (i == 0)
            {
                Assert.That(host.Items.Missiles.Count, Is.EqualTo(5));
                target = host.Items.Missiles[0].Arc!.Target;
                Assert.That(target.Value.Z, Is.EqualTo(host.World.GetVehicle(1).ObservedPhysics.Position.Z - 65));
                Assert.That(host.Items.Missiles.Select(m => m.Id).Distinct().Count(), Is.EqualTo(5));
                Assert.That(host.Items.Missiles[0].Arc!.Origin.Y, Is.EqualTo(host.World.GetVehicle(1).ObservedPhysics.Position.Y + 3));
                Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.None));
            }
            foreach (var m in host.Items.Missiles)
            {
                Assert.That(m.Arc!.Target, Is.EqualTo(target));
                highest = Math.Max(highest, m.Position.Y);
            }
            foreach (var e in host.Items.Events)
            {
                if (e.Impact) { impacts.Add(e); } else { launches.Add(host.World.State.Tick); }
            }
        }
        Assert.That(launches, Is.EqualTo(new ulong[] { 1, 31, 61, 91, 121 }));
        Assert.That(highest, Is.GreaterThan(12));
        Assert.That(impacts.Count, Is.EqualTo(5));
        Assert.That(impacts.All(e => e.Item == HeldItem.Salvo && e.Position == target), Is.True);
        Assert.That(host.Items.Missiles, Is.Empty);
    }

    [Test]
    public void ResumeAndReplacementHostContinueScheduledRoundsWithoutReplay()
    {
        var host = new HostVehicleSession(99);
        host.Join(42);
        GrantUse(host);
        for (int i = 0; i < 10; i++) { host.Step(default, Observe, ground: Ground); }
        var publication = ItemCodec.DecodeState(ItemCodec.EncodeState(new(1, host.Snapshot(), host.Items.Slots, host.Items.Missiles, [])));
        var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new ResumeCheckpoint(publication, host.World.State.Match!, null, host.Configuration)));
        var restored = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 2);
        Assert.That(restored.Items.Events, Is.Empty);
        Assert.That(restored.Items.Missiles, Is.EqualTo(host.Items.Missiles));
        var start = host.World.GetVehicle(1).Movement.Physics.Position;
        for (int i = 0; i < 240; i++)
        {
            var moving = new VehiclePhysicsState(start + Vector3.UnitX * (i * 0.05f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, i * 0.002f), Vector3.Zero, Vector3.Zero);
            VehicleObservation ObserveMoving(VehicleSnapshot v) => v.VehicleId == 1 ? new(moving, Vector3.UnitY) : Observe(v);
            host.Step(default, ObserveMoving, ground: Ground);
            restored.Step(default, ObserveMoving, ground: Ground);
            Assert.That(restored.Items.Missiles, Is.EqualTo(host.Items.Missiles));
            Assert.That(restored.Items.Events, Is.EqualTo(host.Items.Events));
        }
        Assert.That(restored.Items.Missiles, Is.Empty);
        GrantUse(restored, 2);
        restored.Step(default, Observe, ground: Ground);
        Assert.That(restored.Items.Missiles.Count, Is.EqualTo(5));
        Assert.That(restored.Items.Missiles.Min(m => m.Id), Is.GreaterThan(publication.Missiles.Max(m => m.Id)));
    }

    [Test]
    public void MissingGroundAtLaterLaunchCancelsOnlyThatRoundInsteadOfUsingOldAim()
    {
        var host = new HostVehicleSession(99, new ItemConfiguration { SalvoCount = 3, SalvoIntervalTicks = 2 });
        GrantUse(host); host.Step(default, Observe, ground: Ground);
        var ids = host.Items.Missiles.Select(m => m.Id).ToArray();
        host.Step(default, Observe);
        host.Step(default, Observe);
        Assert.That(host.Items.Missiles.Select(m => m.Id), Is.EquivalentTo(new[] { ids[0], ids[2] }));
        Assert.That(host.Items.Events, Is.Empty);
        host.Step(default, Observe, ground: Ground);
        host.Step(default, Observe, ground: Ground);
        Assert.That(host.Items.Events.Single().Token, Is.EqualTo(ids[2]));
        Assert.That(host.Items.Missiles.All(m => m.Launched), Is.True);
    }

    [Test]
    public void MissingGroundCapacityAndRejectedBatchPreserveInventoryAndTokens()
    {
        var host = new HostVehicleSession(99);
        GrantUse(host);
        host.Step(default, Observe);
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.Salvo));
        var token = host.Items.TokenHighWater;
        host.Items.RequestUse(host.World, 1, 1, token);
        Assert.Throws<ArgumentException>(() => host.Step(default, Observe, (_, _) => float.NaN, ground: Ground));
        Assert.That(host.Items.TokenHighWater, Is.EqualTo(token));
        Assert.That(host.Items.Missiles, Is.Empty);
        host.Step(default, Observe, ground: Ground);
        for (int i = 0; i < 2; i++) { GrantUse(host); host.Step(default, Observe, ground: Ground); }
        Assert.That(host.Items.Missiles.Count, Is.EqualTo(15));
        GrantUse(host);
        host.Step(default, Observe, ground: Ground);
        Assert.That(host.Items.Missiles.Count, Is.EqualTo(15));
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.Salvo));
    }

    [Test]
    public void RadialFalloffMultipleTargetsAndDistinctActualDamageRemainAuthoritative()
    {
        var host = new HostVehicleSession(99, new ItemConfiguration { SalvoCount = 2, SalvoIntervalTicks = 1, SalvoDamage = 20 });
        host.Join(42); host.Join(43);
        GrantUse(host);
        host.Step(default, Observe, ground: Ground);
        var center = host.Items.Missiles[0].Arc!.Target;
        float previous = 100;
        for (int distance = 0; distance <= 8; distance++)
        {
            var effect = host.Items.Explosion(center, center + Vector3.UnitX * distance, HeldItem.Salvo);
            Assert.That(effect.Damage, Is.LessThanOrEqualTo(previous));
            Assert.That(effect.Impulse.Length(), Is.LessThanOrEqualTo(3500.01));
            previous = effect.Damage;
        }
        for (int i = 0; i < 120; i++)
        {
            host.Step(default, v => v.VehicleId == 1 ? Observe(v) : new(new VehiclePhysicsState(center + Vector3.UnitX * (v.VehicleId == 2 ? 1 : 4), Quaternion.Identity, Vector3.Zero, Vector3.Zero), Vector3.UnitY), ground: Ground);
        }
        var near = host.World.GetVehicle(2).Damage;
        var far = host.World.GetVehicle(3).Damage;
        Assert.That(near.CurrentHP, Is.LessThan(far.CurrentHP));
        Assert.That(near.LastDamage!.Sequence, Is.EqualTo(2));
        Assert.That(far.LastDamage!.Sequence, Is.EqualTo(2));
        Assert.That(near.LastDamage.Attribution.Source, Is.EqualTo("salvo"));
        Assert.That(near.LastDamage.Attribution.InstigatorId, Is.EqualTo(1));
        Assert.That(host.World.GetVehicle(1).Damage.LastDamage, Is.Null);
    }

    [Test]
    public void ResetAndDepartureCancelPendingAndFlyingRounds()
    {
        var host = new HostVehicleSession(99);
        GrantUse(host); host.Step(default, Observe, ground: Ground);
        var input = new InputFrame(host.World.State.Tick + 1, 0, 0, 0, 0, 0, 0);
        var pose = host.World.GetVehicle(1).Movement.Physics;
        host.Items.Step(host.World, input, [new(1, input, new(pose, Vector3.UnitY), reset: pose)], (_, _) => null);
        Assert.That(host.Items.Missiles, Is.Empty);
        GrantUse(host); host.Step(default, Observe, ground: Ground);
        host.Items.RemovePlayer(1);
        Assert.That(host.Items.Missiles, Is.Empty);
        Assert.That(new HostVehicleSession(100).Items.Missiles, Is.Empty);
    }

    [Test]
    public void LiveTuningRoundTripsAndDoesNotBendExistingArc()
    {
        var host = new HostVehicleSession(99);
        GrantUse(host); host.Step(default, Observe, ground: Ground);
        var before = host.Items.Missiles.ToArray();
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["items.salvo_range"] = 80, ["items.salvo_speed"] = 100, ["items.salvo_damage"] = 40, ["items.missile_speed"] = 100 }, out _), Is.True);
        Assert.That(host.Items.Missiles, Is.EqualTo(before));
        Assert.That(GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(99, host.Configuration)).State, Is.EqualTo(host.Configuration));
        Assert.That(host.TryConfigure(42, new Dictionary<string, double> { ["items.salvo_count"] = 1 }, out _), Is.False);
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["items.salvo_count"] = 17 }, out _), Is.False);
        var broken = before[0] with { Arc = before[0].Arc! with { ElapsedTicks = 9999 } };
        Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), host.Items.Slots, [broken], []));
    }

    private static void GrantUse(HostVehicleSession host, ulong player = 1)
    {
        Assert.That(host.Items.Grant(host.World, player, HeldItem.Salvo), Is.True);
        var slot = host.Items.Slots.Single(s => s.Vehicle == player);
        Assert.That(host.Items.RequestUse(host.World, player, slot.Life, slot.Active.Token), Is.True);
    }
    private static VehicleObservation Observe(VehicleSnapshot state) => new(state.Movement.Physics, Vector3.UnitY);
    private static Vector3? Ground(Vector3 point) => new Vector3(point.X, 0, point.Z);
}
