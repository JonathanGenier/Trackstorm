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
        float[] compression = new float[4];
        SurfaceType?[] wheelSurfaces = new SurfaceType?[4];
        Vector3 normal = Vector3.Zero;
        Vector3 terrainNormal = Vector3.Zero;
        SurfaceType surface = SurfaceType.Concrete;
        SurfaceIdentity? identity = null;
        int index = 0;
        foreach (float z in new[] { -configuration.Wheelbase / 2, configuration.Wheelbase / 2 })
        {
            foreach (float x in new[] { -VehicleDimensions.WheelTrack / 2, VehicleDimensions.WheelTrack / 2 })
            {
                Vector3 origin = pose * new Vector3(x, 0, z);
                using var query = PhysicsRayQueryParameters3D.Create(origin, origin + (-pose.Basis.Y * configuration.SuspensionLength), body.CollisionMask | 8u, new Godot.Collections.Array<Rid> { body.GetRid() });
                var hit = body.GetWorld3D().DirectSpaceState.IntersectRay(query);
                if (hit.Count > 0 && hit["normal"].AsVector3().Y >= configuration.SupportNormalMinimum && !EnvironmentContact.IsObstacle(hit["collider"].AsGodotObject(), hit["normal"].AsVector3()))
                {
                    compression[index] = Math.Clamp(configuration.SuspensionLength - origin.DistanceTo(hit["position"].AsVector3()), 0, 1);
                    normal += hit["normal"].AsVector3();
                    if (hit["collider"].AsGodotObject() is Node terrain && terrain.IsInGroup("landing_terrain")) { terrainNormal += hit["normal"].AsVector3(); }
                    surface = (hit["collider"].AsGodotObject() as SurfaceBody)?.Surface ?? SurfaceType.Concrete;
                    identity = SurfaceIdentityResolver.Resolve(hit["collider"].AsGodotObject(), hit["position"].AsVector3());
                    wheelSurfaces[index] = SurfaceHandling.Resolve(identity, surface);
                }

                index++;
            }
        }

        using var center = PhysicsRayQueryParameters3D.Create(pose.Origin, pose.Origin + (-pose.Basis.Y * configuration.SuspensionLength), body.CollisionMask | 8u, new Godot.Collections.Array<Rid> { body.GetRid() });
        var centerHit = body.GetWorld3D().DirectSpaceState.IntersectRay(center);
        if (centerHit.Count > 0 && centerHit["normal"].AsVector3().Y >= configuration.SupportNormalMinimum && !EnvironmentContact.IsObstacle(centerHit["collider"].AsGodotObject(), centerHit["normal"].AsVector3()))
        {
            surface = (centerHit["collider"].AsGodotObject() as SurfaceBody)?.Surface ?? SurfaceType.Concrete;
            identity = SurfaceIdentityResolver.Resolve(centerHit["collider"].AsGodotObject(), centerHit["position"].AsVector3());
        }

        return (new WheelSupport(new System.Numerics.Vector4(compression[0], compression[1], compression[2], compression[3]), wheelSurfaces[0], wheelSurfaces[1], wheelSurfaces[2], wheelSurfaces[3]), normal.IsZeroApprox() ? Vector3.Zero : normal.Normalized(), SurfaceHandling.Resolve(identity, surface), terrainNormal.IsZeroApprox() ? Vector3.Zero : terrainNormal.Normalized(), identity);
    }
}
