using Trackstorm.Core.Development;

namespace Trackstorm.Core.Tests.Development;

/// <summary>Exact approved Release 0.1.0 values, independent of any developer-local file.</summary>
[TestFixture]
internal sealed class ReleaseDefaultsTests
{
    /// <summary>Every persisted gameplay key maps to its exact approved canonical value.</summary>
    /// <param name="key">Approved stable persistence key.</param>
    /// <param name="expected">Exact persisted numeric value, including float-to-double expansion.</param>
    [TestCase("vehicle.mass", 900d)]
    [TestCase("vehicle.acceleration", 12d)]
    [TestCase("vehicle.braking", 14d)]
    [TestCase("vehicle.stop_speed", 0.10000000149011612d)]
    [TestCase("vehicle.reverse_acceleration", 10d)]
    [TestCase("vehicle.forward_speed", 30d)]
    [TestCase("vehicle.reverse_speed", 11d)]
    [TestCase("vehicle.grip", 12d)]
    [TestCase("vehicle.steering_angle", 0.800000011920929d)]
    [TestCase("vehicle.steering_speed", 30d)]
    [TestCase("vehicle.steering_response", 8d)]
    [TestCase("vehicle.wheelbase", 2.299999952316284d)]
    [TestCase("vehicle.tire_friction", 3.3499999046325684d)]
    [TestCase("vehicle.drive_traction_reserve", 0.550000011920929d)]
    [TestCase("vehicle.load_height", 0.44999998807907104d)]
    [TestCase("vehicle.handbrake_braking", 12d)]
    [TestCase("vehicle.handbrake_grip", 0.6000000238418579d)]
    [TestCase("vehicle.handbrake_response", 12d)]
    [TestCase("vehicle.traction_recovery", 5d)]
    [TestCase("vehicle.coast_drag", 1d)]
    [TestCase("vehicle.reference_mass", 900d)]
    [TestCase("vehicle.suspension_spring", 32d)]
    [TestCase("vehicle.suspension_damping", 12d)]
    [TestCase("vehicle.chassis_compliance", 0.012000000104308128d)]
    [TestCase("vehicle.maximum_chassis_tilt", 0.1599999964237213d)]
    [TestCase("vehicle.stability_damping", 0.6499999761581421d)]
    [TestCase("vehicle.suspension_length", 0.800000011920929d)]
    [TestCase("vehicle.wheel_spring", 150d)]
    [TestCase("vehicle.wheel_damping", 18d)]
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
    [TestCase("vehicle.concrete.grip", 1d)]
    [TestCase("vehicle.concrete.drag", 1d)]
    [TestCase("vehicle.concrete.acceleration", 1d)]
    [TestCase("vehicle.mud.grip", 0.550000011920929d)]
    [TestCase("vehicle.mud.drag", 3d)]
    [TestCase("vehicle.mud.acceleration", 0.6000000238418579d)]
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
        Assert.That(GameplayOptions.All.Count, Is.EqualTo(59));
        Assert.DoesNotThrow(defaults.Validate);
        var file = DeveloperSettingsFile.Read(string.Empty, defaults);
        var restored = DeveloperSettingsFile.Read(file.Write(defaults), defaults);
        Assert.That(restored.RejectedRecords, Is.Zero);
        Assert.That(restored.Configuration, Is.EqualTo(defaults));
        var state = new GameplayConfigurationState(0, defaults);
        Assert.That(GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(9, state)).State, Is.EqualTo(state));
    }
}
