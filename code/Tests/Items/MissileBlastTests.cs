using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

/// <summary>Production-scale radial damage and authoritative tuning, independent of decorative reach.</summary>
[TestFixture]
internal sealed class MissileBlastTests
{
    [Test]
    public void ProductionBlastEndsAtOneCarLengthAndRetainsPeakDamageAndImpulse()
    {
        var host = new HostVehicleSession(241, configuration: GameplayConfiguration.HostedDefaults);
        var items = host.Items;
        Assert.That(items.Configuration.ExplosionRadius, Is.EqualTo(VehicleDimensions.Length));
        var center = items.Explosion(Vector3.Zero, Vector3.Zero);
        Assert.That(center.Damage, Is.EqualTo(300));
        Assert.That(center.Impulse, Is.EqualTo(Vector3.UnitY * 15000));
        foreach (var axis in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
        {
            var half = items.Explosion(Vector3.Zero, axis * (VehicleDimensions.Length / 2));
            Assert.That(half.Damage, Is.EqualTo(150).Within(.001));
            Assert.That(half.Impulse.Length(), Is.EqualTo(7500).Within(.01));
            Assert.That(items.Explosion(Vector3.Zero, axis * (VehicleDimensions.Length - .01f)).Damage, Is.GreaterThan(0));
            foreach (float distance in new[] { VehicleDimensions.Length, VehicleDimensions.Length + .01f, VehicleDimensions.Length * 2.25f })
            {
                var outside = items.Explosion(Vector3.Zero, axis * distance);
                Assert.That(outside.Damage, Is.Zero);
                Assert.That(outside.Impulse, Is.EqualTo(Vector3.Zero));
            }
        }
    }

    [Test]
    public void RadiusEditChangesActualBlastAndSurvivesConfigurationReplicationAndRestore()
    {
        var host = new HostVehicleSession(241, configuration: GameplayConfiguration.HostedDefaults);
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["items.explosion_radius"] = 3 }, out var error), Is.True, error);
        Assert.That(host.Items.Explosion(Vector3.Zero, Vector3.UnitX * 4).Damage, Is.Zero);
        Assert.That(host.Items.Explosion(Vector3.Zero, Vector3.UnitX * 1.5f).Damage, Is.EqualTo(150));
        var state = GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(241, host.Configuration)).State;
        var restored = new ItemAuthority(state.Configuration.Items);
        Assert.That(restored.Explosion(Vector3.Zero, Vector3.UnitX * 4).Impulse, Is.EqualTo(Vector3.Zero));
        Assert.That(restored.Explosion(Vector3.Zero, Vector3.Zero).Damage, Is.EqualTo(300));
        Assert.That(restored.Explosion(Vector3.Zero, Vector3.UnitX * 4, HeldItem.Salvo).Damage, Is.GreaterThan(0), "Salvo keeps its own radius.");
    }
}
