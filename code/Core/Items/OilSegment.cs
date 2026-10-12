using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>A planar capsule in one logical hazard; equal endpoints describe a circular puddle.</summary>
public readonly record struct OilSegment(Vector3 Start, Vector3 End, Vector3 Normal, float Radius)
{
    /// <summary>Validates portable geometry without introducing another lifetime or contact quota.</summary>
    public void Validate()
    {
        if (!VehiclePhysicsState.IsFinite(Start) || !VehiclePhysicsState.IsFinite(End) ||
            !VehiclePhysicsState.IsFinite(Normal) || Math.Abs(Normal.LengthSquared() - 1) > 0.001f ||
            Normal.Y < 0.55f || !float.IsFinite(Radius) || Radius is < 0.05f or > 5 ||
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
        return (planar - along * fraction).LengthSquared() <= Radius * Radius;
    }
}
