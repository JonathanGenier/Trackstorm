using Trackstorm.Core.Development;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;

namespace Trackstorm.Core.Tests.Development;

[TestFixture]
internal sealed class MissileConfigurationTests
{
    [Test]
    public void DedicatedMissileCategoryRoundTripsEveryFunctionalValue()
    {
        var keys = GameplayOptions.All.Where(o => o.Group == "Missile").Select(o => o.Key).ToArray();
        Assert.That(keys.Length, Is.EqualTo(16));
        Assert.That(keys, Does.Contain("items.missile_speed").And.Contain("items.missile_lifetime_ticks").And.Contain("items.maximum_damage"));
        var changes = new Dictionary<string, double>
        {
            ["items.missile_speed"] = 80, ["items.missile_lifetime_ticks"] = 600,
            ["items.maximum_damage"] = 123, ["items.explosion_radius"] = 7, ["items.maximum_impulse"] = 8000,
            ["items.missile_lifetime_seconds"] = 8, ["items.missile_world_limit"] = 2048,
            ["items.missile_clearance"] = 1.5, ["items.missile_look_ahead"] = 28,
            ["items.missile_detection_range"] = 9, ["items.missile_reacquisition_height"] = 4,
            ["items.missile_pitch_limit"] = 32, ["items.missile_turn_rate"] = 30,
            ["items.missile_response"] = 6, ["items.missile_slope_limit"] = 38,
            ["items.missile_drop_tolerance"] = 0.5,
        };
        Assert.That(GameplayOptions.TryApply(GameplayConfiguration.HostedDefaults, changes, out var config, out var error), Is.True, error);
        foreach (var option in GameplayOptions.All.Where(o => o.Group == "Missile")) { Assert.That(option.Read(config), Is.EqualTo(changes[option.Key])); }
        var state = new GameplayConfigurationState(1, config);
        Assert.That(GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(99, state)).State, Is.EqualTo(state));
        var obsolete = GameplayConfigurationCodec.Encode(99, state);
        obsolete[2] = 44;
        Assert.Throws<ArgumentException>(() => GameplayConfigurationCodec.Decode(obsolete), "Old protocol cannot install new terrain tuning.");
        var saved = DeveloperSettingsFile.Read("{\"schema\":2}");
        Assert.That(DeveloperSettingsFile.Read(saved.Write(config)).Configuration, Is.EqualTo(config));
        var host = new HostVehicleSession(99);
        Assert.That(host.TryConfigure(0, changes, out error), Is.True, error);
        var checkpoint = new ResumeCheckpoint(new ItemPublication(1, host.Snapshot(), [], [], []), host.World.State.Match!, null, host.Configuration);
        Assert.That(ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(checkpoint)).Configuration, Is.EqualTo(host.Configuration));
        Assert.That(host.Items.Explosion(System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero).Damage, Is.EqualTo(123));
    }

    [TestCase("items.missile_clearance", 0)]
    [TestCase("items.missile_look_ahead", 61)]
    [TestCase("items.missile_detection_range", double.NaN)]
    [TestCase("items.missile_reacquisition_height", 0.5)]
    [TestCase("items.missile_pitch_limit", 91)]
    [TestCase("items.missile_turn_rate", 91)]
    [TestCase("items.missile_response", 0)]
    [TestCase("items.missile_slope_limit", 90)]
    [TestCase("items.missile_drop_tolerance", -1)]
    [TestCase("items.missile_lifetime_seconds", 61)]
    [TestCase("items.missile_world_limit", 0)]
    public void InvalidMissileTransactionLeavesHostUntouched(string key, double value)
    {
        var host = new HostVehicleSession(99); var before = host.Configuration;
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["items.maximum_damage"] = 777, [key] = value }, out _), Is.False);
        Assert.That(host.Configuration, Is.EqualTo(before));
    }
}
