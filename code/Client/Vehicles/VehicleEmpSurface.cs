using Godot;

namespace Trackstorm.Client.Vehicles;

/// <summary>EMP-only contact with the authored rigid bodywork. The movement hull deliberately
/// fills space above the hood; it cannot supply a visible attachment point.</summary>
internal sealed class VehicleEmpSurface : IDisposable
{
    private readonly TriangleMesh _surface = new();

    internal VehicleEmpSurface()
    {
        var model = Bootstrap.StartupController.LoadResource<PackedScene>("res://assets/vehicles/WastelandVehicle.tscn").Instantiate<Node3D>();
        try
        {
            var faces = new List<Vector3>();
            void Collect(Node3D node, Transform3D parent)
            {
                Transform3D frame = parent * node.Transform;
                if (node is MeshInstance3D { Mesh: { } mesh }) { faces.AddRange(mesh.GetFaces().Select(point => frame * point)); }
                foreach (var child in node.GetChildren().OfType<Node3D>()) { Collect(child, frame); }
            }
            foreach (var part in model.GetChildren().OfType<Node3D>())
            {
                string name = part.Name;
                // These rigs move independently of the chassis. They are presentation, not
                // authoritative attachment surfaces; a ray may continue to bodywork behind them.
                if (name.StartsWith("Wheel", StringComparison.Ordinal) || name.StartsWith("Suspension", StringComparison.Ordinal) ||
                    name.StartsWith("Shock", StringComparison.Ordinal) || name.StartsWith("TrunkHinge", StringComparison.Ordinal) || name == "WeaponRack") { continue; }
                Collect(part, model.Transform);
            }
            if (!_surface.CreateFromFaces(faces.ToArray())) { throw new InvalidOperationException("Vehicle has no rigid EMP contact surface."); }
        }
        finally { model.Free(); }
    }

    /// <summary>BVH query in chassis space. Construct once per arena, never from interpolated art.</summary>
    internal Godot.Collections.Dictionary Intersect(Vector3 from, Vector3 to) => _surface.IntersectSegment(from, to);

    public void Dispose() => _surface.Dispose();
}
