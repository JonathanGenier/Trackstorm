using Trackstorm.Core.Development;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Development;

/// <summary>Approved hosted tuning with current vehicle dimensions, independent of any developer-local file.</summary>
[TestFixture]
internal sealed class ReleaseDefaultsTests
{
    /// <summary>Every persisted gameplay key maps to its exact approved canonical value.</summary>
    /// <param name="key">Approved stable persistence key.</param>
    /// <param name="expected">Exact persisted numeric value, including float-to-double expansion.</param>
    [TestCase("spawns.tombstone_weight", 1d)]
    [TestCase("vehicle.wall_drag", (double)0.18f)]
    [TestCase("vehicle.crash_dissipation", (double)0.95f)]
    [TestCase("vehicle.crash_rotation", (double)0.08f)]
    [TestCase("vehicle.crash_angular_limit", (double)1.2f)]
    [TestCase("environment.health_scale", 1d)]
    [TestCase("environment.impact_threshold", 3d)]
    [TestCase("environment.impact_scale", 10d)]
    [TestCase("environment.piece_speed", 6d)]
    [TestCase("environment.push_scale", (double)0.35f)]
    [TestCase("environment.velocity_retention", (double)0.9f)]
    [TestCase("vehicle.air_pitch_rate", (double)2.52f)]
    [TestCase("vehicle.air_yaw_rate", (double)2.16f)]
    [TestCase("vehicle.air_roll_rate", (double)3.24f)]
    [TestCase("vehicle.air_pitch_acceleration", 16d)]
    [TestCase("vehicle.air_yaw_acceleration", 14d)]
    [TestCase("vehicle.air_roll_acceleration", 20d)]
    [TestCase("vehicle.air_stabilization", 8d)]
    [TestCase("vehicle.air_stabilization_response", (double)0.08f)]
    [TestCase("vehicle.air_input_response", (double)0.06f)]
    [TestCase("vehicle.support_normal_minimum", (double)0.55f)]
    [TestCase("vehicle.mass", 3000d)]
    [TestCase("vehicle.acceleration", (double)(24 * (3000f / 1400)))]
    [TestCase("vehicle.front_drive_share", 0d)]
    [TestCase("vehicle.braking", (double)(45 * (3000f / 1400)))]
    [TestCase("vehicle.stop_speed", 0.05000000074505806d)]
    [TestCase("vehicle.reverse_acceleration", (double)(8 * (3000f / 1400)))]
    [TestCase("vehicle.forward_speed", 44.439998626708984d)]
    [TestCase("vehicle.reverse_speed", 11d)]
    [TestCase("vehicle.grip", (double)(26 * (3000f / 1400)))]
    [TestCase("vehicle.steering_angle", (double)0.9f)]
    [TestCase("vehicle.steering_response", (double)0.95f)]
    [TestCase("vehicle.steering_smoothing", (double)0.3f)]
    [TestCase("vehicle.throttle_rise_time", (double)0.65f)]
    [TestCase("vehicle.throttle_fall_time", (double)0.12f)]
    [TestCase("vehicle.wheelbase", (double)VehicleDimensions.Wheelbase)]
    [TestCase("vehicle.tire_friction", (double)1.9f)]
    [TestCase("vehicle.drive_traction_reserve", 0.550000011920929d)]
    [TestCase("vehicle.load_height", (double)(0.45f * VehicleDimensions.Scale))]
    [TestCase("vehicle.handbrake_braking", (double)(30 * (3000f / 1400)))]
    [TestCase("vehicle.handbrake_grip", 0.5d)]
    [TestCase("vehicle.handbrake_response", 1d)]
    [TestCase("vehicle.traction_recovery", 3d)]
    [TestCase("vehicle.coast_drag", (double)0.28f)]
    [TestCase("vehicle.reference_mass", 900d)]
    [TestCase("vehicle.suspension_spring", 32d)]
    [TestCase("vehicle.suspension_damping", 8d)]
    [TestCase("vehicle.chassis_compliance", 0.004000000189989805d)]
    [TestCase("vehicle.maximum_chassis_tilt", 0.1599999964237213d)]
    [TestCase("vehicle.stability_damping", 0.6499999761581421d)]
    [TestCase("vehicle.suspension_length", (double)(VehicleDimensions.RideHeight + (11f / 22)))]
    [TestCase("vehicle.wheel_spring", 22d)]
    [TestCase("vehicle.wheel_damping", 12d)]
    [TestCase("vehicle.wheel_rebound_damping", 16d)]
    [TestCase("vehicle.wheel_bump_start", (double)0.55f)]
    [TestCase("vehicle.wheel_bump_spring", 3500d)]
    [TestCase("vehicle.gravity", 11d)]
    [TestCase("vehicle.dirt_cornering", 1d)]
    [TestCase("vehicle.crash_recovery_delay", 0.5d)]
    [TestCase("vehicle.crash_recovery_rate", 2d)]
    [TestCase("vehicle.maximum_physics_speed", 65d)]
    [TestCase("vehicle.maximum_angular_speed", 8d)]
    [TestCase("vehicle.dirt_steering_reserve", (double)0.95f)]
    [TestCase("vehicle.dirt.grip", 2d)]
    [TestCase("vehicle.grass.grip", 1.899999976158142d)]
    [TestCase("vehicle.oil_grip_reduction", (double)0.1f)]
    [TestCase("vehicle.oil_recovery_seconds", 0.5d)]
    [TestCase("damage.max_hp", 1000d)]
    [TestCase("damage.collision_threshold", 4d)]
    [TestCase("damage.collision_scale", 5d)]
    [TestCase("damage.maximum_collision_damage", 100d)]
    [TestCase("damage.collision_cooldown_ticks", 12d)]
    [TestCase("items.wrench_heal", 500d)]
    [TestCase("items.missile_speed", 120d)]
    [TestCase("items.mine_damage", 250d)]
    [TestCase("items.mine_attraction_radius", 30d)]
    [TestCase("items.mine_minimum_force", 1000d)]
    [TestCase("items.mine_maximum_force", 2000d)]
    [TestCase("items.mine_knockback", 18000d)]
    [TestCase("items.salvo_speed", 150d)]
    [TestCase("items.salvo_blast_radius", 10d)]
    [TestCase("items.salvo_damage", 250d)]
    [TestCase("items.salvo_marker_scale", 0.5d)]
    [TestCase("items.salvo_marker_width", 0.5d)]
    [TestCase("items.salvo_marker_lift", (double)0.05f)]
    [TestCase("items.explosion_radius", 12d)]
    [TestCase("items.maximum_damage", 300d)]
    [TestCase("items.maximum_impulse", 15000d)]
    [TestCase("items.missile_lifetime_ticks", 300d)]
    [TestCase("spawns.cooldown_ticks", 600d)]
    [TestCase("spawns.wrench_weight", 1d)]
    [TestCase("spawns.missile_weight", 1d)]
    [TestCase("spawns.seed", 34272265d)]
    [TestCase("spawns.pickup_radius", 3d)]
    [TestCase("respawn.delay_ticks", 180d)]
    [TestCase("respawn.clear_held_item_on_death", 1d)]
    [TestCase("match.kill_target", 5d)]
    [TestCase("match.minimum_players", 2d)]
    [TestCase("match.countdown_ticks", 180d)]
    [TestCase("vehicle.concrete.grip", 1.5d)]
    [TestCase("vehicle.concrete.drag", 1d)]
    [TestCase("vehicle.concrete.acceleration", (double)0.98f)]
    [TestCase("vehicle.mud.grip", (double)0.6f)]
    [TestCase("vehicle.mud.drag", 2.5d)]
    [TestCase("vehicle.mud.acceleration", (double)0.85f)]
    public void HostedDefaultsMatchApprovedTuning(string key, double expected)
    {
        var option = GameplayOptions.All.Single(option => option.Key == key);
        Assert.That(option.Read(GameplayConfiguration.HostedDefaults), Is.EqualTo(expected));
    }

