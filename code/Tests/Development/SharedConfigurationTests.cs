using System.Buffers.Binary;
using System.Globalization;
using Trackstorm.Core.Development;
using Trackstorm.Core.Networking.Replication;

namespace Trackstorm.Core.Tests.Development;

[TestFixture]
internal sealed class SharedConfigurationTests
{
    [Test]
    public void HostValidatesConnectedPeerEditsAndRejectsSuspendedSenders()
    {
        var host = new HostVehicleSession(9);
        host.Join(42);
        var edits = new Dictionary<string, double> { ["damage.max_hp"] = 2000, ["vehicle.mass"] = 1200 };
        Assert.That(host.TryConfigure(42, edits, out _), Is.True);
        Assert.That(host.World.State.Vehicles.All(vehicle => vehicle.Damage.MaxHP == 2000), Is.True);
        var before = host.Configuration;
        Assert.That(host.TryConfigure(42, new Dictionary<string, double> { ["vehicle.mass"] = -1, ["damage.max_hp"] = 100 }, out _), Is.False);
        Assert.That(host.Configuration, Is.EqualTo(before));
        host.Suspend(42);
        Assert.That(host.TryConfigure(42, edits, out _), Is.False);
        Assert.That(host.TryConfigure(43, edits, out _), Is.False);
    }

    [Test]
    public void RequestCodecRejectsMalformedDuplicateUnknownAndNonfiniteFields()
    {
        var bytes = ConfigurationRequestCodec.Encode(1, 2, new Dictionary<string, double> { ["vehicle.mass"] = 1200, ["vehicle.acceleration"] = 8 });
        Assert.That(ConfigurationRequestCodec.Decode(bytes).Edits.Count, Is.EqualTo(2));
        for (int length = 0; length < bytes.Length; length++)
            Assert.Throws<ArgumentException>(() => ConfigurationRequestCodec.Decode(bytes.AsSpan(0, length)));
        var duplicate = bytes.ToArray();
        duplicate[29] = duplicate[19]; duplicate[30] = duplicate[20];
        Assert.Throws<ArgumentException>(() => ConfigurationRequestCodec.Decode(duplicate));
        var unknown = bytes.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(unknown.AsSpan(19), ushort.MaxValue);
        Assert.Throws<ArgumentException>(() => ConfigurationRequestCodec.Decode(unknown));
        BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(21), double.NaN);
        Assert.Throws<ArgumentException>(() => ConfigurationRequestCodec.Decode(bytes));
    }

    [Test]
    public void ExportContainsOnlyCanonicalDifferencesGroupedAndCultureIndependent()
    {
        var defaults = GameplayConfiguration.HostedDefaults;
        var current = defaults with { Vehicle = defaults.Vehicle with { Mass = 1200 }, Items = defaults.Items with { MachineGunDamage = 12.5f } };
        var timestamp = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-CA");
            string export = ConfigurationChangesExport.Format(current, "0.1.70", timestamp);
            Assert.That(export, Does.Contain("Version: 0.1.70\n").And.Contain("Exported: 2026-09-26T12:00:00.0000000+00:00"));
            Assert.That(export, Does.Contain("[Vehicle]\nvehicle.mass=1200\n").And.Contain("[Machine Gun]\nitems.machine_gun_damage=12.5\n"));
            Assert.That(export, Does.Not.Contain("vehicle.acceleration"));
            Assert.That(ConfigurationChangesExport.Format(defaults, "0.1.70", timestamp), Does.Not.Contain("["));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }
}
