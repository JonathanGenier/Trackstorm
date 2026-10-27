using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Fits an intact visual to static ground without moving its authoritative marker.</summary>
internal static class PickupGroundPose
{
    // Bottom bearing edges in the unchanged Blender asset, in metres.
    private const float Floor = .025f;
    private static readonly Vector3[] Feet =
    [new(-1.4f, Floor, -.65f), new(1.4f, Floor, -.65f), new(-1.4f, Floor, .65f), new(1.4f, Floor, .65f)];

    internal static Transform3D? Resolve(PhysicsDirectSpaceState3D space, Vector3 marker, Basis facing)
    {
        // Start near this marker, not above the whole map: the tunnel must use
        // its floor, while the elevated jump marker must find the tabletop.
        if (Ground(space, marker) is not { } center) return null;
        Vector3 forward = -facing.Z;
        Basis basis = Basis.LookingAt(forward.Slide(center.Normal).Normalized(), center.Normal);
        var pose = new Transform3D(basis, center.Position - basis.Y * Floor);
        var supports = new Vector3[4];
        for (int i = 0; i < Feet.Length; i++)
        {
            if (Ground(space, pose * Feet[i]) is not { } support) return pose;
            supports[i] = support.Position;
        }

        // Fit across both bottom crates instead of inheriting a single tiny
        // terrain triangle's normal. Preserve the route heading on that plane.
        Vector3 right = supports[1] + supports[3] - supports[0] - supports[2];
        Vector3 back = supports[2] + supports[3] - supports[0] - supports[1];
        Vector3 normal = back.Cross(right).Normalized();
        basis = Basis.LookingAt(forward.Slide(normal).Normalized(), normal);
        pose = new Transform3D(basis, center.Position - basis.Y * Floor);
        float gap = float.NegativeInfinity;
        foreach (Vector3 foot in Feet)
        {
            Vector3 point = pose * foot;
            if (Ground(space, point) is { } support) gap = MathF.Max(gap, point.Y - support.Position.Y);
        }
        // A rigid stack cannot bend with small terrain curvature. Seat its lowest
        // support instead of leaving daylight under a corner; no scale changes.
        if (float.IsFinite(gap)) pose.Origin -= Vector3.Up * gap;
        return pose;
    }

    private static (Vector3 Position, Vector3 Normal)? Ground(PhysicsDirectSpaceState3D space, Vector3 point)
    {
        using var ray = PhysicsRayQueryParameters3D.Create(point + Vector3.Up * .5f, point + Vector3.Down * 30, 1);
        using var hit = space.IntersectRay(ray);
        return hit.Count != 0 && hit["collider"].AsGodotObject() is StaticBody3D && hit["normal"].AsVector3().Y > .5f
            ? (hit["position"].AsVector3(), hit["normal"].AsVector3()) : null;
    }
}
