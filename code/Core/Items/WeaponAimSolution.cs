using System.Numerics;

namespace Trackstorm.Core.Items;

/// <summary>Host-accepted, capability-scoped direct-fire solution; presentation transforms never authorize a shot.</summary>
public sealed record WeaponAimSolution(ulong Vehicle, ulong Life, ulong Token, ulong Tick, float Yaw, float Pitch, Vector3 Origin, Vector3 Direction, bool Clear)
{
    /// <summary>Firing readiness includes deployment, fresh input and self clearance.</summary>
    public bool Ready { get; init; }
}
