using System.Numerics;

namespace Trackstorm.Core.Vehicles;

/// <summary>The production sprung chassis envelope, shared by native collision and short-range blasts.</summary>
public static class VehicleHull
{
    private static readonly Vector2[] Profile = CreateProfile();
    /// <summary>Unscaled convex vertices; tires remain supported by wheel rays.</summary>
    public static IReadOnlyList<Vector3> Vertices { get; } = Array.AsReadOnly(
        new[] { -1f, 1f }.SelectMany(side => Profile.Select(p => new Vector3(side * VehicleDimensions.Width / 2, p.Y, p.X))).ToArray());

    /// <summary>Nearest point in the oriented chassis, preserving the raised bumper undercuts.</summary>
    public static Vector3 ClosestPoint(VehiclePhysicsState pose, Vector3 point)
    {
        Vector3 local = Vector3.Transform(point - pose.Position, Quaternion.Conjugate(pose.Orientation));
        var yz = new Vector2(local.Z, local.Y);
        Vector2 nearest = yz;
        float distance = float.MaxValue;
        bool positive = false, negative = false;
        for (int i = 0; i < Profile.Length; i++)
        {
            Vector2 a = Profile[i], edge = Profile[(i + 1) % Profile.Length] - a, offset = yz - a;
            float cross = edge.X * offset.Y - edge.Y * offset.X;
            positive |= cross > .000001f; negative |= cross < -.000001f;
            Vector2 candidate = a + edge * Math.Clamp(Vector2.Dot(offset, edge) / edge.LengthSquared(), 0, 1);
            float squared = Vector2.DistanceSquared(yz, candidate);
            if (squared < distance) { distance = squared; nearest = candidate; }
        }
        if (!(positive && negative)) { nearest = yz; }
        var closest = new Vector3(Math.Clamp(local.X, -VehicleDimensions.Width / 2, VehicleDimensions.Width / 2), nearest.Y, nearest.X);
        return pose.Position + Vector3.Transform(closest, pose.Orientation);
    }

    private static Vector2[] CreateProfile()
    {
        float centerY = .02975f * VehicleDimensions.Scale + VehicleDimensions.OriginShift;
        float centerZ = -.0375f * VehicleDimensions.Scale;
        float bottom = centerY - 1.1755f * VehicleDimensions.Scale / 2;
        float top = centerY + 1.1755f * VehicleDimensions.Scale / 2;
        float front = centerZ - VehicleDimensions.Length / 2, rear = centerZ + VehicleDimensions.Length / 2;
        return [new(front, top), new(rear, top), new(rear, bottom + VehicleDimensions.WheelRadius),
            new(VehicleDimensions.Wheelbase / 2, bottom), new(-VehicleDimensions.Wheelbase / 2, bottom), new(front, bottom + VehicleDimensions.WheelRadius)];
    }
}
