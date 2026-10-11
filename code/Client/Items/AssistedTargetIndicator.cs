using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Items;

/// <summary>Arena-local, depth-tested feedback for the already selected rival. Has no targeting authority.</summary>
internal sealed partial class AssistedTargetIndicator : Node3D
{
    private readonly ImmediateMesh _ringMesh = new();
    private readonly ImmediateMesh _chevronMesh = new();
    private readonly ImmediateMesh _ringCoreMesh = new();
    private readonly MeshInstance3D _ring = new();
    private readonly MeshInstance3D _chevron = new();
    private readonly MeshInstance3D _ringCore = new();
    private readonly StandardMaterial3D _coreMaterial = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
    private readonly Shader _shader = new() { Code = """
        shader_type spatial;
        render_mode unshaded, cull_disabled, depth_draw_never, fog_disabled;
        uniform vec4 tint : source_color = vec4(0.93, 0.96, 1.0, 1.0);
        void fragment() {
            float edge = abs(UV.y);
            float core = 1.0 - smoothstep(0.18, 0.30, edge);
            float rim = 1.0 - smoothstep(0.31, 0.40, edge);
            float glow = exp(-4.5 * edge * edge) * 0.38;
            ALBEDO = mix(tint.rgb, vec3(0.035), rim);
            ALBEDO = mix(ALBEDO, mix(tint.rgb, vec3(1.0), 0.45), core);
            ALPHA = max(glow, max(rim * 0.65, core));
        }
        """ };
    private ShaderMaterial _material = null!;
    internal ulong Target { get; private set; }
    internal bool Engaged { get; private set; }
    internal bool RingVisible => _ring.Visible;
    internal bool ChevronVisible => _chevron.Visible;
    internal Vector3 ChevronTip { get; private set; }
    internal IReadOnlyList<Vector3> SurfacePoints { get; private set; } = [];

    public override void _Ready()
    {
        TopLevel = true;
        GlobalTransform = Transform3D.Identity;
        _material = new ShaderMaterial { Shader = _shader };
        _ring.Mesh = _ringMesh; _chevron.Mesh = _chevronMesh;
        foreach (var visual in new[] { _ring, _chevron })
        {
            visual.MaterialOverride = _material;
            visual.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            AddChild(visual);
        }
        _ringCore.Mesh = _ringCoreMesh; _ringCore.MaterialOverride = _coreMaterial;
        _ringCore.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off; AddChild(_ringCore);
        Clear();
    }

    internal void Clear()
    {
        Target = 0; Engaged = false;
        _ring.Hide(); _ringCore.Hide(); _chevron.Hide(); SurfacePoints = [];
    }

    internal void Present(ulong target, Transform3D pose, Camera3D camera, bool ready, bool grounded)
    {
        Target = target; Engaged = ready;
        _material.SetShaderParameter("tint", ready ? new Color(1, .22f, .075f) : new Color(.93f, .96f, 1));
        _coreMaterial.AlbedoColor = ready ? new Color(1, .58f, .35f) : new Color(.96f, .98f, 1);
        DrawChevron(pose.Origin, camera);
        if (grounded) { DrawRing(pose, camera); }
        else { _ring.Hide(); _ringCore.Hide(); SurfacePoints = []; }
    }

    private void DrawChevron(Vector3 center, Camera3D camera)
    {
        // World-up anchoring and camera-facing geometry never inherit chassis roll/pitch.
        Vector3 roof = center + Vector3.Up * 1.6f;
        float depth = -camera.ToLocal(roof).Z;
        _chevronMesh.ClearSurfaces();
        if (depth <= camera.Near) { _chevron.Hide(); return; }
        float scale = Math.Max(.7f, camera.GetViewport().GetVisibleRect().Size.Y / 1080);
        Vector2 tip = camera.UnprojectPosition(roof) - new Vector2(0, 8 * scale);
        ChevronTip = camera.ProjectPosition(tip, depth);
        _chevronMesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        Vector2 left = tip + new Vector2(-19, -24) * scale;
        Vector2 right = tip + new Vector2(19, -24) * scale;
        // Match the gameplay reference: downward V plus a short detached crown bar.
        Line(left, tip); Line(tip, right);
        Line(left + new Vector2(7, 1) * scale, right + new Vector2(-7, 1) * scale);
        _chevronMesh.SurfaceEnd(); _chevron.Show();

        void Line(Vector2 a, Vector2 b)
        {
            Vector2 side = (b - a).Normalized().Orthogonal() * (6 * scale);
            Quad(_chevronMesh, camera.ProjectPosition(a - side, depth), camera.ProjectPosition(a + side, depth),
                camera.ProjectPosition(b - side, depth), camera.ProjectPosition(b + side, depth));
        }
    }

