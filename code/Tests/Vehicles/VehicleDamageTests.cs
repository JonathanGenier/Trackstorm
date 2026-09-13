using System.Numerics;
using System.Text.Json;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

/// <summary>Authoritative health, attribution, impact and replay invariants without a native engine.</summary>
[TestFixture]
internal sealed class VehicleDamageTests
{
    /// <summary>Damage and repair respect both clamps; negative damage cannot heal and wrecks require reset.</summary>
    [Test]
    public void Health_ClampsDamageRepairAndDeathTransitions()
    {
        var health = new VehicleHealth(new DamageConfiguration { MaxHP = 80 });
        var source = new DamageContext("missile", 7, "shot-42");
        Assert.That(health.ApplyDamage(-20, source, 1), Is.Null);
        Assert.That(health.State.CurrentHP, Is.EqualTo(80));
        health.ApplyDamage(30, source, 2);
        Assert.That(health.State.CurrentHP, Is.EqualTo(50));
        health.Repair(-20);
        Assert.That(health.State.CurrentHP, Is.EqualTo(50));
        health.Repair(float.MaxValue);
        Assert.That(health.State.CurrentHP, Is.EqualTo(80));
        DamageEvent? death = health.ApplyDamage(float.MaxValue, source, 3);
        Assert.That(death!.Amount, Is.EqualTo(80));
        Assert.That(death.DestroyedTransition, Is.True);
        Assert.That(health.State.CurrentHP, Is.Zero);
        Assert.That(health.State.Destroyed, Is.True);
        Assert.That(health.ApplyDamage(5, source, 4), Is.Null);
        health.Repair(100);
        Assert.That(health.State.CurrentHP, Is.Zero);
        Assert.That(health.State.LastDamage, Is.SameAs(death));
        health.Reset();
        Assert.That(health.State.Destroyed, Is.False);
        Assert.That(health.ApplyDamage(80, source, 1)!.DestroyedTransition, Is.True);
    }

    /// <summary>Requested and actual loss differ under clamping; harmless effects never overwrite attribution.</summary>
    [Test]
    public void Metadata_IsRetainedAndOutcomesAreOrdered()
    {
        var health = new VehicleHealth(new DamageConfiguration());
        var source = new DamageContext("explosion", 123, "blast:456");
        DamageEvent? first = health.ApplyDamage(25, source, 10);
        Assert.That(first!.Attribution, Is.EqualTo(source));
        Assert.That(first.Sequence, Is.EqualTo(1));
        Assert.That(first.Tick, Is.EqualTo(10));
        Assert.That(first.DestroyedTransition, Is.False);
        health.ApplyDamage(0, new DamageContext("collision", 0, "floor"), 11);
        Assert.That(health.State.LastDamage, Is.SameAs(first));
        Assert.That(health.ApplyDamage(90, source, 12)!.Sequence, Is.EqualTo(2));
        Assert.That(health.State.LastDamage!.Amount, Is.EqualTo(75));
        Assert.That(first.Amount, Is.EqualTo(25), "earlier event is immutable");
    }

    /// <summary>Threshold and cap are tunable and damage is monotonic across all severities tested.</summary>
    [Test]
    public void CollisionDamage_IsHarmlessBelowThresholdAndMonotonic()
    {
        var configuration = new DamageConfiguration { CollisionThreshold = 5, CollisionScale = 2, MaximumCollisionDamage = 30 };
        float previous = 0;
        for (int severity = 0; severity <= 100; severity++)
        {
            float damage = VehicleDamageMath.CollisionDamage(severity, configuration);
            Assert.That(damage, Is.InRange(previous, 30));
            if (severity <= 5)
            {
                Assert.That(damage, Is.Zero);
            }

            previous = damage;
        }

        Assert.That(VehicleDamageMath.CollisionDamage(10, configuration), Is.EqualTo(10));
    }

    /// <summary>Both victims use opposing normals/velocities and their own mass; glancing/separating contacts stay safe.</summary>
    [Test]
    public void CollisionSeverity_UsesEachVictimsRelativeApproachAndImpulse()
    {
        Vector3 relative = new(0, 0, -12);
        Assert.That(VehicleDamageMath.CollisionSeverity(relative, Vector3.UnitZ, 0, 900), Is.EqualTo(12));
        Assert.That(VehicleDamageMath.CollisionSeverity(-relative, -Vector3.UnitZ, 0, 600), Is.EqualTo(12));
        Assert.That(VehicleDamageMath.CollisionSeverity(Vector3.Zero, Vector3.UnitZ, 9000, 900), Is.EqualTo(10));
        Assert.That(VehicleDamageMath.CollisionSeverity(Vector3.Zero, -Vector3.UnitZ, 9000, 600), Is.EqualTo(15));
        Assert.That(VehicleDamageMath.CollisionSeverity(new Vector3(30, 0, 2), Vector3.UnitZ, 0, 900), Is.Zero);
        Assert.Throws<ArgumentException>(() => VehicleDamageMath.CollisionSeverity(relative, Vector3.Zero, 0, 900));
        Assert.Throws<ArgumentException>(() => VehicleDamageMath.CollisionSeverity(relative, Vector3.UnitZ, -1, 900));
        Assert.Throws<ArgumentException>(() => VehicleDamageMath.CollisionDamage(-1, new DamageConfiguration()));
    }

