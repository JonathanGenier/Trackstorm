namespace Trackstorm.Core.Items;

/// <summary>Captured flight identity and deadline. A lost victim is never replaced or rebound to a new life.</summary>
public sealed record EmpClawFlight(ulong OwnerLife, ulong Target, ulong TargetLife, ulong ExpiresAtTick);
