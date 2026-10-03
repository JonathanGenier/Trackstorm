using Godot;

namespace Trackstorm.Client.Vehicles;

/// <summary>Separates authored driveable support from static obstacle sides and bevels.</summary>
internal static class EnvironmentContact
{
    /// <summary>Downward wheel rays may support on a rock's top slope without treating its side as a driving ramp.</summary>
    internal static bool IsWheelSupport(GodotObject? collider, Vector3 normal) =>
        !IsObstacle(collider, normal) || Arenas.DestructibleEnvironment.RockId(collider) != 0;

    /// <summary>Uses the exposed union surface instead of buried end caps at overlapping module seams.</summary>
    internal static Vector3 ExposedNormal(PhysicsBody3D body, Vector3 origin, Vector3 point, Vector3 normal)
    {
        Vector3 start = new(origin.X, point.Y, origin.Z);
        Vector3 direction = point - start;
        if (direction.LengthSquared() < 0.01f) { return normal; }
        using var query = PhysicsRayQueryParameters3D.Create(start, point + direction.Normalized() * 0.1f, body.CollisionMask, new Godot.Collections.Array<Rid> { body.GetRid() });
        using var hit = body.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0 && IsObstacle(hit["collider"].AsGodotObject(), hit["normal"].AsVector3())) { return hit["normal"].AsVector3().Normalized(); }
        return normal;
    }

    /// <summary>Uses the authored triangle face for driveable mesh contact, not a sweep's internal-edge separating axis.</summary>
    internal static Vector3 SupportFaceNormal(PhysicsBody3D body, GodotObject? collider, Vector3 point, Vector3 normal)
    {
        if (normal.Y < 0.55f || collider is not Node terrain || !terrain.IsInGroup("landing_terrain")) { return normal; }
        // The short vertical probe must hit the same collider at this contact. It cannot
        // borrow a wheel normal from another surface or flatten an actual ramp/obstacle.
        using var query = PhysicsRayQueryParameters3D.Create(point + Vector3.Up * 0.1f, point - Vector3.Up * 0.1f,
            body.CollisionMask, new Godot.Collections.Array<Rid> { body.GetRid() });
        using var hit = body.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 || hit["collider"].AsGodotObject() != collider) { return normal; }
        Vector3 face = hit["normal"].AsVector3().Normalized();
        return face.Y >= 0.55f && face.Dot(normal) >= 0.9f ? face : normal;
    }

    internal static bool IsObstacle(GodotObject? collider, Vector3 normal) =>
        collider is StaticBody3D and not Networking.NetworkVehicleBody && normal.Y > -0.55f &&
        (normal.Y < 0.55f || (normal.Y < 0.95f && collider is not SurfaceBody && collider is Node node && !node.IsInGroup("landing_terrain")));
}