    private void DrawRing(Transform3D pose, Camera3D camera)
    {
        _ringMesh.ClearSurfaces(); _ringCoreMesh.ClearSurfaces(); _ring.Hide(); _ringCore.Hide(); SurfacePoints = [];
        // Nearby support only: the ring vanishes on takeoff, not on the road far below.
        var support = Sample(pose.Origin, VehicleDimensions.RideHeight + .3f);
        if (support is not { } ground || pose.Origin.Y - ground.Position.Y > VehicleDimensions.RideHeight + .25f) { return; }
        Vector3 forward = -pose.Basis.Z; forward.Y = 0;
        if (forward.LengthSquared() < .01f) { forward = Vector3.Forward; }
        forward = forward.Normalized(); Vector3 right = forward.Cross(Vector3.Up);
        float depth = Math.Max(1, -camera.ToLocal(pose.Origin).Z);
        float metrePerPixel = camera.ProjectPosition(Vector2.Right, depth).DistanceTo(camera.ProjectPosition(Vector2.Zero, depth));
        float halfWidth = Mathf.Clamp(metrePerPixel * 5, .15f, .7f);
        const int count = 64;
        var inner = new Vector3?[count]; var outer = new Vector3?[count];
        var points = new List<Vector3>();
        for (int i = 0; i < count; i++)
        {
            float angle = Mathf.Tau * i / count;
            Vector3 radial = right * MathF.Cos(angle) + forward * MathF.Sin(angle);
            Vector3 offset = right * (MathF.Cos(angle) * 1.85f) + forward * (MathF.Sin(angle) * 3.05f);
            inner[i] = Footprint(offset - radial * halfWidth);
            outer[i] = Footprint(offset + radial * halfWidth);
        }
        bool started = false;
        for (int i = 0; i < count; i++)
        {
            // Four short gaps, leaving long side arcs and shorter nose/tail arcs.
            if (i % 16 is >= 6 and <= 9) { continue; }
            int next = (i + 1) % count;
            if (inner[i] is not { } a || outer[i] is not { } b || inner[next] is not { } c || outer[next] is not { } d ||
                Math.Abs(a.Y - c.Y) > .5f || Math.Abs(b.Y - d.Y) > .5f) { continue; }
            if (!started) { _ringMesh.SurfaceBegin(Mesh.PrimitiveType.Triangles); _ringCoreMesh.SurfaceBegin(Mesh.PrimitiveType.Lines); started = true; }
            Quad(_ringMesh, a, b, c, d); points.AddRange([a, b, c, d]);
            // A native one-pixel core remains legible when grazing perspective makes
            // the ground ribbon subpixel at distance. It keeps the same surface/depth.
            _ringCoreMesh.SurfaceAddVertex((a + b) * .5f + Vector3.Up * .001f);
            _ringCoreMesh.SurfaceAddVertex((c + d) * .5f + Vector3.Up * .001f);
        }
        if (started) { _ringMesh.SurfaceEnd(); _ringCoreMesh.SurfaceEnd(); _ring.Show(); _ringCore.Show(); }
        SurfacePoints = points;

        Vector3? Footprint(Vector3 offset)
        {
            Vector3 plane = ground.Position + offset;
            plane.Y -= ground.Normal.Dot(offset) / ground.Normal.Y;
            return Sample(plane + Vector3.Up * .45f, .9f) is { } hit && Math.Abs(hit.Position.Y - plane.Y) < .4f
                ? hit.Position + hit.Normal * .018f : null;
        }
    }

    private (Vector3 Position, Vector3 Normal)? Sample(Vector3 origin, float down)
    {
        using var query = PhysicsRayQueryParameters3D.Create(origin, origin + Vector3.Down * down, 1);
        using var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 || hit["normal"].AsVector3().Y < .35f) { return null; }
        return (hit["position"].AsVector3(), hit["normal"].AsVector3());
    }

    private static void Quad(ImmediateMesh mesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        Vertex(a, -1); Vertex(b, 1); Vertex(c, -1);
        Vertex(b, 1); Vertex(d, 1); Vertex(c, -1);
        void Vertex(Vector3 p, float across) { mesh.SurfaceSetUV(new(0, across)); mesh.SurfaceAddVertex(p); }
    }

    public override void _ExitTree()
    {
        _ring.Mesh = null; _chevron.Mesh = null;
        _ringCore.Mesh = null; _ringCore.MaterialOverride = null;
        _ring.MaterialOverride = null; _chevron.MaterialOverride = null;
        _ringMesh.Dispose(); _chevronMesh.Dispose(); _ringCoreMesh.Dispose(); _coreMaterial.Dispose(); _material.Dispose(); _shader.Dispose();
    }
}
