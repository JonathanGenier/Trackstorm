using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

/// <summary>Shared native ray observations for practice, host and prediction replay; Core computes all spring forces.</summary>
internal static class WheelSuspension
{
    /// <summary>Uses physical wheel compression and steering for reconstructable wheel visuals.</summary>
    /// <param name="wheels">Visual wheels in observation order.</param>
    /// <param name="state">Accepted handling state.</param>
    /// <param name="length">Full suspension extension.</param>
    /// <param name="radius">Visual wheel radius.</param>
    internal static void Present(IReadOnlyList<Node3D> wheels, VehicleState state, float length, float radius)
    {
        float[] compression = [state.Wheels.Compression.X, state.Wheels.Compression.Y, state.Wheels.Compression.Z, state.Wheels.Compression.W];
        for (int index = 0; index < wheels.Count; index++)
        {
            Node3D wheel = wheels[index];
            wheel.Position = new Vector3(wheel.Position.X, -length + compression[index] + radius, wheel.Position.Z);
            wheel.Rotation = new Vector3(0, index < 2 ? -state.SteeringAngle : 0, wheel.Rotation.Z);
        }
    }

    /// <summary>Samples individual wheels at the fixed-step command boundary.</summary>
    /// <param name="body">Collision proxy to exclude.</param>
    /// <param name="pose">Solved body pose.</param>
    /// <param name="configuration">Shared suspension dimensions.</param>
    /// <returns>Plain compression, support normal and center-selected surface.</returns>
    internal static (WheelSupport Wheels, Vector3 Normal, SurfaceType Surface) Observe(PhysicsBody3D body, Transform3D pose, VehicleConfiguration configuration)
    {
        float[] compression = new float[4];
        Vector3 normal = Vector3.Zero;
        SurfaceType surface = SurfaceType.Concrete;
        int index = 0;
        foreach (float z in new[] { -configuration.Wheelbase / 2, configuration.Wheelbase / 2 })
        {
            foreach (float x in new[] { -0.85f, 0.85f })
            {
                Vector3 origin = pose * new Vector3(x, 0, z);
                using var query = PhysicsRayQueryParameters3D.Create(origin, origin + (Vector3.Down * configuration.SuspensionLength), body.CollisionMask, new Godot.Collections.Array<Rid> { body.GetRid() });
                var hit = body.GetWorld3D().DirectSpaceState.IntersectRay(query);
                if (hit.Count > 0 && hit["normal"].AsVector3().Y >= 0.55f)
                {
                    compression[index] = Math.Clamp(configuration.SuspensionLength - origin.DistanceTo(hit["position"].AsVector3()), 0, 1);
                    normal += hit["normal"].AsVector3();
                    surface = (hit["collider"].AsGodotObject() as SurfaceBody)?.Surface ?? SurfaceType.Concrete;
                }

                index++;
            }
        }

        using var center = PhysicsRayQueryParameters3D.Create(pose.Origin, pose.Origin + (Vector3.Down * configuration.SuspensionLength), body.CollisionMask, new Godot.Collections.Array<Rid> { body.GetRid() });
        var centerHit = body.GetWorld3D().DirectSpaceState.IntersectRay(center);
        if (centerHit.Count > 0 && centerHit["normal"].AsVector3().Y >= 0.55f)
        {
            surface = (centerHit["collider"].AsGodotObject() as SurfaceBody)?.Surface ?? SurfaceType.Concrete;
        }

        return (new WheelSupport(new System.Numerics.Vector4(compression[0], compression[1], compression[2], compression[3])), normal.IsZeroApprox() ? Vector3.Zero : normal.Normalized(), surface);
    }
}
