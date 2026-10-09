using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Items;

/// <summary>Reads authored static drivable ground only; roofs, props, water and vehicle bodies cannot attract missiles.</summary>
internal static class MissileTerrainQuery
{
    internal static MissileTerrainSample? Cast(PhysicsDirectSpaceState3D space, N.Vector3 from, N.Vector3 to)
    {
        using var ray = PhysicsRayQueryParameters3D.Create(VehicleBody.ToGodot(from), VehicleBody.ToGodot(to), 1);
        using var hit = space.IntersectRay(ray);
        if (hit.Count == 0 || hit["collider"].AsGodotObject() is not StaticBody3D body) { return null; }
        Vector3 position = hit["position"].AsVector3();
        var identity = SurfaceIdentityResolver.Resolve(body, position);
        if (identity is null or SurfaceIdentity.Water) { return null; }
        return new(VehicleBody.ToCore(position), VehicleBody.ToCore(hit["normal"].AsVector3()));
    }
}
