using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Items;

/// <summary>Sweeps static world geometry; unsupported/water impacts retire parcels without inventing ground.</summary>
internal static class OilSurfaceQuery
{
    internal static OilImpact? Cast(PhysicsDirectSpaceState3D space, N.Vector3 from, N.Vector3 to, float radius)
    {
        // Network cars are manually advanced StaticBody3D proxies on layer 2,
        // not stationary world support. Oil has no projectile-on-car effect;
        // sweep through them to the actual world instead of freezing a spill
        // at their old pose. Movable world obstacles still intercept the ray.
        using var ray = PhysicsRayQueryParameters3D.Create(VehicleBody.ToGodot(from), VehicleBody.ToGodot(to), 1 | 16 | 32);
        ray.HitFromInside = true;
        using var hit = space.IntersectRay(ray);
        if (hit.Count == 0) { return null; }
        Vector3 position = hit["position"].AsVector3();
        Vector3 normal = hit["normal"].AsVector3().Normalized();
        if (normal.LengthSquared() < .5f) { return new(VehicleBody.ToCore(position), N.Vector3.UnitY, 0); }
        if (hit["collider"].AsGodotObject() is not StaticBody3D body ||
            body is Networking.NetworkVehicleBody or AnimatableBody3D ||
            body.ConstantLinearVelocity.LengthSquared() > .000001f || body.ConstantAngularVelocity.LengthSquared() > .000001f ||
            (body.CollisionLayer & (16 | 32)) != 0 || Arenas.DestructibleEnvironment.RockId(body) != 0)
        { return new(VehicleBody.ToCore(position), VehicleBody.ToCore(normal), 0); }
        if (SurfaceIdentityResolver.Resolve(body, position) == SurfaceIdentity.Water) { return new(VehicleBody.ToCore(position), VehicleBody.ToCore(normal), 0); }
        Vector3 tangent = normal.Cross(Math.Abs(normal.Z) < .9f ? Vector3.Back : Vector3.Right).Normalized();
        for (int attempt = 0; attempt < 4; attempt++, radius *= .5f)
        {
            bool supported = true;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.Tau / 8;
                Vector3 point = position + radius * (tangent * Mathf.Cos(angle) + normal.Cross(tangent) * Mathf.Sin(angle));
                ray.From = point + normal * .1f; ray.To = point - normal * .1f;
                using var sample = space.IntersectRay(ray);
                if (sample.Count == 0 || sample["collider"].AsGodotObject() != body ||
                    sample["normal"].AsVector3().Dot(normal) < .98f || Math.Abs((sample["position"].AsVector3() - point).Dot(normal)) > .01f)
                { supported = false; break; }
            }
            if (supported && radius >= .1f) { return new(VehicleBody.ToCore(position), VehicleBody.ToCore(normal), radius); }
        }
        return new(VehicleBody.ToCore(position), VehicleBody.ToCore(normal), 0);
    }
}
