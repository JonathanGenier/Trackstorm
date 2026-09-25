using Godot;

namespace Trackstorm.Client.Vehicles;

/// <summary>Separates authored driveable support from static obstacle sides and bevels.</summary>
internal static class EnvironmentContact
{
    /// <summary>Uses the exposed union surface instead of buried end caps at overlapping module seams.</summary>
    internal static Vector3 ExposedNormal(PhysicsBody3D body, Vector3 origin, Vector3 point, Vector3 normal)
    {
        Vector3 start = new(origin.X, point.Y, origin.Z);
        Vector3 direction = point - start;
        if (direction.LengthSquared() < 0.01f) { return normal; }
        using var query = PhysicsRayQueryParameters3D.Create(start, point + direction.Normalized() * 0.1f, body.CollisionMask, new Godot.Collections.Array<Rid> { body.GetRid() });
        var hit = body.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0 && IsObstacle(hit["collider"].AsGodotObject(), hit["normal"].AsVector3())) { return hit["normal"].AsVector3().Normalized(); }
        return normal;
    }

    internal static bool IsObstacle(GodotObject? collider, Vector3 normal) =>
        collider is StaticBody3D and not Networking.NetworkVehicleBody && normal.Y > -0.55f &&
        (normal.Y < 0.55f || (normal.Y < 0.95f && collider is not SurfaceBody && collider is Node node && !node.IsInGroup("landing_terrain")));
}
