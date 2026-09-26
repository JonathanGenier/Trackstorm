using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

/// <summary>Observable ammunition, hit authority, lifecycle and recovery invariants.</summary>
[TestFixture]
internal sealed class MachineGunTests
{
    [Test]
    public void EightHundredActualRoundsExhaustInTenSecondsAndCannotRepeat()
    {
        var host = Create();
        var slot = Grant(host);
        int shots = 0;
        WeaponRayHit? Miss(ulong owner, Vector3 start, Vector3 end)
        {
            Assert.That(owner, Is.EqualTo(1));
            Assert.That(Vector3.Distance(start, end), Is.EqualTo(225).Within(0.0001));
            shots++;
            return null;
        }
        for (int i = 0; i < 599; i++) { Step(host, true, Miss); }
        Assert.That(shots, Is.EqualTo(798));
        Assert.That(host.Items.Slots.Single().Ammo!.Remaining, Is.EqualTo(2));
        Step(host, true, Miss);
        Assert.That(shots, Is.EqualTo(800));
        Assert.That(host.Items.Slots.Single(), Is.EqualTo(slot with { Item = HeldItem.None, Ammo = null }));
        for (int i = 0; i < 60; i++) { Step(host, true, Miss); }
        Assert.That(shots, Is.EqualTo(800));
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.False);
        Assert.That(host.World.Events.Entries.Count(e => e.Kind == "Exhausted"), Is.EqualTo(1));
    }

    [Test]
    public void ReleaseReuseSwitchAndRepeatedAcquisitionPreservePhysicalResources()
    {
        var host = Create();
        var first = Grant(host);
        for (int i = 0; i < 60; i++) { Step(host, true); }
        Assert.That(host.Items.Slots.Single().Ammo!.Remaining, Is.EqualTo(720));
        Step(host);
        var paused = host.Items.Slots.Single();
        for (int i = 0; i < 30; i++) { Step(host); }
        Assert.That(host.Items.Slots.Single(), Is.EqualTo(paused));
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.MachineGun), Is.True);
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.Wrench), Is.False);
        Assert.That(host.SwitchItem(0, 99, 1, 1), Is.True);
        Step(host, true);
        Assert.That(host.Items.Slots.Single().SecondAmmo!.Remaining, Is.EqualTo(800));
        Assert.That(host.UseItem(0, 99, 1, first.Token), Is.False);
        var second = host.Items.Slots.Single();
        Assert.That(host.UseItem(0, 99, 1, second.SecondToken), Is.True);
        for (int i = 0; i < 1200; i++) { Step(host, true); }
        Assert.That(host.Items.Slots.Single().Ammo, Is.EqualTo(paused.Ammo));
        Assert.That(host.Items.Slots.Single().SecondItem, Is.EqualTo(HeldItem.None));
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.MachineGun), Is.True);
        Assert.That(host.Items.Slots.Single().SecondAmmo!.Percentage, Is.EqualTo(100));
        Assert.That(host.UseItem(0, 99, 1, second.SecondToken), Is.False);
    }

    [TestCase(0.02f, 2.25f)]
    [TestCase(12f / 225f, 2.25f)]
    [TestCase(0.6f, 0.6179834963276867f)]
    [TestCase(0.8f, 0.21849016045733952f)]
    [TestCase(1f, 0f)]
    public void NativeClosestHitAppliesBoundedFalloffAndAttributedSmallImpulse(float fraction, float damage)
    {
        var host = Create(new() { MachineGunFireRate = 60, MachineGunSpread = 0 });
        Grant(host);
        Step(host, true, (_, _, _) => new(fraction, 2));
        var target = host.World.GetVehicle(2);
        Assert.That(target.Damage.CurrentHP, Is.EqualTo(1000 - damage).Within(0.001));
        if (damage > 0)
        {
            Assert.That(target.Damage.LastDamage!.Attribution.Source, Is.EqualTo("machine-gun"));
            Assert.That(target.Damage.LastDamage.Attribution.InstigatorId, Is.EqualTo(1));
            Assert.That(target.Effects.Single().Effect.Impulse.Length(), Is.EqualTo(8 * damage / 2.25).Within(0.001));
        }
        else { Assert.That(target.Damage.LastDamage, Is.Null); }
        Assert.That(host.World.State.Match!.Players.Single(p => p.Player == 1).CircusScore, Is.Zero);
        Assert.That(host.Items.Slots.Single().Ammo!.Remaining, Is.EqualTo(799));
    }

    [Test]
    public void CoverMissAndMissingAdapterCannotInventDamageOrSpendUnfiredRounds()
    {
        var host = Create(new() { MachineGunFireRate = 60 });
        Grant(host);
        host.Step(Held, Observe);
        Assert.That(host.Items.Slots.Single().Ammo!.Remaining, Is.EqualTo(800));
        Step(host, true, (_, _, _) => new(0.1f, 0));
        Step(host, true);
        Assert.That(host.Items.Slots.Single().Ammo!.Remaining, Is.EqualTo(798));
        Assert.That(host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(1000));
    }

    [TestCase(float.NaN, 2ul)]
    [TestCase(-0.1f, 2ul)]
    [TestCase(1.01f, 2ul)]
    [TestCase(0.5f, 1ul)]
    [TestCase(0.5f, 99ul)]
    public void InvalidHostObservationRollsBackAmmoWorldAndPendingUse(float fraction, ulong vehicle)
    {
        var host = Create(new() { MachineGunFireRate = 60 });
        var slot = Grant(host);
        ulong tick = host.World.State.Tick;
        Assert.Throws<ArgumentException>(() => Step(host, true, (_, _, _) => new(fraction, vehicle)));
        Assert.That(host.World.State.Tick, Is.EqualTo(tick));
        Assert.That(host.Items.Slots.Single(), Is.EqualTo(slot));
        Step(host, true);
        Assert.That(host.Items.Slots.Single().Ammo!.Remaining, Is.EqualTo(799));
    }

    [Test]
    public void CheckpointReplacementRetainsAmmoCadenceAndDeterministicSpreadWithoutHistoricalShots()
    {
        var host = Create();
        Grant(host);
        for (int i = 0; i < 31; i++) { Step(host, true); }
        host.Items.Grant(host.World, 1, HeldItem.MachineGun);
        var publication = new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], []);
        var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(publication, host.World.State.Match!, null, host.Configuration)));
        var restored = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 2);
        Assert.That(restored.Items.Slots, Is.EqualTo(host.Items.Slots));
        Assert.That(restored.Items.Events, Is.Empty);
        Assert.That(host.PrepareJoin(3, 2)!.Items.Slots, Is.EqualTo(host.Items.Slots));
        Step(host); Step(restored);
        Assert.That(restored.Items.Slots, Is.EqualTo(host.Items.Slots));
        Assert.That(restored.ResumePlayer(20, 1), Is.True);
        var slot = host.Items.Slots.Single();
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.True);
        Assert.That(restored.UseItem(20, 99, slot.Life, slot.Token, 1), Is.True);
        var original = new List<Vector3>(); var replay = new List<Vector3>();
        for (uint i = 1; i <= 60; i++)
        {
            Assert.That(restored.Receive(20, 99, [new SequencedInput(i, Held)], slot.Life), Is.True);
            Step(host, true, (_, _, end) => { original.Add(end); return null; });
            Step(restored, false, (_, _, end) => { replay.Add(end); return null; });
        }
        Assert.That(replay, Is.EqualTo(original));
        Assert.That(restored.Items.Slots, Is.EqualTo(host.Items.Slots));
        restored.Suspend(20);
        Step(restored);
        var remaining = restored.Items.Slots.Single().Ammo;
        for (int i = 0; i < 30; i++) { Step(restored); }
        Assert.That(restored.Items.Slots.Single().Ammo, Is.EqualTo(remaining));
    }

    [Test]
    public void CodecAndConfigRejectInvalidContinuationAndLiveCapacityNeverRefills()
    {
        var host = Create();
        Grant(host); Step(host, true);
        var ammo = host.Items.Slots.Single().Ammo;
        var values = new Dictionary<string, double> { ["items.machine_gun_capacity"] = 100, ["items.machine_gun_fire_rate"] = 60,
            ["items.machine_gun_range"] = 9, ["items.machine_gun_damage"] = 4, ["items.machine_gun_falloff_start"] = 4,
            ["items.machine_gun_falloff"] = 2, ["items.machine_gun_spread"] = 2, ["items.machine_gun_knockback"] = 5, ["items.machine_gun_tracer_every"] = 3 };
        Assert.That(host.TryConfigure(0, values, out _), Is.True);
        Assert.That(host.Items.Slots.Single().Ammo, Is.EqualTo(ammo));
        var decoded = GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(99, host.Configuration));
        Assert.That(decoded.State.Configuration, Is.EqualTo(host.Configuration.Configuration));
        host.Items.Grant(host.World, 1, HeldItem.MachineGun);
        Assert.That(host.Items.Slots.Single().SecondAmmo, Is.EqualTo(new MachineGunAmmo(100, 100)));
        var publication = new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], []);
        Assert.That(ItemCodec.DecodeState(ItemCodec.EncodeState(publication)).Slots, Is.EqualTo(host.Items.Slots));
        foreach (var invalid in new MachineGunAmmo?[] { null, new(0, 500), new(501, 500), new(1, 10001), new(1, 500, double.NaN), new(1, 500, 1) })
        {
            Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), [host.Items.Slots.Single() with { Ammo = invalid }], [], []));
        }
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["items.machine_gun_fire_rate"] = 121 }, out _), Is.False);
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["items.machine_gun_range"] = 3 }, out _), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DeathAndResetRetireCapabilitiesAndStopFiring(bool death)
    {
        var host = Create(new() { MachineGunFireRate = 60 });
        var slot = Grant(host); Step(host, true);
        var frame = new InputFrame(host.World.State.Tick + 1, 0, 0, 0, InputButtons.UseItem, 0, 0);
        host.Items.Step(host.World, frame, host.World.State.Vehicles.Select(v => new VehicleStepRequest(v.VehicleId, frame, Observe(v),
            death && v.VehicleId == 1 ? [new VehicleEffectRequest(new DamageEffect(10000, Vector3.Zero, Vector3.Zero), new DamageContext("world", 0, "test"))] : [],
            reset: !death && v.VehicleId == 1 ? v.ObservedPhysics : null)).ToArray(), (_, _) => null, raycastWeapon: (_, _, _) => null);
        Assert.That(host.Items.Slots, Is.Empty);
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.False);
        Assert.That(new HostVehicleSession(100).Items.Slots, Is.Empty);
    }

    [Test]
    public void RemoteUseWaitsForItsInputSequenceAndDisconnectNeutralizesFire()
    {
        var host = Create(new() { MachineGunFireRate = 60 });
        host.Items.Grant(host.World, 2, HeldItem.MachineGun);
        var slot = host.Items.Slots.Single();
        Assert.That(host.UseItem(0, 99, 1, slot.Token), Is.False);
        Assert.That(host.UseItem(10, 98, 1, slot.Token), Is.False);
        Assert.That(host.UseItem(10, 99, 1, slot.Token, 3), Is.True);
        host.Receive(10, 99, [new SequencedInput(1, Held)]); Step(host);
        host.Receive(10, 99, [new SequencedInput(2, default)]); Step(host);
        Assert.That(host.Items.Slots.Single().Ammo!.Remaining, Is.EqualTo(800));
        host.Receive(10, 99, [new SequencedInput(3, Held)]); Step(host);
        Assert.That(host.Items.Slots.Single().Ammo!.Remaining, Is.EqualTo(799));
        host.Suspend(10); Step(host);
        Assert.That(host.Items.Slots.Single().Ammo!.Remaining, Is.EqualTo(799));
    }

    [Test]
    public void LethalRoundUsesExistingKillAttributionWithoutDamageScore()
    {
        var host = Create(new() { MachineGunFireRate = 60, MachineGunDamage = 1000, MachineGunSpread = 0 });
        Grant(host);
        Step(host, true, (_, _, _) => new(0.02f, 2));
        Assert.That(host.World.GetVehicle(2).Damage.Destroyed, Is.True);
        Assert.That(host.World.State.Match!.Players.Single(p => p.Player == 1).Kills, Is.EqualTo(1));
        Assert.That(host.World.State.Match.Players.Single(p => p.Player == 1).CircusScore, Is.EqualTo(host.Configuration.Configuration.Match.BaseKillPoints));
        Step(host);
        Assert.That(host.World.State.Match.Players.Single(p => p.Player == 1).Kills, Is.EqualTo(1));
    }

    [Test]
    public void DefaultConeExpandsWithDistanceAndFollowsEveryCurrentOrientation()
    {
        var host = Create();
        Grant(host);
        var slopes = new List<Vector2>();
        for (int tick = 0; tick < 1200; tick++)
        {
            // Change heading and pitch every tick: a cached launch orientation cannot pass.
            var orientation = Quaternion.CreateFromYawPitchRoll(tick * 0.01f, 0.15f * MathF.Sin(tick * 0.03f), 0);
            host.Step(Held, state => new(new VehiclePhysicsState(state.ObservedPhysics.Position, orientation, Vector3.Zero, Vector3.Zero), Vector3.UnitY),
                raycastWeapon: (_, start, end) =>
                {
                    Assert.That(Vector3.Distance(start, end), Is.EqualTo(225).Within(0.0001));
                    var local = Vector3.Transform(Vector3.Normalize(end - start), Quaternion.Inverse(orientation));
                    Assert.That(-local.Z, Is.GreaterThanOrEqualTo(MathF.Cos(MathF.PI / 30) - 0.000001));
                    slopes.Add(new(local.X / -local.Z, local.Y / -local.Z));
                    return null;
                });
        }
        Assert.That(slopes.Count, Is.EqualTo(800));
        var rates = new List<double>();
        foreach (float distance in new[] { 5f, 15f, 224f })
        {
            var pattern = slopes.Select(p => p * distance).ToArray();
            float width = pattern.Max(p => p.X) - pattern.Min(p => p.X);
            int hits = pattern.Count(p => Math.Abs(p.X) <= 1 && Math.Abs(p.Y) <= 0.75);
            rates.Add(hits / 800.0);
            TestContext.Out.WriteLine($"Cone plane {distance} m: width {width:F4} m, 2 x 1.5 m target {hits}/800 ({hits / 8.0:F2}%).");
            Assert.That(width, Is.InRange(distance * 0.19f, distance * 0.211f));
        }
        Assert.That(rates[0], Is.EqualTo(1));
        Assert.That(rates[1], Is.InRange(0.3, 0.7));
        Assert.That(rates[2], Is.LessThan(rates[1] * 0.65));
        Assert.That(new ItemConfiguration().MachineGunDamage * 800, Is.EqualTo(1800));
    }

    [Test]
    public void MultipleRoundsInOneTickHaveIndependentSpreadTracerAndDamage()
    {
        var host = Create(new() { MachineGunFireRate = 120 });
        Grant(host);
        var ends = new List<Vector3>();
        Step(host, true, (_, _, end) => { ends.Add(end); return new(0.02f, 2); });
        Assert.That(ends.Count, Is.EqualTo(2));
        Assert.That(ends[0], Is.Not.EqualTo(ends[1]));
        Assert.That(host.Items.Slots.Single().Ammo!.Remaining, Is.EqualTo(798));
        Assert.That(host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(995.5));
        Assert.That(host.Items.Events.Select(e => e.Tracer), Is.EqualTo(new[] { true, false }));
        var publication = new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], host.Items.Events);
        Assert.That(ItemCodec.DecodeState(ItemCodec.EncodeState(publication)).Events, Is.EqualTo(publication.Events));
    }

    [Test]
    public void LastPartialTickNeverFiresMoreThanRemainingAmmo()
    {
        var host = Create(new() { MachineGunCapacity = 3, MachineGunFireRate = 120 });
        Grant(host);
        int rays = 0;
        for (int i = 0; i < 4; i++) { Step(host, true, (_, _, _) => { rays++; return null; }); }
        Assert.That(rays, Is.EqualTo(3));
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.None));
        Assert.That(host.World.Events.Entries.Count(e => e.Kind == "Exhausted"), Is.EqualTo(1));
    }

    [Test]
    public void InvalidSecondRayRollsBackTheWholeFiringTick()
    {
        var host = Create(new() { MachineGunFireRate = 120 });
        var slot = Grant(host);
        ulong tick = host.World.State.Tick;
        int rays = 0;
        Assert.Throws<ArgumentException>(() => Step(host, true, (_, _, _) => ++rays == 1 ? new(0.02f, 2) : new(float.NaN, 2)));
        Assert.That(host.World.State.Tick, Is.EqualTo(tick));
        Assert.That(host.Items.Slots.Single(), Is.EqualTo(slot));
        Assert.That(host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(1000));
        Step(host, true);
        Assert.That(host.Items.Slots.Single().Ammo!.Remaining, Is.EqualTo(798));
    }

    private static InputFrame Held => new(0, 0, 0, 0, InputButtons.UseItem, 0, 0);
    private static HostVehicleSession Create(ItemConfiguration? items = null)
    {
        var host = new HostVehicleSession(99, items, damageConfiguration: new() { MaxHP = 1000 },
            matchConfiguration: new() { MinimumPlayers = 1, CountdownTicks = 1, KillTarget = 100 });
        host.JoinPlayer(10, 2);
        Step(host); Step(host);
        return host;
    }
    private static ItemSlot Grant(HostVehicleSession host)
    {
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.MachineGun), Is.True);
        var slot = host.Items.Slots.Single();
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.True);
        return slot;
    }
    private static void Step(HostVehicleSession host, bool held = false, Func<ulong, Vector3, Vector3, WeaponRayHit?>? ray = null) =>
        host.Step(held ? Held : default, Observe, raycastWeapon: ray ?? ((_, _, _) => null));
    private static VehicleObservation Observe(VehicleSnapshot state) => new(new VehiclePhysicsState(state.ObservedPhysics.Position, Quaternion.Identity, Vector3.Zero, Vector3.Zero), Vector3.UnitY);
}

