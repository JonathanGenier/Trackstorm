using System.Globalization;
using Trackstorm.Core.Development;

namespace Trackstorm.Core.Tests.Development;

internal sealed class ConfigurationChangesImportTests
{
    private const string Header = "Trackstorm Config Changes\nVersion: 0.1.70\nExported: 2026-09-26T12:00:00.0000000+00:00\n";

    [Test]
    public void ExportImportPreservesNumericPrecisionAndOnlyChangesListedKeys()
    {
        var defaults = GameplayConfiguration.HostedDefaults;
        Assert.That(GameplayOptions.TryApply(defaults, new Dictionary<string, double>
        {
            ["vehicle.mass"] = 1200.125, ["items.machine_gun_damage"] = 12.5,
            ["match.drift_minimum_seconds"] = 0.123456789012345, ["spawns.seed"] = int.MaxValue,
        }, out var source, out _), Is.True);
        var destination = defaults with { Vehicle = defaults.Vehicle with { Acceleration = 19 } };
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-CA");
            string text = "\uFEFF" + ConfigurationChangesExport.Format(source, "0.1.70", DateTimeOffset.UtcNow).Replace("\n", "\r\n", StringComparison.Ordinal);
            Assert.That(ConfigurationChangesImport.TryParse(text, destination, out var edits, out string version, out string error), Is.True, error);
            Assert.That(version, Is.EqualTo("0.1.70"));
            Assert.That(edits.Count, Is.EqualTo(4));
            Assert.That(GameplayOptions.TryApply(destination, edits, out var result, out _), Is.True);
            Assert.That(result, Is.EqualTo(source with { Vehicle = source.Vehicle with { Acceleration = 19 } }));
            Assert.That(ConfigurationChangesImport.TryParse(Header, destination, out edits, out _, out _), Is.True);
            Assert.That(edits, Is.Empty, "An export with no differences is a no-op import.");
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    [Test]
    public void CompleteCatalogUsesTheSameExportCategoryAndKeyContract()
    {
        string text = Header + string.Concat(GameplayOptions.All.GroupBy(option => option.Group).Select(group =>
            $"\n[{group.Key}]\n" + string.Concat(group.Select(option => $"{option.Key}={option.Read(GameplayConfiguration.HostedDefaults).ToString("R", CultureInfo.InvariantCulture)}\n"))));
        Assert.That(ConfigurationChangesImport.TryParse(text, GameplayConfiguration.HostedDefaults, out var edits, out _, out string error), Is.True, error);
        Assert.That(edits.Count, Is.EqualTo(GameplayOptions.All.Count));
    }

    [TestCase("[Vehicle]\nvehicle.mass=1200\nvehicle.mass=1300")]
    [TestCase("[Vehicle]\nvehicle.mass=1200\n[Vehicle]\nvehicle.acceleration=19")]
    [TestCase("[Unknown]\nvehicle.mass=1200")]
    [TestCase("[Vehicle]\nunknown=1200")]
    [TestCase("[Vehicle]\nitems.machine_gun_damage=12")]
    [TestCase("[Vehicle]\nvehicle.mass=NaN")]
    [TestCase("[Vehicle]\nvehicle.mass=Infinity")]
    [TestCase("[Vehicle]\nvehicle.mass=1,200")]
    [TestCase("[Vehicle]\nvehicle.mass=-1")]
    [TestCase("[Item spawns]\nspawns.seed=1.5")]
    [TestCase("[Vehicle]\nvehicle.mass=1200\nbroken")]
    [TestCase("vehicle.mass=1200")]
    public void MalformedFilesNeverReturnPartialEdits(string body)
    {
        Assert.That(ConfigurationChangesImport.TryParse(Header + body, GameplayConfiguration.HostedDefaults, out var edits, out _, out string error), Is.False);
        Assert.That(edits, Is.Empty);
        Assert.That(error, Is.Not.Empty);
    }

    [TestCase("")]
    [TestCase("Trackstorm Config Changes\nVersion: invalid\nExported: invalid")]
    [TestCase("Trackstorm Config Changes\nVersion: 0.1.70")]
    [TestCase("Trackstorm Config Changes\nVersion: 0.1.70\nExported: yesterday")]
    public void MissingOrInvalidMetadataIsRejected(string text) =>
        Assert.That(ConfigurationChangesImport.TryParse(text, GameplayConfiguration.HostedDefaults, out _, out _, out _), Is.False);

    [Test]
    public void OversizedAndCrossFieldInvalidImportsAreRejected()
    {
        Assert.That(ConfigurationChangesImport.TryParse(Header + new string(' ', ConfigurationChangesImport.MaximumLength), GameplayConfiguration.HostedDefaults, out _, out _, out _), Is.False);
        Assert.That(ConfigurationChangesImport.TryParse(Header + "[Circus stunts]\nmatch.top_speed_enter_ratio=0.5", GameplayConfiguration.HostedDefaults, out var edits, out _, out _), Is.False);
        Assert.That(edits, Is.Empty);
    }
}
