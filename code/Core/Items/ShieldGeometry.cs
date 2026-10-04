using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Production rear and side armor shared by authoritative intersection and native reconstruction.</summary>
public static class ShieldGeometry
{
    /// <summary>Vehicle-local center, behind the positive-Z bumper.</summary>
    public static Vector3 Center => new(0, 0.25f, 3.25f);
    /// <summary>Physical box dimensions in metres.</summary>
    public static Vector3 Size => new(3.7f, 2.5f, 0.28f);

    /// <summary>Three physical panels leave the vehicle interior and exposed forward sides open.</summary>
    public static IReadOnlyList<(Vector3 Center, Vector3 Size)> MountedBoxes { get; } = Array.AsReadOnly(new[]
    {
        (Center, Size),
        (new Vector3(-1.85f, .25f, 2.525f), new Vector3(.28f, 2.5f, 1.45f)),
        (new Vector3(1.85f, .25f, 2.525f), new Vector3(.28f, 2.5f, 1.45f)),
    });

    /// <summary>Tests an actual local contact, with only native solver margin tolerance.</summary>
    public static bool Contains(Vector3 localPoint)
    {
        return MountedBoxes.Any(box =>
        {
            var distance = Vector3.Abs(localPoint - box.Center);
            return distance.X <= box.Size.X / 2 + .02f && distance.Y <= box.Size.Y / 2 + .02f && distance.Z <= box.Size.Z / 2 + .02f;
        });
    }

    /// <summary>Closest segment intersection with the full oriented box, including starts inside it.</summary>
    public static float? Intersect(VehiclePhysicsState pose, Vector3 start, Vector3 end)
    {
        float? nearest = null;
        foreach (var box in MountedBoxes)
        {
            float? hit = IntersectBox(pose.Position + Vector3.Transform(box.Center, pose.Orientation), pose.Orientation, box.Size, start, end);
            if (hit is { } fraction && (nearest is null || fraction < nearest)) { nearest = fraction; }
        }
        return nearest;
    }

    /// <summary>Tests a deployed wall's captured physical envelope.</summary>
    public static float? Intersect(ShieldState wall, Vector3 start, Vector3 end)
        => IntersectBox(wall.Position, wall.Orientation, wall.WallSize, start, end);

    /// <summary>Separating-axis test for deployment candidates, including two uses in one batch.</summary>
    public static bool Overlaps(ShieldState first, ShieldState second)
    {
        var a = new[] { Vector3.Transform(Vector3.UnitX, first.Orientation), Vector3.Transform(Vector3.UnitY, first.Orientation), Vector3.Transform(Vector3.UnitZ, first.Orientation) };
        var b = new[] { Vector3.Transform(Vector3.UnitX, second.Orientation), Vector3.Transform(Vector3.UnitY, second.Orientation), Vector3.Transform(Vector3.UnitZ, second.Orientation) };
        var axes = a.Concat(b).Concat(a.SelectMany(x => b.Select(y => Vector3.Cross(x, y))));
        foreach (var axis in axes)
        {
            if (axis.LengthSquared() < 0.000001f) { continue; }
            float Radius(Vector3[] basis, Vector3 size) => (Math.Abs(Vector3.Dot(axis, basis[0])) * size.X +
                Math.Abs(Vector3.Dot(axis, basis[1])) * size.Y + Math.Abs(Vector3.Dot(axis, basis[2])) * size.Z) / 2;
            if (Math.Abs(Vector3.Dot(second.Position - first.Position, axis)) >= Radius(a, first.WallSize) + Radius(b, second.WallSize)) { return false; }
        }
        return true;
    }

    private static float? IntersectBox(Vector3 center, Quaternion orientation, Vector3 size, Vector3 start, Vector3 end)
    {
        var inverse = Quaternion.Conjugate(orientation);
        var origin = Vector3.Transform(start - center, inverse);
        var direction = Vector3.Transform(end - start, inverse);
        var half = size / 2;
        float near = 0, far = 1;
        for (int axis = 0; axis < 3; axis++)
        {
            float p = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
            float d = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
            float h = axis == 0 ? half.X : axis == 1 ? half.Y : half.Z;
            if (Math.Abs(d) < 0.000001f) { if (Math.Abs(p) > h) { return null; } continue; }
            float a = (-h - p) / d, b = (h - p) / d;
            near = Math.Max(near, Math.Min(a, b));
            far = Math.Min(far, Math.Max(a, b));
            if (near > far) { return null; }
        }
        return near;
    }
}
