using System.Globalization;
using Trackstorm.Core.Development;
using Trackstorm.Core.Items;

namespace Trackstorm.Core.Tests.Development;

/// <summary>Historical external tuning is readable, while all live writes and packets stay canonical.</summary>
internal sealed class ShieldConfigurationMigrationTests
{
    private const string Header = "Trackstorm Config Changes\nVersion: 0.2.26\nExported: 2026-10-03T12:00:00.0000000+00:00\n";

    [TestCase("items.tombstone_width", "items.shield_width", "Tombstone", 7)]
    [TestCase("items.tombstone_height", "items.shield_height", "Shield", 3)]
    [TestCase("items.tombstone_depth", "items.shield_depth", "Shield", 1)]
    [TestCase("items.tombstone_mass", "items.shield_mass", "Shield", 300)]
    [TestCase("items.tombstone_clearance", "items.shield_clearance", "Shield", 2)]
    [TestCase("items.tombstone_lifetime", "items.shield_lifetime", "Shield", 90)]
    [TestCase("items.tombstone_tip_speed", "items.shield_tip_speed", "Shield", 25)]
    [TestCase("spawns.tombstone_weight", "spawns.shield_weight", "Item spawns", 3)]
    public void HistoricalFilesLoadAndRewriteCanonicalKeys(string historical, string canonical, string group, double value)
    {
        string number = value.ToString("R", CultureInfo.InvariantCulture);
        var file = DeveloperSettingsFile.Read($"{{\"schema\":2}}\n{{\"key\":\"{historical}\",\"value\":{number}}}");
        var option = GameplayOptions.All.Single(option => option.Key == canonical);
        Assert.That(file.RejectedRecords, Is.Zero);
        Assert.That(option.Read(file.Configuration), Is.EqualTo(value));
        string rewritten = file.Write(file.Configuration);
        Assert.That(rewritten, Does.Contain(canonical).And.Not.Contain(historical));
        Assert.That(DeveloperSettingsFile.Read(rewritten).Configuration, Is.EqualTo(file.Configuration));
        Assert.That(ConfigurationChangesImport.TryParse(Header + $"[{group}]\n{historical}={number}", GameplayConfiguration.HostedDefaults,
            out var edits, out _, out string error), Is.True, error);
        Assert.That(edits, Is.EqualTo(new Dictionary<string, double> { [canonical] = value }));
        string exported = ConfigurationChangesExport.Format(file.Configuration, "0.2.27", DateTimeOffset.UnixEpoch);
        Assert.That(exported, Does.Contain(canonical).And.Not.Contain(historical));
        Assert.Throws<ArgumentException>(() => ConfigurationRequestCodec.Encode(1, 1, new Dictionary<string, double> { [historical] = value }));
        Assert.That(ConfigurationRequestCodec.Decode(ConfigurationRequestCodec.Encode(1, 1, edits)).Edits, Is.EqualTo(edits));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CanonicalSeedWinsRegardlessOfRecordOrder(bool canonicalFirst)
    {
        const string canonical = "{\"key\":\"items.shield_mass\",\"value\":400}";
        const string historical = "{\"key\":\"items.tombstone_mass\",\"value\":300}";
        var file = DeveloperSettingsFile.Read(canonicalFirst ? canonical + "\n" + historical : historical + "\n" + canonical);
        Assert.That(file.Configuration.Items.ShieldMass, Is.EqualTo(400));
        Assert.That(file.RejectedRecords, Is.Zero);
    }

    [Test]
    public void RepeatedHistoricalSeedRecordsKeepExistingLastValidRecordPolicy()
    {
        var file = DeveloperSettingsFile.Read("{\"key\":\"items.tombstone_mass\",\"value\":300}\n{\"key\":\"items.tombstone_mass\",\"value\":400}");
        Assert.That(file.Configuration.Items.ShieldMass, Is.EqualTo(400));
        Assert.That(file.RejectedRecords, Is.Zero);
    }

    [TestCase("[Shield]\nitems.tombstone_mass=300\nitems.shield_mass=400")]
    [TestCase("[Shield]\nitems.shield_mass=400\nitems.tombstone_mass=300")]
    [TestCase("[Tombstone]\nitems.tombstone_mass=300\n[Shield]\nitems.shield_width=7")]
    [TestCase("[Shield]\nitems.tombstone_unknown=3")]
    [TestCase("[Shield]\nitems.tombstone_mass=-1")]
    public void AmbiguousOrInvalidImportsReturnNoEdits(string body)
    {
        Assert.That(ConfigurationChangesImport.TryParse(Header + body, GameplayConfiguration.HostedDefaults, out var edits, out _, out _), Is.False);
        Assert.That(edits, Is.Empty);
    }

    [Test]
    public void InventoryIdentityAndCanonicalCatalogRemainStable()
    {
        Assert.That((byte)HeldItem.Shield, Is.EqualTo(8));
        Assert.That(ItemRegistry.Find(HeldItem.Shield)!.Key, Is.EqualTo("shield"));
        Assert.That(GameplayOptions.All.Any(option => option.Key.Contains("tombstone", StringComparison.OrdinalIgnoreCase)), Is.False);
        Assert.That(GameplayConfiguration.HostedDefaults.Spawns.Weights[HeldItem.Shield], Is.EqualTo(1));
        Assert.That(ShieldState.DefaultHP, Is.EqualTo(1000));
    }
}
