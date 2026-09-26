using Trackstorm.Core.Items;
using Trackstorm.Core.Development;
using Trackstorm.Core.Networking.Replication;

namespace Trackstorm.Core.Tests.Development;

[TestFixture]
internal sealed class EnvironmentPresetTests
{
    [TestCase(EnvironmentPreset.ClearBlue)]
    [TestCase(EnvironmentPreset.Night)]
    [TestCase(EnvironmentPreset.EmberSky)]
    [TestCase(EnvironmentPreset.Apocalypse)]
    [TestCase(EnvironmentPreset.NeonSunset)]
    public void SelectionIsAuthoritativePersistentAndRecoverableWithoutChangingVehicles(EnvironmentPreset preset)
    {
        var host = new HostVehicleSession(9);
        host.Join(42);
        var vehicles = host.World.State.Vehicles.ToArray();
        var edits = new Dictionary<string, double> { ["environment.preset"] = (int)preset };
        Assert.That(host.TryConfigure(99, edits, out _), Is.False);
        Assert.That(host.TryConfigure(42, edits, out _), Is.True);
        Assert.That(host.Configuration.Configuration.Environment, Is.EqualTo(preset));
        Assert.That(host.World.State.Vehicles, Is.EqualTo(vehicles));
        var file = DeveloperSettingsFile.Read(string.Empty);
        Assert.That(DeveloperSettingsFile.Read(file.Write(host.Configuration.Configuration)).Configuration, Is.EqualTo(host.Configuration.Configuration));
        var checkpoint = new ResumeCheckpoint(new ItemPublication(1, host.Snapshot(), [], [], []), host.World.State.Match!, null, host.Configuration);
        var restored = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(checkpoint));
        Assert.That(restored.Configuration, Is.EqualTo(host.Configuration));
        Assert.That(host.TryConfigure(42, edits, out _), Is.True);
        Assert.That(host.Configuration.Revision, Is.EqualTo(preset == EnvironmentPreset.ClearBlue ? 0 : 1));
    }

    [TestCase(-1d)]
    [TestCase(5d)]
    [TestCase(1.5d)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void InvalidSelectionRejectsTheWholeTransaction(double value)
    {
        var host = new HostVehicleSession(9);
        var before = host.Configuration;
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["environment.preset"] = value, ["vehicle.mass"] = 1000 }, out _), Is.False);
        Assert.That(host.Configuration, Is.EqualTo(before));
    }
}
