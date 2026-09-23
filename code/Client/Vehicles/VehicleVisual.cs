using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

/// <summary>Instantiates the shared static art beneath the caller's presentation transform.</summary>
internal static class VehicleVisual
{
    /// <summary>Shared sprung chassis envelope; tires are supported by wheel rays rather than solid cylinders.</summary>
    /// <returns>An unscaled collision shape used identically offline and online.</returns>
    internal static CollisionShape3D CreateCollision()
    {
        float centerY = (0.02975f * VehicleDimensions.Scale) + VehicleDimensions.OriginShift;
        float centerZ = -0.0375f * VehicleDimensions.Scale;
        float bottom = centerY - (1.1755f * VehicleDimensions.Scale / 2);
        float top = centerY + (1.1755f * VehicleDimensions.Scale / 2);
        var points = new List<Vector3>();
        foreach (float side in new[] { -1f, 1f })
        {
            float x = side * VehicleDimensions.Width / 2;
            foreach (float end in new[] { -1f, 1f })
            {
                float z = centerZ + (end * VehicleDimensions.Length / 2);
                points.Add(new Vector3(x, top, z));
                // The old box filled empty space beneath the bumper overhangs and struck crests
                // before the wheel rays. Keep the full silhouette, with the underbody between axles.
                points.Add(new Vector3(x, bottom + VehicleDimensions.WheelRadius, z));
                points.Add(new Vector3(x, bottom, end * VehicleDimensions.Wheelbase / 2));
            }
        }

        return new CollisionShape3D { Shape = new ConvexPolygonShape3D { Points = points.ToArray() } };
    }

    /// <summary>Creates a visual only; no collision, authority or handling state is consulted.</summary>
    /// <param name="identification">Per-vehicle identification and existing combat feedback material.</param>
    /// <returns>A caller-owned static model root.</returns>
    internal static Node3D Create(Material identification)
    {
        var model = GD.Load<PackedScene>("res://assets/vehicles/WastelandVehicle.tscn").Instantiate<Node3D>();
        model.GetNode<MeshInstance3D>("Identification").MaterialOverride = identification;
        return model;
    }
}
