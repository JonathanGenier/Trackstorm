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
    [TestCase("vehicle.mass", 900d)]
    [TestCase("vehicle.acceleration", 11d)]
    [TestCase("vehicle.braking", 14d)]
    [TestCase("vehicle.stop_speed", 0.05000000074505806d)]
    [TestCase("vehicle.reverse_acceleration", 8d)]
    [TestCase("vehicle.forward_speed", 44.439998626708984d)]
    [TestCase("vehicle.reverse_speed", 11d)]
    [TestCase("vehicle.grip", 12d)]
    [TestCase("vehicle.steering_angle", 0.6000000238418579d)]
    [TestCase("vehicle.steering_speed", 11d)]
    [TestCase("vehicle.steering_response", 2.4000000953674316d)]
    [TestCase("vehicle.wheelbase", (double)VehicleDimensions.Wheelbase)]
    [TestCase("vehicle.tire_friction", (double)1.65f)]
    [TestCase("vehicle.drive_traction_reserve", 0.550000011920929d)]
    [TestCase("vehicle.load_height", (double)(0.45f * VehicleDimensions.Scale))]
    [TestCase("vehicle.handbrake_braking", 12d)]
    [TestCase("vehicle.handbrake_grip", 0.6000000238418579d)]
    [TestCase("vehicle.handbrake_response", 4d)]
    [TestCase("vehicle.traction_recovery", 3d)]
    [TestCase("vehicle.coast_drag", 0.5d)]
    [TestCase("vehicle.reference_mass", 900d)]
    [TestCase("vehicle.suspension_spring", 32d)]
    [TestCase("vehicle.suspension_damping", 8d)]
    [TestCase("vehicle.chassis_compliance", 0.004000000189989805d)]
    [TestCase("vehicle.maximum_chassis_tilt", 0.1599999964237213d)]
    [TestCase("vehicle.stability_damping", 0.6499999761581421d)]
    [TestCase("vehicle.suspension_length", (double)(VehicleDimensions.RideHeight + (9.81f / 100)))]
    [TestCase("vehicle.wheel_spring", 100d)]
    [TestCase("vehicle.wheel_damping", 20d)]
    [TestCase("vehicle.gravity", 9.8100004196167d)]
    [TestCase("vehicle.maximum_physics_speed", 65d)]
    [TestCase("vehicle.maximum_angular_speed", 8d)]
    [TestCase("damage.max_hp", 1000d)]
    [TestCase("damage.collision_threshold", 4d)]
    [TestCase("damage.collision_scale", 5d)]
    [TestCase("damage.maximum_collision_damage", 100d)]
    [TestCase("damage.collision_cooldown_ticks", 12d)]
    [TestCase("items.wrench_heal", 35d)]
    [TestCase("items.missile_speed", 70d)]
    [TestCase("items.explosion_radius", 12d)]
    [TestCase("items.maximum_damage", 300d)]
    [TestCase("items.maximum_impulse", 15000d)]
    [TestCase("items.missile_lifetime_ticks", 300d)]
    [TestCase("spawns.cooldown_ticks", 600d)]
    [TestCase("spawns.wrench_weight", 1d)]
    [TestCase("spawns.missile_weight", 1d)]
    [TestCase("spawns.seed", 1d)]
    [TestCase("spawns.pickup_radius", 3d)]
    [TestCase("respawn.delay_ticks", 180d)]
    [TestCase("respawn.clear_held_item_on_death", 1d)]
    [TestCase("match.kill_target", 5d)]
    [TestCase("match.minimum_players", 2d)]
    [TestCase("match.countdown_ticks", 180d)]
    [TestCase("vehicle.concrete.grip", (double)0.95f)]
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
        Assert.That(GameplayOptions.All.Count, Is.EqualTo(92));
        Assert.That(defaults.Items.MaximumOilPatches, Is.EqualTo(16));
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
