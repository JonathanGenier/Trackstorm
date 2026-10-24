using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>One committed claw, anchored to the actual hit in a target's life-local frame.
/// Identity and absolute lifetime survive publications, reconnect and authority recovery.</summary>
public sealed record EmpClawAttachment(ulong Id, ulong Owner, ulong Vehicle, ulong Life,
    Vector3 LocalPosition, Vector3 LocalNormal, ulong AttachedAtTick, ulong ExpiresAtTick)
{
    /// <summary>Thirty seconds at the authoritative sixty-hertz item simulation rate.</summary>
    public const ulong LifetimeTicks = 1800;

    /// <summary>Rejects malformed anchors and deadlines without relocating the impact.</summary>
    public void Validate(ulong tick)
    {
        if (Id == 0 || Owner == 0 || Vehicle == 0 || Owner == Vehicle || Life == 0 ||
            !VehiclePhysicsState.IsFinite(LocalPosition) || LocalPosition.Length() > 10 ||
            !WeaponAim.IsDirection(LocalNormal) || AttachedAtTick > tick || ExpiresAtTick <= tick ||
            ExpiresAtTick <= AttachedAtTick || ExpiresAtTick - AttachedAtTick > LifetimeTicks)
        { throw new ArgumentException("Invalid EMP attachment."); }
    }
}