    /// <summary>Repeated manifold contacts are gated, the exact next window is allowed, and brushes do not consume it.</summary>
    [Test]
    public void CollisionGate_PreventsRepeatedContactDrain()
    {
        var health = new VehicleHealth(new DamageConfiguration { CollisionCooldownTicks = 12 });
        var contact = new DamageContext("collision", 2, "vehicle");
        Assert.That(health.ApplyCollision(3, contact, 1), Is.Null);
        Assert.That(health.ApplyCollision(8, contact, 2)!.Amount, Is.EqualTo(12));
        for (ulong tick = 2; tick < 14; tick++)
        {
            Assert.That(health.ApplyCollision(8, contact, tick), Is.Null);
        }

        Assert.That(health.ApplyCollision(8, contact, 14)!.Amount, Is.EqualTo(12));
        Assert.That(health.State.CurrentHP, Is.EqualTo(76));
    }

    /// <summary>Center, intermediate, boundary and outside responses fade consistently and retain torque offset.</summary>
    [Test]
    public void Explosion_FadesDamageAndImpulseWithoutSingularity()
    {
        Vector3 offset = new(0.7f, 0, -0.5f);
        DamageEffect center = VehicleDamageMath.Explosion(Vector3.Zero, Vector3.Zero, 10, 80, 9000, offset);
        DamageEffect half = VehicleDamageMath.Explosion(Vector3.Zero, new Vector3(5, 0, 0), 10, 80, 9000, offset);
        DamageEffect edge = VehicleDamageMath.Explosion(Vector3.Zero, new Vector3(10, 0, 0), 10, 80, 9000, offset);
        DamageEffect outside = VehicleDamageMath.Explosion(Vector3.Zero, new Vector3(11, 0, 0), 10, 80, 9000, offset);
        Assert.That(center.Damage, Is.EqualTo(80));
        Assert.That(center.Impulse, Is.EqualTo(Vector3.UnitY * 9000));
        Assert.That(half.Damage, Is.EqualTo(40));
        Assert.That(half.Impulse.Length(), Is.EqualTo(4500).Within(0.01));
        Assert.That(half.Impulse.X, Is.GreaterThan(0));
        Assert.That(half.Impulse.Y, Is.GreaterThan(0));
        Assert.That(Vector3.Cross(half.Offset, half.Impulse).Length(), Is.GreaterThan(0));
        Assert.That(edge.Damage, Is.Zero);
        Assert.That(outside.Impulse, Is.EqualTo(Vector3.Zero));
        Assert.Throws<ArgumentException>(() => VehicleDamageMath.Explosion(Vector3.Zero, Vector3.Zero, 0, 80, 9000, offset));
    }

    /// <summary>JSON round trips the complete scalar health/event state, preserving cooldown and death replay semantics.</summary>
    [Test]
    public void DamageState_RoundTripsAndRestoresWithoutDuplicateDeath()
    {
        var original = new VehicleHealth(new DamageConfiguration());
        var source = new DamageContext("collision", 8, "front impact");
        original.ApplyCollision(15, source, 100);
        string json = JsonSerializer.Serialize(original.State);
        VehicleDamageState restored = JsonSerializer.Deserialize<VehicleDamageState>(json)!;
        Assert.That(restored, Is.EqualTo(original.State));
        var replay = new VehicleHealth(new DamageConfiguration());
        replay.Restore(restored);
        Assert.That(replay.ApplyCollision(15, source, 101), Is.Null);
        Assert.That(replay.ApplyCollision(15, source, 112), Is.EqualTo(original.ApplyCollision(15, source, 112)));
        original.ApplyDamage(100, source, 113);
        replay.Restore(JsonSerializer.Deserialize<VehicleDamageState>(JsonSerializer.Serialize(original.State))!);
        Assert.That(replay.ApplyDamage(100, source, 114), Is.Null);
        Assert.That(replay.State.LastDamage, Is.EqualTo(original.State.LastDamage));
    }

    /// <summary>Invalid data and stale ticks cannot partially mutate authoritative state.</summary>
    [Test]
    public void InvalidDamage_IsRejectedAtomically()
    {
        var health = new VehicleHealth(new DamageConfiguration());
        var source = new DamageContext("generic", 1, string.Empty);
        health.ApplyDamage(10, source, 10);
        VehicleDamageState before = health.State;
        Assert.Throws<ArgumentException>(() => health.ApplyDamage(float.NaN, source, 11));
        Assert.Throws<ArgumentException>(() => health.ApplyDamage(10, source, 9));
        Assert.Throws<ArgumentException>(() => health.Repair(float.PositiveInfinity));
        Assert.Throws<ArgumentException>(() => health.Restore(new VehicleDamageState(50, 50, null, null)));
        Assert.That(health.State, Is.SameAs(before));
        Assert.Throws<ArgumentException>(() => new VehicleDamageState(100, -1, null, null));
        Assert.Throws<ArgumentException>(() => new VehicleDamageState(100, 101, null, null));
        Assert.Throws<ArgumentException>(() => new DamageEffect(1, new Vector3(float.NaN, 0, 0), Vector3.Zero));
        Assert.Throws<ArgumentException>(() => new DamageConfiguration { MaxHP = 0 }.Validate());
    }
}
