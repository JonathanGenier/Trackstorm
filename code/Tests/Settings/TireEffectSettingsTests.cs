using NUnit.Framework;
using Trackstorm.Core.Settings;

namespace Trackstorm.Core.Tests.Settings;

[TestFixture]
internal sealed class TireEffectSettingsTests
{
    [Test]
    public void DefaultsSupportSixtySecondsOfEightVehicleMaximumEmission()
    {
        var settings = TireEffectSettings.Defaults;
        Assert.That(settings["tire.lifetime"], Is.EqualTo(60));
        Assert.That(settings["tire.budget"], Is.GreaterThanOrEqualTo(8 * 4 * 20 * 60));
        foreach (string surface in new[] { "dirt", "grass", "mud", "deep_mud" })
        {
            Assert.That(settings[$"tire.{surface}.duration"], Is.EqualTo(1));
        }
        Assert.That(settings["tire.water.duration"], Is.LessThan(1));
    }

    [TestCase("tire.budget", 100000)]
    [TestCase("tire.budget", 512.5)]
    [TestCase("tire.quality", -1)]
    [TestCase("tire.lifetime", double.NaN)]
    [TestCase("tire.lifetime", double.PositiveInfinity)]
    [TestCase("unknown", 1)]
    public void InvalidTransactionCannotPartiallyCommit(string key, double value)
    {
        var baseline = TireEffectSettings.Defaults;
        Assert.That(baseline.TryApply(new Dictionary<string, double> { ["tire.width"] = 2, [key] = value }, out var result, out var error), Is.False);
        Assert.That(result, Is.SameAs(baseline));
        Assert.That(error, Is.Not.Empty);
    }

    [Test]
    public void EveryControlRoundTripsLocallyAndMalformedValuesDoNotDestroyOtherPreferences()
    {
        var edits = TireEffectSettings.Options.ToDictionary(option => option.Key, option => (double)option.Minimum);
        Assert.That(TireEffectSettings.Defaults.TryApply(edits, out var tire, out _), Is.True);
        var decoded = PlayerSettingsJson.Deserialize(PlayerSettingsJson.Serialize(new PlayerSettings { TireEffects = tire, MusicVolume = .4 }));
        foreach (var (key, value) in edits) { Assert.That(decoded.TireEffects[key], Is.EqualTo((float)value), key); }
        Assert.That(decoded.MusicVolume, Is.EqualTo(.4));
        var tolerant = PlayerSettingsJson.Deserialize("{\"tireEffects\":{\"tire.budget\":999999,\"tire.grass.duration\":0.5},\"musicVolume\":0.2}");
        Assert.That(tolerant.TireEffects["tire.budget"], Is.EqualTo(TireEffectSettings.Defaults["tire.budget"]));
        Assert.That(tolerant.TireEffects["tire.grass.duration"], Is.EqualTo(.5f));
        Assert.That(tolerant.MusicVolume, Is.EqualTo(.2));
    }
}
