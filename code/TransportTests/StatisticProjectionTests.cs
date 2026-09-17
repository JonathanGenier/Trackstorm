using System.Numerics;
using Trackstorm.Client.Statistics;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Transport.Tests;

/// <summary>Diagnostic projections report owning values without leaking contexts or stale item lives.</summary>
[TestFixture]
internal sealed class StatisticProjectionTests
{
    /// <summary>Supported physics and combat fields come directly from the snapshot.</summary>
    [Test]
    public void ReportsSnapshotWithoutExposingTokensOrDamageContext()
    {
        var physics = new VehiclePhysicsState(new Vector3(3, 4, 5), Quaternion.Identity, new Vector3(3, 80, 4), Vector3.Zero);
        var movement = new VehicleState(120, physics, true, true, 0.2f, 0.8f, SurfaceType.Mud);
        var damage = new VehicleDamageState(1000, 750, new DamageEvent(1, 110, 250, new DamageContext("private-source", 7, "credential-canary"), false), 110);
        var state = new VehicleSnapshot(2, 3, movement, damage, physics);
        string text = Text(VehicleStatistics.Capture(state, [new ItemSlot(2, 3, 9988776655, HeldItem.Missile)], 120));
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("HP 750/1000"));
            Assert.That(text, Does.Contain("Speed 5.00 m/s"));
            Assert.That(text, Does.Contain("Mud").And.Contain("Grounded").And.Contain("Missile"));
            Assert.That(text, Does.Not.Contain("credential-canary").And.Not.Contain("private-source").And.Not.Contain("9988776655"));
            Assert.That(state.Damage.CurrentHP, Is.EqualTo(750));
        });
        Assert.That(Text(VehicleStatistics.Capture(state, [new ItemSlot(2, 2, 1, HeldItem.Wrench)], 120)), Does.Contain("Held item: None"));
        Assert.That(Text(VehicleStatistics.Capture(state, null, 120)), Does.Contain("Held item: Unavailable"));
    }

    /// <summary>Deadlines use current ticks and absent owners never become zero-valued telemetry.</summary>
    [Test]
    public void MissingStateAndExpiredDeadlinesAreExplicit()
    {
        Assert.That(Text(VehicleStatistics.Capture(null, null, 0)), Does.Contain("unavailable"));
        Assert.That(VehicleStatistics.Remaining(180, 120), Does.StartWith("1.00 s"));
        Assert.That(VehicleStatistics.Remaining(180, 240), Does.StartWith("0.00 s"));
    }

    /// <summary>A fresh projection reflects changed lifecycle without retaining the previous health.</summary>
    [Test]
    public void RefreshReflectsDeathAndRespawnDeadline()
    {
        var physics = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        var movement = new VehicleState(120, physics, false, false, 0, 0, SurfaceType.Mud);
        var hit = new DamageEvent(1, 100, 1000, new DamageContext("explosion", 7, "secret"), true);
        var dead = new VehicleSnapshot(2, 3, movement, new VehicleDamageState(1000, 0, hit, null), physics, lifecycle: VehicleLifecycle.Respawning, respawnAtTick: 240);
        string text = Text(VehicleStatistics.Capture(dead, [], 120));
        Assert.That(text, Does.Contain("HP 0/1000").And.Contain("Respawning").And.Contain("2.00 s").And.Contain("Mud (last supported)").And.Contain("Airborne"));
        Assert.That(Text(VehicleStatistics.Capture(dead, [], 180)), Does.Contain("1.00 s"));
    }

    private static string Text(IReadOnlyList<StatisticSection> sections) => string.Join("\n", sections.Select(section => section.Text));
}
