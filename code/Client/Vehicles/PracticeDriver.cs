using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

/// <summary>Local fixture adapter for the shared host practice driver.</summary>
internal sealed class PracticeDriver(VehicleBody vehicle)
{
    private readonly PracticeVehicleInput _driver = new();
    internal InputFrame Capture(ulong tick) => _driver.Capture(vehicle.Snapshot, vehicle.Configuration, tick);
}
