using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Arenas;

/// <summary>Matching-build authored mesh bounds for small blasts against rocks.</summary>
public sealed record EnvironmentRockBounds(Vector3 Center, Vector3 Size, Quaternion Orientation)
{
    public Vector3 ClosestPoint(Vector3 point)
        => Center + Vector3.Transform(Vector3.Clamp(Vector3.Transform(point - Center, Quaternion.Conjugate(Orientation)), -Size / 2, Size / 2), Orientation);

    internal void Validate()
    {
        if (!VehiclePhysicsState.IsFinite(Center) || !VehiclePhysicsState.IsFinite(Size) || Size.X <= 0 || Size.Y <= 0 || Size.Z <= 0 ||
            !float.IsFinite(Orientation.LengthSquared()) || Math.Abs(Orientation.LengthSquared() - 1) > .001f)
        { throw new ArgumentException("Invalid rock blast bounds."); }
    }
}
