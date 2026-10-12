using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>All surface pieces of one logical hazard, with one reconstructable scene-tree owner.</summary>
internal sealed partial class OilHazardVisual : Node3D
{
    private OilPatch? _patch;

    internal void Apply(OilPatch patch)
    {
        if (_patch is not null && _patch.Position == patch.Position && _patch.Normal == patch.Normal &&
            _patch.Radius == patch.Radius && _patch.AdditionalSegments.Equals(patch.AdditionalSegments)) { return; }
        foreach (Node child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        _patch = patch;
        AddSegment(new(patch.Position, patch.Position, patch.Normal, patch.Radius));
        foreach (var segment in patch.AdditionalSegments.Segments) { AddSegment(segment); }
    }

    private void AddSegment(OilSegment segment)
    {
        Vector3 normal = VehicleBody.ToGodot(segment.Normal);
        Vector3 start = VehicleBody.ToGodot(segment.Start) + normal * 0.07f;
        Vector3 end = VehicleBody.ToGodot(segment.End) + normal * 0.07f;
        var material = new StandardMaterial3D { AlbedoColor = new Color(0.035f, 0.022f, 0.055f), Metallic = 0.65f, Roughness = 0.18f };
        void Cap(Vector3 position)
        {
            AddChild(new MeshInstance3D
            {
                Position = position, Quaternion = new Quaternion(Vector3.Up, normal),
                Mesh = new CylinderMesh { TopRadius = segment.Radius, BottomRadius = segment.Radius, Height = 0.012f, RadialSegments = 64 },
                MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            });
        }
        Cap(start);
        if (start.DistanceSquaredTo(end) < 0.000001f) { return; }
        Cap(end);
        AddChild(new MeshInstance3D
        {
            Position = (start + end) / 2, Basis = Basis.LookingAt(end - start, normal),
            Mesh = new BoxMesh { Size = new(segment.Radius * 2, 0.012f, start.DistanceTo(end)) },
            MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });
    }
}
