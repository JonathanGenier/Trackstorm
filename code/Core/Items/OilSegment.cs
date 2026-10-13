using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>A planar capsule in one logical hazard; equal endpoints describe a circular puddle.</summary>
public readonly record struct OilSegment(Vector3 Start, Vector3 End, Vector3 Normal, float Radius)
{
    /// <summary>Validates portable geometry without introducing another lifetime.</summary>
    public void Validate()
    {
        if (!VehiclePhysicsState.IsFinite(Start) || !VehiclePhysicsState.IsFinite(End) ||
            !VehiclePhysicsState.IsFinite(Normal) || Math.Abs(Normal.LengthSquared() - 1) > 0.001f ||
            !float.IsFinite(Radius) || Radius is < 0.05f or > 5 ||
            Vector3.DistanceSquared(Start, End) > 10000 || Math.Abs(Vector3.Dot(End - Start, Normal)) > 0.01f)
        {
            throw new ArgumentException("Invalid Oil footprint segment.");
        }
    }

    internal bool Contains(Vector3 point)
    {
        Vector3 offset = point - Start;
        float height = Vector3.Dot(offset, Normal);
        if (Math.Abs(height) > 0.08f) { return false; }
        Vector3 along = End - Start;
        Vector3 planar = offset - height * Normal;
        float fraction = along.LengthSquared() > 0 ? Math.Clamp(Vector3.Dot(planar, along) / along.LengthSquared(), 0, 1) : 0;
        Vector3 radial = planar - along * fraction;
        Vector3 tangent = Vector3.Normalize(Vector3.Cross(Normal, Math.Abs(Normal.Z) < .9f ? Vector3.UnitZ : Vector3.UnitX));
        float angle = MathF.Atan2(Vector3.Dot(radial, Vector3.Cross(Normal, tangent)), Vector3.Dot(radial, tangent));
        float radius = EdgeRadius(angle);
        return radial.LengthSquared() <= radius * radius;
    }

    /// <summary>The same organic boundary used by the rendered surface and tire coverage.</summary>
    public float EdgeRadius(float angle) => Radius * (.80f + .12f * MathF.Sin(3 * angle + Start.X) + .05f * MathF.Sin(5 * angle + Start.Z) + .03f * MathF.Sin(11 * angle + Start.X * .7f));
}
