using Trackstorm.Core.Input;
using Trackstorm.Core.Settings;

namespace Trackstorm.Core.Tests.Settings;

/// <summary>Verifies preference defaults, compatibility, isolation, and stable serialization without Godot.</summary>
[TestFixture]
internal sealed class PlayerSettingsTests
{
    /// <summary>Safe startup values match the documented settings contract.</summary>
    [Test]
    public void Defaults_AreSafe()
    {
        var settings = new PlayerSettings();
        Assert.Multiple(() =>
        {
            Assert.That(settings.MasterVolume, Is.EqualTo(1));
            Assert.That(settings.MusicVolume, Is.EqualTo(1));
            Assert.That(settings.SfxVolume, Is.EqualTo(1));
            Assert.That(settings.CameraShakeIntensity, Is.EqualTo(1));
            Assert.That(settings.Fullscreen, Is.False);
            Assert.That(settings.WindowWidth, Is.EqualTo(1280));
            Assert.That(settings.WindowHeight, Is.EqualTo(720));
            Assert.That(settings.SpeedUnit, Is.EqualTo(SpeedUnit.KilometresPerHour));
            Assert.That(settings.ShowFps, Is.False);
            Assert.That(settings.ShowPing, Is.False);
            Assert.That(settings.InvertSteering, Is.False);
            Assert.That(settings.DeadZone, Is.EqualTo(0.15));
            Assert.That(settings.Bindings, Is.Empty);
        });
    }

    /// <summary>Every preference, including explicitly unbound actions, survives the persisted representation.</summary>
    [Test]
    public void RoundTrip_PreservesAllSettings()
    {
        PlayerSettings expected = new PlayerSettings
        {
            MasterVolume = 0.2,
            MusicVolume = 0.4,
            SfxVolume = 0.6,
            CameraShakeIntensity = 0.35,
            Fullscreen = true,
            WindowWidth = 1920,
            WindowHeight = 1080,
            SpeedUnit = SpeedUnit.MilesPerHour,
            ShowFps = true,
            ShowPing = true,
            InvertSteering = true,
            DeadZone = 0.25,
        }.WithBindings(InputAction.Accelerate, ["key:87", "axis:0:5:1"])
            .WithBindings(InputAction.Pause, []);
        string json = PlayerSettingsJson.Serialize(expected);
        Assert.That(PlayerSettingsJson.Serialize(PlayerSettingsJson.Deserialize(json)), Is.EqualTo(json));
    }

    /// <summary>Old saves are identifiable without changing their custom bindings; current saves retain the migration marker.</summary>
    [Test]
    public void BindingDefaultsRevisionDistinguishesLegacySaves()
    {
        Assert.That(PlayerSettingsJson.Deserialize("{\"version\":1}").BindingDefaultsVersion, Is.Zero);
        Assert.That(PlayerSettingsJson.Deserialize(PlayerSettingsJson.Serialize(new PlayerSettings())).BindingDefaultsVersion, Is.EqualTo(1));
    }

    /// <summary>Malformed or unsupported documents restore defaults.</summary>
    /// <param name="json">Invalid persisted document.</param>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("{broken")]
    [TestCase("[]")]
    [TestCase("null")]
    [TestCase("{\"version\":2}")]
    public void InvalidDocument_Defaults(string? json)
    {
        Assert.That(PlayerSettingsJson.Serialize(PlayerSettingsJson.Deserialize(json)), Is.EqualTo(PlayerSettingsJson.Serialize(new PlayerSettings())));
    }

    /// <summary>Invalid fields do not erase unrelated valid preferences.</summary>
    [Test]
    public void InvalidFields_FallBackIndependently()
    {
        PlayerSettings settings = PlayerSettingsJson.Deserialize("""
            {"masterVolume":"loud","musicVolume":null,"sfxVolume":1e999,"fullscreen":"yes",
             "windowWidth":-1,"windowHeight":720.5,"speedUnit":999,"showFps":true,"showPing":"true",
             "deadZone":1,"bindings":{"RemovedAction":["x"],"0":["x"],"Accelerate":{},"Brake":[null]}}
            """);
        Assert.Multiple(() =>
        {
            Assert.That(settings.MasterVolume, Is.EqualTo(1));
            Assert.That(settings.MusicVolume, Is.EqualTo(1));
            Assert.That(settings.SfxVolume, Is.EqualTo(1));
            Assert.That(settings.Fullscreen, Is.False);
            Assert.That(settings.WindowWidth, Is.EqualTo(1280));
            Assert.That(settings.WindowHeight, Is.EqualTo(720));
            Assert.That(settings.SpeedUnit, Is.EqualTo(SpeedUnit.KilometresPerHour));
            Assert.That(settings.ShowFps, Is.True);
            Assert.That(settings.ShowPing, Is.False);
            Assert.That(settings.DeadZone, Is.EqualTo(0.15));
            Assert.That(settings.Bindings, Is.Empty);
        });
    }

