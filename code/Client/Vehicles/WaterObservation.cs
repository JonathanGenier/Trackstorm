using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

/// <summary>Samples authored Water fields independently of wheel rays and terrain collision continuity.</summary>
internal static class WaterObservation
{
    private static readonly Vector3[] Footprint = [Vector3.Zero,
        new(-VehicleDimensions.WheelTrack / 2, 0, -VehicleDimensions.Wheelbase / 2),
        new(VehicleDimensions.WheelTrack / 2, 0, -VehicleDimensions.Wheelbase / 2),
        new(-VehicleDimensions.WheelTrack / 2, 0, VehicleDimensions.Wheelbase / 2),
        new(VehicleDimensions.WheelTrack / 2, 0, VehicleDimensions.Wheelbase / 2)];

    internal static float Observe(PhysicsBody3D body, Transform3D pose)
    {
        float depth = 0;
        var members = body.GetTree().GetNodesInGroup("water_terrain");
        foreach (Node member in members)
        {
            if (member is not Node3D terrain || terrain.GetWorld3D() != body.GetWorld3D() ||
                !terrain.HasMeta("water_level") || !terrain.HasMeta("surface_bounds")) { continue; }
            Vector4 bounds = terrain.GetMeta("surface_bounds").AsVector4();
            float level = terrain.ToGlobal(new Vector3(0, terrain.GetMeta("water_level").AsSingle(), 0)).Y;
            // Center plus the tire footprint prevents a missed wheel ray or an inverted body
            // from turning off immersion. No overlap signals or retained enter/exit latches.
            foreach (Vector3 offset in Footprint)
            {
                Vector3 point = pose * offset;
                Vector3 local = terrain.ToLocal(point);
                if (local.X < bounds.X || local.Z < bounds.Y || local.X > bounds.X + bounds.Z || local.Z > bounds.Y + bounds.W) { continue; }
                if (SurfaceIdentityResolver.Resolve(terrain, point) == SurfaceIdentity.Water)
                {
                    depth = Math.Max(depth, Math.Clamp(level - point.Y + VehicleDimensions.RideHeight, 0, 1000));
                }
            }
        }
        return depth;
    }
}
