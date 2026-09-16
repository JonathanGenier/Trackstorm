using Godot;

namespace Trackstorm.Client.Vehicles;

/// <summary>Instantiates the shared static art beneath the caller's presentation transform.</summary>
internal static class VehicleVisual
{
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
