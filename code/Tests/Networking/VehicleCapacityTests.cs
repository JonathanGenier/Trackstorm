using System.Numerics;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Networking;

/// <summary>Production and future vehicle capacities survive authority, wire and prediction boundaries.</summary>
[TestFixture]
internal sealed class VehicleCapacityTests
{
    /// <summary>Capacity is a configuration value, not a fixed codec or prediction constant.</summary>
    /// <param name="capacity">Host vehicle capacity.</param>
    [TestCase(1000f)]
    [TestCase(1500f)]
    public void CapacitySurvivesJoinCodecAndPrediction(float capacity)
    {
        var host = new HostVehicleSession(99, damageConfiguration: new DamageConfiguration { MaxHP = capacity });
        host.Join(10);
        host.JoinPlayer(20, 3);
        host.Step(default, state => new VehicleObservation(state.ObservedPhysics, Vector3.UnitY));
        WorldSnapshot decoded = VehicleNetworkCodec.DecodeSnapshot(VehicleNetworkCodec.EncodeSnapshot(host.Snapshot()));
        foreach (ReplicatedVehicle vehicle in decoded.Vehicles)
        {
            Assert.That(vehicle.State.Damage.MaxHP, Is.EqualTo(capacity));
            Assert.That(vehicle.State.Damage.CurrentHP, Is.EqualTo(capacity));
            var predicted = new PredictedVehicle(vehicle);
            predicted.Predict(default, state => new VehicleObservation(state.ObservedPhysics, Vector3.UnitY));
            Assert.That(predicted.State.Damage.MaxHP, Is.EqualTo(capacity));
            Assert.That(predicted.State.Damage.CurrentHP, Is.EqualTo(capacity));
        }
    }
}
