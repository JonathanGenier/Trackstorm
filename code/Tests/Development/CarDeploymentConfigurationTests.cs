using Trackstorm.Core.Development;
using Trackstorm.Core.Networking.Replication;

namespace Trackstorm.Core.Tests.Development;

[TestFixture]
internal sealed class CarDeploymentConfigurationTests
{
    [Test]
    public void IndependentSpeedsSurviveAuthorityCodecPersistenceAndDefaults()
    {
        var host = new HostVehicleSession(9);
        Assert.That(host.Configuration.Configuration.Vehicle.TrunkDeploymentSpeed, Is.EqualTo(3));
        Assert.That(host.Configuration.Configuration.Vehicle.RackDeploymentSpeed, Is.EqualTo(3));
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["vehicle.trunk_deployment_speed"] = 1 }, out _), Is.True);
        Assert.That(host.Configuration.Configuration.Vehicle.RackDeploymentSpeed, Is.EqualTo(3));
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["vehicle.rack_deployment_speed"] = 5 }, out _), Is.True);
        Assert.That(host.Configuration.Configuration.Vehicle.TrunkDeploymentSpeed, Is.EqualTo(1));
        Assert.That(GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(9, host.Configuration)).State, Is.EqualTo(host.Configuration));
        var file = DeveloperSettingsFile.Read(string.Empty);
        Assert.That(DeveloperSettingsFile.Read(file.Write(host.Configuration.Configuration)).Configuration, Is.EqualTo(host.Configuration.Configuration));
        var reset = GameplayOptions.All.Where(o => o.Group == "Car deployment").ToDictionary(o => o.Key, o => o.Read(GameplayConfiguration.HostedDefaults));
        Assert.That(host.TryConfigure(0, reset, out _), Is.True);
        Assert.That(host.Configuration.Configuration.Vehicle.TrunkDeploymentSpeed, Is.EqualTo(3));
        Assert.That(host.Configuration.Configuration.Vehicle.RackDeploymentSpeed, Is.EqualTo(3));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(11)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void InvalidSpeedRejectsWholeTransaction(double invalid)
    {
        var host = new HostVehicleSession(9);
        var before = host.Configuration;
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["vehicle.trunk_deployment_speed"] = 2, ["vehicle.rack_deployment_speed"] = invalid }, out _), Is.False);
        Assert.That(host.Configuration, Is.EqualTo(before));
    }
}
