using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Only the balloon membrane is vulnerable. Dimensions follow the authored 1.5-scale claw.</summary>
public static class EmpClawGeometry
{
    /// <summary>Inflated membrane radius, excluding metal jaws and body.</summary>
    public const float InflatedRadius = .195f;
    /// <summary>Uninflated stowed membrane radius.</summary>
    public const float StowedRadius = .075f;
    /// <summary>Fixed rear-facing round roots relative to the deployed weapon rack.</summary>
    public static Vector3 StoredRoot(int index) => new((index - 1) * .40f, -.02f, .75f);
    /// <summary>Deployed rack origin in the vehicle frame.</summary>
    public static Vector3 RackOrigin => WeaponAim.Pivot - new Vector3(0, .47f, 0);
    /// <summary>Rear-facing membrane, including its authored local offset.</summary>
    public static Vector3 StoredNose(int index) => RackOrigin + StoredRoot(index) + new Vector3(0, -.003f, .1875f);
    /// <summary>Resolves the inflated membrane without relocating the original attachment.</summary>
    public static Vector3 Nose(EmpClawAttachment claw, VehiclePhysicsState pose)
    {
        Vector3 normal = claw.LocalNormal;
        Vector3 up = Math.Abs(normal.Y) > .98f ? Vector3.UnitX : Vector3.UnitY;
        Vector3 right = Vector3.Normalize(Vector3.Cross(up, normal));
        up = Vector3.Cross(normal, right);
        return pose.Position + Vector3.Transform(claw.LocalPosition + normal * .33f - up * .003f, pose.Orientation);
    }

    /// <summary>Closest segment/sphere hit, including an origin inside the membrane.</summary>
    public static float? Intersect(Vector3 start, Vector3 end, Vector3 center, float radius)
    {
        Vector3 offset = start - center, delta = end - start;
        float c = offset.LengthSquared() - radius * radius;
        if (c <= 0) { return 0; }
        float a = delta.LengthSquared(), b = Vector3.Dot(offset, delta);
        float discriminant = b * b - a * c;
        if (a < 1e-12f || discriminant < 0) { return null; }
        float fraction = (-b - MathF.Sqrt(discriminant)) / a;
        return fraction is >= 0 and <= 1 ? fraction : null;
    }
}
