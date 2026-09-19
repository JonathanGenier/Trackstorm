using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Arenas;

/// <summary>Loads the active reusable map and extracts its authored spawn contract before authority is created.</summary>
internal static class ActiveMap
{
    /// <summary>The map used by ordinary practice and multiplayer entry.</summary>
    internal const string ScenePath = "res://scenes/maps/oval_foundation.tscn";

    /// <summary>Loads a fresh map without introducing gameplay nodes or legacy arena content.</summary>
    /// <returns>The scene-owned foundation.</returns>
    internal static Node3D Load() => GD.Load<PackedScene>(ScenePath).Instantiate<Node3D>();

    /// <summary>Reads the actual scene markers in stable slot order, without duplicating their coordinates in code.</summary>
    /// <param name="map">An identity-transform foundation instance.</param>
    /// <returns>Validated plain-data contract consumed by the single Core simulation.</returns>
    internal static ArenaConfiguration ReadConfiguration(Node3D map)
    {
        if (!map.Transform.IsEqualApprox(Transform3D.Identity))
        {
            throw new InvalidOperationException("The active map must preserve its authored world transform.");
        }

        var markers = map.GetNode<Node3D>("PlayerSpawns");
        if (!markers.Transform.IsEqualApprox(Transform3D.Identity))
        {
            throw new InvalidOperationException("PlayerSpawns must preserve map coordinates.");
        }

        ArenaSpawn[] players = markers.GetChildren().OfType<Marker3D>().OrderBy(marker => marker.Name.ToString(), StringComparer.Ordinal)
            .Select(marker => new ArenaSpawn(marker.Name, VehicleBody.ToCore(marker.Position), marker.Rotation.Y)).ToArray();
        MeshInstance3D track = map.GetNode<MeshInstance3D>("Geometry/Track");
        Aabb bounds = track.GetAabb();
        // Bounds validate authored poses; they do not introduce invisible walls.
        return new ArenaConfiguration(VehicleBody.ToCore(bounds.Position - Vector3.Up), VehicleBody.ToCore(bounds.End + Vector3.Up), players, Array.Empty<ArenaSpawn>(), new[] { SurfaceType.Concrete });
    }
}