    /// <summary>The complete release preset remains valid across persistence and network encoding.</summary>
    [Test]
    public void ReleaseDefaultsValidateAndRoundTrip()
    {
        var defaults = GameplayConfiguration.HostedDefaults;
        Assert.That(GameplayOptions.All.Count, Is.EqualTo(216));
        Assert.That(defaults.Items.OilPasses, Is.EqualTo(2));
        Assert.DoesNotThrow(defaults.Validate);
        var file = DeveloperSettingsFile.Read(string.Empty, defaults);
        var restored = DeveloperSettingsFile.Read(file.Write(defaults), defaults);
        Assert.That(restored.RejectedRecords, Is.Zero);
        Assert.That(restored.Configuration, Is.EqualTo(defaults));
        var state = new GameplayConfigurationState(0, defaults);
        Assert.That(GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(9, state)).State, Is.EqualTo(state));
    }

    /// <summary>Old complete saved presets adopt the new geometry without discarding unrelated or intentional overrides.</summary>
    [Test]
    public void OldSpatialDefaultsMigrateOnce()
    {
        const string legacy = "{\"schema\":1}\n{\"key\":\"vehicle.wheelbase\",\"value\":2.3}\n{\"key\":\"vehicle.load_height\",\"value\":0.45}\n{\"key\":\"vehicle.suspension_length\",\"value\":0.8}\n{\"key\":\"vehicle.mass\",\"value\":1100}";
        var file = DeveloperSettingsFile.Read(legacy, GameplayConfiguration.HostedDefaults);
        Assert.That(file.Configuration.Vehicle.Wheelbase, Is.EqualTo(VehicleDimensions.Wheelbase));
        Assert.That(file.Configuration.Vehicle.LoadHeight, Is.EqualTo(GameplayConfiguration.HostedDefaults.Vehicle.LoadHeight));
        Assert.That(file.Configuration.Vehicle.SuspensionLength, Is.EqualTo(GameplayConfiguration.HostedDefaults.Vehicle.SuspensionLength));
        Assert.That(file.Configuration.Vehicle.Mass, Is.EqualTo(1100));
        Assert.That(DeveloperSettingsFile.Read(file.Write(file.Configuration)).Configuration, Is.EqualTo(file.Configuration));
        Assert.That(DeveloperSettingsFile.Read(legacy.Replace("2.3", "2.7", StringComparison.Ordinal)).Configuration.Vehicle.Wheelbase, Is.EqualTo(2.7f));
        Assert.That(DeveloperSettingsFile.Read(legacy.Replace("\"schema\":1", "\"schema\":2", StringComparison.Ordinal)).Configuration.Vehicle.Wheelbase, Is.EqualTo(2.3f));
    }
}
