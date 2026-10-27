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

    /// <summary>Final wing hinge angle, opening toward the vehicle as in the approved plan view.</summary>
    public const float DeployedWingAngle = MathF.PI / 4;

    private static readonly (Vector3 Center, Vector3 Size, Quaternion Rotation)[] DeployedPanels =
    [
        (Vector3.Zero, new(3.7f, 2.5f, .28f), Quaternion.Identity),
        Panel(-1), Panel(1),
    ];

    private static (Vector3 Center, Vector3 Size, Quaternion Rotation) Panel(int side)
    {
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, side * DeployedWingAngle);
        return (new Vector3(side * 1.85f, 0, 0) + Vector3.Transform(new Vector3(side * .725f, 0, 0), rotation),
            new(1.45f, 2.5f, .28f), rotation);
    }

    /// <summary>Captured scale applied to the authored three-panel shape.</summary>
    public static Vector3 DeployedScale(Vector3 size) => size / new Vector3(6.6f, 2.5f, .6f);

    /// <summary>Convex panel corners in wall-local space, shared with native collision and placement.</summary>
    public static IReadOnlyList<Vector3[]> DeployedCorners(Vector3 size)
    {
        var scale = DeployedScale(size);
        return DeployedPanels.Select(panel =>
            (from x in new[] { -1, 1 } from y in new[] { -1, 1 } from z in new[] { -1, 1 }
             select (panel.Center + Vector3.Transform(panel.Size * new Vector3(x, y, z) / 2, panel.Rotation)) * scale).ToArray()).ToArray();
    }

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
    {
        var inverse = Quaternion.Conjugate(wall.Orientation);
        var scale = DeployedScale(wall.WallSize);
        start = Vector3.Transform(start - wall.Position, inverse) / scale;
        end = Vector3.Transform(end - wall.Position, inverse) / scale;
        return DeployedPanels.Select(panel => IntersectBox(panel.Center, panel.Rotation, panel.Size, start, end))
            .Where(hit => hit.HasValue).DefaultIfEmpty(null).Min();
    }

    /// <summary>Nearest point in the physical armor, for finite-radius Salvo damage.</summary>
    public static Vector3 ClosestPoint(ShieldState shield, VehiclePhysicsState pose, Vector3 point)
    {
        if (!shield.Attached)
        {
            if (Intersect(shield, point, point).HasValue) { return point; }
            var local = Vector3.Transform(point - shield.Position, Quaternion.Conjugate(shield.Orientation));
            var nearest = DeployedCorners(shield.WallSize).SelectMany(corners => Faces.SelectMany(face => new[]
            {
                ClosestTriangle(local, corners[face[0]], corners[face[1]], corners[face[2]]),
                ClosestTriangle(local, corners[face[0]], corners[face[2]], corners[face[3]]),
            })).MinBy(candidate => Vector3.DistanceSquared(local, candidate));
            return shield.Position + Vector3.Transform(nearest, shield.Orientation);
        }
        return MountedBoxes.Select(box => ClosestBox(pose.Position + Vector3.Transform(box.Center, pose.Orientation), pose.Orientation, box.Size, point))
            .MinBy(candidate => Vector3.DistanceSquared(point, candidate));
    }

    private static Vector3 ClosestBox(Vector3 center, Quaternion orientation, Vector3 size, Vector3 point)
        => center + Vector3.Transform(Vector3.Clamp(Vector3.Transform(point - center, Quaternion.Conjugate(orientation)), -size / 2, size / 2), orientation);

    /// <summary>Separating-axis test for deployment candidates, including two uses in one batch.</summary>
    public static bool Overlaps(ShieldState first, ShieldState second)
    {
        var firstPanels = DeployedCorners(first.WallSize).Select(c => c.Select(p => first.Position + Vector3.Transform(p, first.Orientation)).ToArray());
        var secondPanels = DeployedCorners(second.WallSize).Select(c => c.Select(p => second.Position + Vector3.Transform(p, second.Orientation)).ToArray()).ToArray();
        return firstPanels.Any(a => secondPanels.Any(b => ConvexOverlaps(a, b)));
    }

    private static readonly int[][] Faces = [[0, 1, 3, 2], [4, 6, 7, 5], [0, 4, 5, 1], [2, 3, 7, 6], [0, 2, 6, 4], [1, 5, 7, 3]];

    private static bool ConvexOverlaps(Vector3[] a, Vector3[] b)
    {
        Vector3[] Edges(Vector3[] p) => [p[4] - p[0], p[2] - p[0], p[1] - p[0]];
        var ae = Edges(a); var be = Edges(b);
        var axes = Faces.Select(f => Vector3.Cross(a[f[1]] - a[f[0]], a[f[2]] - a[f[0]]))
            .Concat(Faces.Select(f => Vector3.Cross(b[f[1]] - b[f[0]], b[f[2]] - b[f[0]])))
            .Concat(ae.SelectMany(x => be.Select(y => Vector3.Cross(x, y))));
        foreach (var axis in axes)
        {
            if (axis.LengthSquared() < .000001f) { continue; }
            if (a.Max(p => Vector3.Dot(p, axis)) <= b.Min(p => Vector3.Dot(p, axis)) ||
                b.Max(p => Vector3.Dot(p, axis)) <= a.Min(p => Vector3.Dot(p, axis))) { return false; }
        }
        return true;
    }

    private static Vector3 ClosestTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a; var ac = c - a; var ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) { return a; }
        var bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) { return b; }
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) { return a + ab * (d1 / (d1 - d3)); }
        var cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) { return c; }
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) { return a + ac * (d2 / (d2 - d6)); }
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) { return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))); }
        float inverse = 1 / (va + vb + vc);
        return a + ab * (vb * inverse) + ac * (vc * inverse);
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
