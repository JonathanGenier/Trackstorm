using System.Numerics;

namespace Trackstorm.Client.Vehicles;

/// <summary>Road-relative driving intent; no target selection or projectile correction.</summary>
internal static class DrivingWeaponAim
{
    internal static Vector3 Direction(Vector3 forward, Vector3 roadNormal)
    {
        Vector3 heading = new(forward.X, 0, forward.Z);
        if (heading.LengthSquared() < .001f) heading = -Vector3.UnitZ;
        heading = Vector3.Normalize(heading);
        Vector3 tangent = Vector3.Normalize(heading - roadNormal * Vector3.Dot(heading, roadNormal));
        const float depression = MathF.PI / 120; // 1.5 degrees below the road tangent.
        return Vector3.Normalize(tangent * MathF.Cos(depression) - roadNormal * MathF.Sin(depression));
    }
}
