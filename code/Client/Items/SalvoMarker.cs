using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Items;

/// <summary>One arena-local aiming guide. Never creates markers by iterating remote inventory.</summary>
internal sealed partial class SalvoMarker : MeshInstance3D
{
    private double _remaining;
    internal int RingCount { get; private set; }
    internal IReadOnlyList<Vector3> SurfaceVertices { get; private set; } = [];

    public override void _Ready()
    {
        Name = "LocalSalvoMarker";
        CastShadow = ShadowCastingSetting.Off;
        MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1, 0.85f, 0.12f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        Visible = false;
    }

    internal void Refresh(double delta, VehicleSnapshot? local, ItemSlot? inventory, ItemPublication? state,
        ItemConfiguration tuning, bool active, Func<Vector3, (Vector3 Position, Vector3 Normal)?> ground)
    {
        var targets = new List<Vector3>();
        if (active && local is { CanInteract: true })
        {
            if ((inventory?.Life == local.LifeId && inventory.Active.Item == HeldItem.Salvo) ||
                state?.Missiles.Any(m => m.Owner == local.VehicleId && m.Arc?.Life == local.LifeId) == true)
            {
                targets.Add(VehicleBody.ToGodot(SalvoFlight.Aim(local.Movement.Physics, tuning.SalvoRange)));
            }
        }
        if (targets.Count == 0) { Visible = false; RingCount = 0; SurfaceVertices = []; _remaining = 0; return; }
        _remaining -= delta;
        if (_remaining > 0) { return; }
        _remaining = 0.05;
        var mesh = new ImmediateMesh();
        var vertices = new List<Vector3>();
        RingCount = 0;
        foreach (var target in targets.Distinct())
        {
            const int segments = 64;
            var inner = new Vector3?[segments];
            var outer = new Vector3?[segments];
            float radius = tuning.SalvoBlastRadius * tuning.SalvoMarkerScale;
            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.Tau * i / segments;
                var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                inner[i] = Sample(target + direction * Math.Max(0.1f, radius - tuning.SalvoMarkerWidth / 2));
                outer[i] = Sample(target + direction * (radius + tuning.SalvoMarkerWidth / 2));
            }
            bool drawn = false;
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                if (inner[i] is not { } a || outer[i] is not { } b || inner[next] is not { } c || outer[next] is not { } d ||
                    Math.Abs(a.Y - c.Y) > 3 || Math.Abs(b.Y - d.Y) > 3) { continue; }
                vertices.AddRange(new[] { a, b, c, b, d, c });
                drawn = true;
            }
            if (drawn) { RingCount++; }
        }
        // A missing terrain footprint is hidden rather than drawn as a floating flat disk.
        if (vertices.Count > 0)
        {
            mesh.SurfaceBegin(Godot.Mesh.PrimitiveType.Triangles);
            foreach (var vertex in vertices) { mesh.SurfaceAddVertex(vertex); }
            mesh.SurfaceEnd();
            var previous = Mesh;
            Mesh = mesh;
            previous?.Dispose();
        }
        else { mesh.Dispose(); }
        Visible = vertices.Count > 0;
        SurfaceVertices = vertices;

        Vector3? Sample(Vector3 point) => ground(point) is { } hit ? hit.Position + hit.Normal * tuning.SalvoMarkerLift : null;
    }
}
