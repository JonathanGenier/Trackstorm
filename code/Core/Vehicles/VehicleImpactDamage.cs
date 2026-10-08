namespace Trackstorm.Core.Vehicles;

/// <summary>One received vehicle impact prepared from the common authoritative boundary.</summary>
internal readonly record struct VehicleImpactDamage(ulong OtherVehicleId, float Severity, float MomentumDisadvantage);
