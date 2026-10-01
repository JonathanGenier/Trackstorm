using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

/// <summary>Live trophy-truck controls affect forces/continuation through the shared catalog.</summary>
[TestFixture]
internal sealed class TrophyTuningTests
{
    [TestCase("rear_drive_grip", 1)]
    [TestCase("brake_grip", 1)]
    [TestCase("power_oversteer", 0)]
    [TestCase("spin_drive_loss", 0)]
    [TestCase("dirt_corner_full_speed", 16)]
    [TestCase("dirt_corner_fade_speed", 18)]
    [TestCase("dirt_corner_grip", 0)]
    [TestCase("dirt_corner_power_slip", 0)]
    [TestCase("front_brake_share", 0.2)]
    [TestCase("wheel_deep_damping", 0)]
    [TestCase("landing_rebound_decay", 10)]
    [TestCase("crash_recovery_ramp", 3)]
    [TestCase("crash_slide_damping", 0)]
    [TestCase("crash_roll_damping", 3)]
    public void SharedTuningChangesPhysicalResponseAndRoundTrips(string suffix, double value)
    {
        var defaults = new GameplayConfiguration();
        Assert.That(GameplayOptions.TryApply(defaults, new Dictionary<string, double> { ["vehicle." + suffix] = value }, out var edited, out _), Is.True);
        var restored = GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(1, new(1, edited))).State.Configuration;
        Assert.That(restored, Is.EqualTo(edited));
        var persisted = DeveloperSettingsFile.Read(DeveloperSettingsFile.Read("", defaults).Write(edited), defaults);
        Assert.That(persisted.Configuration, Is.EqualTo(edited));
        bool changed = false;
        for (int scenario = 0; scenario < 4; scenario++)
        {
            bool crash = scenario >= 2;
            var pose = new VehiclePhysicsState(Vector3.Zero, crash ? Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 2) : Quaternion.Identity,
                new(4, scenario == 1 ? 1 : -1, suffix == "spin_drive_loss" ? -6 : -16), new(0, 0, scenario == 3 ? 2 : 0));
            VehicleState Run(VehicleConfiguration configuration)
            {
                var movement = new VehicleMovement(configuration, pose);
                movement.Restore(new(0, pose, true, false, 0.6f, 0, landingIntensity: 1, crashSeconds: crash ? 2 : 0, throttle: 1, powerSlip: suffix == "spin_drive_loss" ? 0.3f : 0));
                return movement.Step(new InputFrame(1, 32767, scenario == 1 ? (ushort)0 : ushort.MaxValue, scenario == 1 ? ushort.MaxValue : (ushort)0, 0, 0, 0),
                    pose, Vector3.UnitY, surface: SurfaceType.Dirt, wheels: new WheelSupport(new Vector4(crash ? 0 : 0.7f)),
                    contacts: [new VehicleContact(Vector3.Zero, Vector3.UnitY, 0, 0)]);
            }
            changed |= Run(defaults.Vehicle) != Run(edited.Vehicle);
        }
        Assert.That(changed, Is.True, "Changing the live option must affect a physical command or its next-step continuation.");
    }

    [Test]
    public void InvalidForceAndRecoveryTuningRejectsWholeTransaction()
    {
        var defaults = new GameplayConfiguration();
        foreach (var change in new[] {
            new Dictionary<string,double> { ["vehicle.dirt_corner_full_speed"] = 30, ["vehicle.dirt_corner_fade_speed"] = 20 },
            new Dictionary<string,double> { ["vehicle.crash_recovery_ramp"] = 0 },
            new Dictionary<string,double> { ["vehicle.wheel_deep_damping"] = -1 },
            new Dictionary<string,double> { ["vehicle.front_brake_share"] = 1.1 },
            new Dictionary<string,double> { ["vehicle.rear_drive_grip"] = 0 },
            new Dictionary<string,double> { ["vehicle.brake_grip"] = 4.1 },
            new Dictionary<string,double> { ["vehicle.power_oversteer"] = 1 },
            new Dictionary<string,double> { ["vehicle.spin_drive_loss"] = double.NaN } })
        {
            Assert.That(GameplayOptions.TryApply(defaults, change, out _, out _), Is.False);
        }
        Assert.That(GameplayOptions.All.Any(o => o.Key == "vehicle.steering_speed"), Is.False, "Retired steering limiter must not appear as a dead live control.");
    }
}
