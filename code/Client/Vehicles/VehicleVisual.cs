using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

/// <summary>Instantiates the existing art beneath the caller's presentation transform.</summary>
internal static class VehicleVisual
{
    /// <summary>Shared sprung chassis envelope; tires are supported by wheel rays rather than solid cylinders.</summary>
    /// <returns>An unscaled collision shape used identically offline and online.</returns>
    internal static CollisionShape3D CreateCollision()
    {
        return new CollisionShape3D { Shape = new ConvexPolygonShape3D { Points = VehicleHull.Vertices.Select(VehicleBody.ToGodot).ToArray() } };
    }

    /// <summary>Creates presentation only; optional suspension observations cannot change collision or authority.</summary>
    /// <param name="identification">Per-vehicle identification and existing combat feedback material.</param>
    /// <param name="suspension">Optional current movement observation and accepted tuning for tire travel.</param>
    /// <returns>A caller-owned model root.</returns>
    internal static Node3D Create(Material identification, Func<(VehicleState State, VehicleConfiguration Configuration)?>? suspension = null)
    {
        var model = Bootstrap.StartupController.LoadResource<PackedScene>("res://assets/vehicles/WastelandVehicle.tscn").Instantiate<Node3D>();
        model.GetNode<MeshInstance3D>("Identification").MaterialOverride = identification;
        model.AddChild(new CarDeployment { Configuration = () => suspension?.Invoke()?.Configuration });
        model.AddChild(new CarLighting { Source = () => suspension?.Invoke()?.State });
        if (suspension is not null) { model.AddChild(new WheelPresentation { Source = suspension }); }
        return model;
    }
}
