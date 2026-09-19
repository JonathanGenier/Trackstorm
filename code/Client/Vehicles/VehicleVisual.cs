using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

/// <summary>Instantiates the shared static art beneath the caller's presentation transform.</summary>
internal static class VehicleVisual
{
    /// <summary>Shared sprung chassis envelope; tires are supported by wheel rays rather than solid cylinders.</summary>
    /// <returns>An unscaled collision shape used identically offline and online.</returns>
    internal static CollisionShape3D CreateCollision() => new()
    {
        Position = new Vector3(0, (0.02975f * VehicleDimensions.Scale) + VehicleDimensions.OriginShift, -0.0375f * VehicleDimensions.Scale),
        Shape = new BoxShape3D { Size = new Vector3(VehicleDimensions.Width, 1.1755f * VehicleDimensions.Scale, VehicleDimensions.Length) },
    };

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
