using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

/// <summary>Shared native ray observations for practice, host and prediction replay; Core computes all spring forces.</summary>
internal static class WheelSuspension
{
    /// <summary>Samples individual wheels at the fixed-step command boundary.</summary>
    /// <param name="body">Collision proxy to exclude.</param>
    /// <param name="pose">Solved body pose.</param>
    /// <param name="configuration">Shared suspension dimensions.</param>
    /// <returns>Plain compression, support normal and center-selected surface.</returns>
    internal static (WheelSupport Wheels, Vector3 Normal, SurfaceType Surface, Vector3 TerrainNormal, SurfaceIdentity? Identity) Observe(PhysicsBody3D body, Transform3D pose, VehicleConfiguration configuration)
    {
        Span<float> compression = stackalloc float[4];
        Span<SurfaceType?> wheelSurfaces = stackalloc SurfaceType?[4];
        compression.Clear();
        wheelSurfaces.Clear();
        Vector3 normal = Vector3.Zero;
        Vector3 terrainNormal = Vector3.Zero;
        SurfaceType surface = SurfaceType.Concrete;
        SurfaceIdentity? identity = null;
        var excluded = new Godot.Collections.Array<Rid> { body.GetRid() };
        using var query = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Zero, body.CollisionMask | 8u, excluded);
        var space = body.GetWorld3D().DirectSpaceState;
        for (int index = 0; index < 4; index++)
        {
            float z = index < 2 ? -configuration.Wheelbase / 2 : configuration.Wheelbase / 2;
            float x = index % 2 == 0 ? -VehicleDimensions.WheelTrack / 2 : VehicleDimensions.WheelTrack / 2;
            Vector3 origin = pose * new Vector3(x, 0, z);
            query.From = origin;
            query.To = origin + (-pose.Basis.Y * configuration.SuspensionLength);
            using var hit = space.IntersectRay(query);
            if (hit.Count > 0 && hit["normal"].AsVector3().Y >= configuration.SupportNormalMinimum && !EnvironmentContact.IsObstacle(hit["collider"].AsGodotObject(), hit["normal"].AsVector3()))
            {
                compression[index] = Math.Clamp(configuration.SuspensionLength - origin.DistanceTo(hit["position"].AsVector3()), 0, 1);
                normal += hit["normal"].AsVector3();
                if (hit["collider"].AsGodotObject() is Node terrain && terrain.IsInGroup("landing_terrain")) { terrainNormal += hit["normal"].AsVector3(); }
                surface = (hit["collider"].AsGodotObject() as SurfaceBody)?.Surface ?? SurfaceType.Concrete;
                identity = SurfaceIdentityResolver.Resolve(hit["collider"].AsGodotObject(), hit["position"].AsVector3());
                wheelSurfaces[index] = SurfaceHandling.Resolve(identity, surface);
            }
        }

        query.From = pose.Origin;
        query.To = pose.Origin + (-pose.Basis.Y * configuration.SuspensionLength);
        using var centerHit = space.IntersectRay(query);
        if (centerHit.Count > 0 && centerHit["normal"].AsVector3().Y >= configuration.SupportNormalMinimum && !EnvironmentContact.IsObstacle(centerHit["collider"].AsGodotObject(), centerHit["normal"].AsVector3()))
        {
            surface = (centerHit["collider"].AsGodotObject() as SurfaceBody)?.Surface ?? SurfaceType.Concrete;
            identity = SurfaceIdentityResolver.Resolve(centerHit["collider"].AsGodotObject(), centerHit["position"].AsVector3());
        }

        return (new WheelSupport(new System.Numerics.Vector4(compression[0], compression[1], compression[2], compression[3]), wheelSurfaces[0], wheelSurfaces[1], wheelSurfaces[2], wheelSurfaces[3]), normal.IsZeroApprox() ? Vector3.Zero : normal.Normalized(), SurfaceHandling.Resolve(identity, surface), terrainNormal.IsZeroApprox() ? Vector3.Zero : terrainNormal.Normalized(), identity);
    }
}
