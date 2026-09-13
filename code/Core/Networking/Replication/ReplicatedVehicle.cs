using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Networking.Replication;

/// <summary>Authoritative vehicle state plus the last input consumed for its owner.</summary>
/// <param name="State">Complete replay state, independent of a rendering engine.</param>
/// <param name="AcknowledgedInput">Last retired command identity.</param>
public sealed record ReplicatedVehicle(VehicleSnapshot State, uint AcknowledgedInput);
