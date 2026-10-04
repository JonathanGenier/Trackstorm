using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Fits the rigid wall's base to its terrain footprint while preserving horizontal facing.</summary>
internal static class ShieldGround
{
    internal static Transform3D? Seat(PhysicsDirectSpaceState3D space, Vector3 center, Vector3 heading, Vector3 size)
    {
        heading.Y = 0;
        if (heading.LengthSquared() < 0.01f) { return null; }
        heading = heading.Normalized();
        var right = Vector3.Up.Cross(heading);
        Vector3? Sample(Vector3 offset)
        {
            var point = center + offset;
            using var ray = PhysicsRayQueryParameters3D.Create(point + Vector3.Up * (size.Y / 2 + 2), point - Vector3.Up * (size.Y / 2 + 8), 1);
            var hit = space.IntersectRay(ray);
            return hit.Count > 0 && hit["collider"].AsGodotObject() is StaticBody3D && hit["normal"].AsVector3().Y >= 0.55f
                ? hit["position"].AsVector3() : null;
        }
        if (Sample(Vector3.Zero) is not { } middle || Sample(right * size.X / 2) is not { } east ||
            Sample(-right * size.X / 2) is not { } west || Sample(heading * size.Z / 2) is not { } back ||
            Sample(-heading * size.Z / 2) is not { } front) { return null; }
        var up = (back - front).Cross(east - west).Normalized();
        if (up.Y < 0.55f) { return null; }
        // Preserve the exact projected heading; projecting the vector onto the support
        // plane instead would change yaw on a compound slope.
        heading.Y = -(up.X * heading.X + up.Z * heading.Z) / up.Y;
        heading = heading.Normalized();
        var basis = new Basis(up.Cross(heading).Normalized(), up, heading);
        // A rigid base cannot bend around a crest. Seat on the highest sampled support.
        float support = new[] { middle, east, west, back, front }.Max(p => up.Dot(p));
        center.Y = (support + size.Y / 2 + 0.015f - up.X * center.X - up.Z * center.Z) / up.Y;
        return new(basis, center);
    }
}