    /// <summary>Finite gains clamp, while non-finite gains restore defaults on every construction path.</summary>
    /// <param name="value">Requested gain.</param>
    /// <param name="expected">Validated gain.</param>
    [TestCase(-1d, 0d)]
    [TestCase(0d, 0d)]
    [TestCase(0.5d, 0.5d)]
    [TestCase(1d, 1d)]
    [TestCase(2d, 1d)]
    [TestCase(double.NaN, 1d)]
    [TestCase(double.PositiveInfinity, 1d)]
    [TestCase(double.NegativeInfinity, 1d)]
    public void Volumes_Clamp(double value, double expected)
    {
        var settings = new PlayerSettings { MasterVolume = value, MusicVolume = value, SfxVolume = value };
        Assert.That(new[] { settings.MasterVolume, settings.MusicVolume, settings.SfxVolume }, Is.All.EqualTo(expected));
    }

    /// <summary>Local shake settings clamp safely and remain compatible with older or malformed saves.</summary>
    [Test]
    public void CameraShake_ValidatesAndPersistsIndependently()
    {
        foreach (double value in new[] { -1d, 0d, 0.25d, 1d, 2d, double.NaN, double.PositiveInfinity })
        {
            double expected = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1;
            var settings = new PlayerSettings { CameraShakeIntensity = value };
            Assert.That(settings.CameraShakeIntensity, Is.EqualTo(expected));
            Assert.That(PlayerSettingsJson.Deserialize(PlayerSettingsJson.Serialize(settings)).CameraShakeIntensity, Is.EqualTo(expected));
        }

        foreach (string json in new[] { "{}", "{\"cameraShakeIntensity\":null}", "{\"cameraShakeIntensity\":\"off\"}", "{\"cameraShakeIntensity\":1e999}" })
        {
            Assert.That(PlayerSettingsJson.Deserialize(json).CameraShakeIntensity, Is.EqualTo(1));
        }

        var clamped = PlayerSettingsJson.Deserialize("{\"cameraShakeIntensity\":-2,\"showFps\":true}");
        Assert.That(clamped.CameraShakeIntensity, Is.Zero);
        Assert.That(clamped.ShowFps, Is.True);
    }

    /// <summary>Text values remain stable independently of enum numeric values.</summary>
    /// <param name="unit">Preference enum.</param>
    /// <param name="token">Stable serialized name.</param>
    [TestCase(SpeedUnit.KilometresPerHour, "km/h")]
    [TestCase(SpeedUnit.MilesPerHour, "mph")]
    public void SpeedUnit_HasStableText(SpeedUnit unit, string token)
    {
        Assert.That(PlayerSettingsJson.Serialize(new PlayerSettings { SpeedUnit = unit }), Does.Contain($"\"speedUnit\":\"{token}\""));
        Assert.That(PlayerSettingsJson.Deserialize($"{{\"speedUnit\":\"{token}\"}}").SpeedUnit, Is.EqualTo(unit));
        Assert.That(new PlayerSettings { SpeedUnit = (SpeedUnit)99 }.SpeedUnit, Is.EqualTo(SpeedUnit.KilometresPerHour));
    }

    /// <summary>Removed actions cannot poison valid overrides; missing preferences retain defaults.</summary>
    [Test]
    public void UnknownBindings_AreIgnored()
    {
        PlayerSettings settings = PlayerSettingsJson.Deserialize("""{"bindings":{"OldAction":["x"],"Accelerate":["key:87"],"Pause":[]}}""");
        Assert.That(settings.Bindings.Keys, Is.EquivalentTo(new[] { InputAction.Accelerate, InputAction.Pause }));
        Assert.That(settings.Bindings[InputAction.Pause], Is.Empty);
        Assert.That(settings.MasterVolume, Is.EqualTo(1));
    }

    /// <summary>Snapshots do not share caller-owned mutable binding collections.</summary>
    [Test]
    public void Bindings_CopyInputAndPreservePreviousSnapshot()
    {
        string[] tokens = ["key:87"];
        var original = new PlayerSettings();
        PlayerSettings updated = original.WithBindings(InputAction.Accelerate, tokens);
        tokens[0] = "changed";
        Assert.That(original.Bindings, Is.Empty);
        Assert.That(updated.Bindings[InputAction.Accelerate], Is.EqualTo(new[] { "key:87" }));
    }

    /// <summary>Persisted high-precision values cannot round up to the invalid float endpoint in the input adapter.</summary>
    [Test]
    public void DeadZone_RejectsValuesThatRoundToOne()
    {
        Assert.That(new PlayerSettings { DeadZone = 0.999999999 }.DeadZone, Is.EqualTo(0.15));
        Assert.That(PlayerSettingsJson.Deserialize("{\"deadZone\":0.999999999}").DeadZone, Is.EqualTo(0.15));
    }
}
