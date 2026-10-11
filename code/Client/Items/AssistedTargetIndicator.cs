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
    private readonly Shader _chevronShader = new() { Code = """
        shader_type spatial;
        render_mode unshaded, cull_disabled, depth_draw_never, fog_disabled;
        uniform vec4 tint : source_color;
        float wing(vec2 p, vec2 shoulder) {
            vec2 axis = -shoulder;
            float along = clamp(dot(p - shoulder, axis) / dot(axis, axis), 0.0, 1.0);
            return length(p - shoulder - along * axis) - mix(0.032, 0.075, along);
        }
        void fragment() {
            vec2 p = UV * vec2(1.3, 0.72) + vec2(-0.65, -0.54);
            float edge = min(wing(p, vec2(-0.46, -0.36)), wing(p, vec2(0.46, -0.36)));
            float aa = max(fwidth(edge), 0.003);
            float core = 1.0 - smoothstep(-aa, aa, edge);
            float glow = exp(-max(edge, 0.0) * 30.0) * 0.65;
            ALBEDO = mix(tint.rgb, vec3(1.0), core * 0.45);
            ALPHA = max(core, glow);
        }
        """ };
    private ShaderMaterial _chevronMaterial = null!;
    private readonly Shader _shader = new() { Code = """
        shader_type spatial;
        render_mode unshaded, cull_disabled, depth_draw_never, fog_disabled;
        uniform vec4 tint : source_color = vec4(0.93, 0.96, 1.0, 1.0);
        void fragment() {
            float edge = abs(UV.y);
            float core = 1.0 - smoothstep(0.14, 0.24, edge);
            float glow = exp(-3.5 * edge * edge) * (1.0 - smoothstep(0.8, 1.0, edge));
            ALBEDO = mix(tint.rgb, vec3(1.0), core * 0.8);
            ALPHA = max(glow * 0.85, core);
        }
        """ };
    private ShaderMaterial _material = null!;
    internal ulong Target { get; private set; }
    internal bool Engaged { get; private set; }
    internal bool RingVisible => _ring.Visible;
    internal bool ChevronVisible => _chevron.Visible;
    internal Vector3 ChevronTip { get; private set; }
    internal float ChevronWidth { get; private set; }
    internal IReadOnlyList<Vector3> SurfacePoints { get; private set; } = [];

    public override void _Ready()
    {
        TopLevel = true;
        GlobalTransform = Transform3D.Identity;
        _material = new ShaderMaterial { Shader = _shader };
        _chevronMaterial = new ShaderMaterial { Shader = _chevronShader };
        _ring.Mesh = _ringMesh; _chevron.Mesh = _chevronMesh;
        foreach (var visual in new[] { _ring, _chevron })
        {
            visual.MaterialOverride = _material;
            visual.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            AddChild(visual);
        }
        _chevron.MaterialOverride = _chevronMaterial;
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
        _chevronMaterial.SetShaderParameter("tint", ready ? new Color(1, .22f, .075f) : new Color(.93f, .96f, 1));
        _coreMaterial.AlbedoColor = ready ? new Color(1, .65f, .45f) : Colors.White;
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
        float screenScale = camera.GetViewport().GetVisibleRect().Size.Y / 1080;
        float pixelsPerMetre = camera.UnprojectPosition(roof + camera.GlobalBasis.X).DistanceTo(camera.UnprojectPosition(roof));
        // Perspective-sized like a 1.6 m symbol, bounded only for near/far readability.
        ChevronWidth = Mathf.Clamp(pixelsPerMetre * 1.6f, 14 * screenScale, 64 * screenScale);
        float scale = ChevronWidth / 38;
        Vector2 tip = camera.UnprojectPosition(roof) - new Vector2(0, Math.Max(4 * screenScale, 8 * scale));
        ChevronTip = camera.ProjectPosition(tip, depth);
        _chevronMesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        // One tapered downward pointer, with a joined tip and no enclosing crown.
        float unit = ChevronWidth / .92f;
        Corner(new(0, 0)); Corner(new(1, 0)); Corner(new(0, 1));
        Corner(new(1, 0)); Corner(new(1, 1)); Corner(new(0, 1));
        _chevronMesh.SurfaceEnd(); _chevron.Show();

        void Corner(Vector2 uv)
        {
            _chevronMesh.SurfaceSetUV(uv);
            _chevronMesh.SurfaceAddVertex(camera.ProjectPosition(tip + (uv * new Vector2(1.3f, .72f) + new Vector2(-.65f, -.54f)) * unit, depth));
        }
    }

    private void DrawRing(Transform3D pose, Camera3D camera)
    {
        _ringMesh.ClearSurfaces(); _ringCoreMesh.ClearSurfaces(); _ring.Hide(); _ringCore.Hide(); SurfacePoints = [];
        // Clearance is perpendicular to support, not world vertical: on a 35-degree
        // bank normal ride height already exceeds the old vertical cutoff.
        var support = Sample(pose.Origin + Vector3.Up * .3f, 4);
        if (support is not { } ground) { return; }
        float clearance = (pose.Origin - ground.Position).Dot(ground.Normal);
        if (clearance < 0 || clearance > VehicleDimensions.RideHeight + .25f) { return; }
        Vector3 center = pose.Origin - ground.Normal * clearance;
        Vector3 forward = (-pose.Basis.Z).Slide(ground.Normal).Normalized();
        if (forward.LengthSquared() < .01f) { forward = Vector3.Forward.Slide(ground.Normal).Normalized(); }
        Vector3 right = forward.Cross(ground.Normal).Normalized();
        float depth = Math.Max(1, -camera.ToLocal(pose.Origin).Z);
        float metrePerPixel = camera.ProjectPosition(Vector2.Right, depth).DistanceTo(camera.ProjectPosition(Vector2.Zero, depth));
        float halfWidth = Mathf.Clamp(metrePerPixel * 8, .18f, 1f);
        const int count = 64, bands = 4;
        var samples = new (Vector3 Position, Vector3 Normal)?[count, bands + 1];
        for (int i = 0; i < count; i++)
        {
            float angle = Mathf.Tau * i / count;
            Vector3 radial = right * MathF.Cos(angle) + forward * MathF.Sin(angle);
            Vector3 offset = right * (MathF.Cos(angle) * 2.25f) + forward * (MathF.Sin(angle) * 3.35f);
            for (int band = 0; band <= bands; band++)
            {
                Vector3 expected = center + offset + radial * (halfWidth * (band * 2f / bands - 1));
                if (Sample(expected + Vector3.Up * 1.6f, 3.2f) is { } hit && Math.Abs(hit.Position.Y - expected.Y) < 1.5f)
                {
                    // The production infield's render/collision bake has a 4 cm bound.
                    // This small normal offset clears that error without a floating plane.
                    samples[i, band] = (hit.Position + hit.Normal * .045f, hit.Normal);
                }
            }
        }
        var points = new List<Vector3>(); bool started = false;
        for (int i = 0; i < count; i++)
        {
            if (i % 16 is >= 6 and <= 9) { continue; }
            int next = (i + 1) % count;
            for (int band = 0; band < bands; band++)
            {
                if (samples[i, band] is not { } a || samples[i, band + 1] is not { } b ||
                    samples[next, band] is not { } c || samples[next, band + 1] is not { } d ||
                    !Connected(a, b) || !Connected(a, c) || !Connected(b, d) || !Connected(c, d)) { continue; }
                if (!started) { _ringMesh.SurfaceBegin(Mesh.PrimitiveType.Triangles); started = true; }
                Quad(_ringMesh, a.Position, b.Position, c.Position, d.Position, band * 2f / bands - 1, (band + 1) * 2f / bands - 1);
                points.AddRange([a.Position, b.Position, c.Position, d.Position]);
            }
        }
        if (started) { _ringMesh.SurfaceEnd(); _ring.Show(); }
        // The crisp core is sampled independently, never averaged across a crest or
        // billboarded toward the camera. Every visible part follows native support.
        bool core = false;
        for (int i = 0; i < count; i++)
        {
            if (i % 16 is >= 6 and <= 9 || samples[i, bands / 2] is not { } a ||
                samples[(i + 1) % count, bands / 2] is not { } b || !Connected(a, b)) { continue; }
            if (!core) { _ringCoreMesh.SurfaceBegin(Mesh.PrimitiveType.Lines); core = true; }
            _ringCoreMesh.SurfaceAddVertex(a.Position); _ringCoreMesh.SurfaceAddVertex(b.Position);
        }
        if (core) { _ringCoreMesh.SurfaceEnd(); _ringCore.Show(); }
        SurfacePoints = points;

        static bool Connected((Vector3 Position, Vector3 Normal) a, (Vector3 Position, Vector3 Normal) b)
        {
            Vector3 delta = b.Position - a.Position;
            return delta.LengthSquared() < 1 && Math.Abs(delta.Dot(a.Normal)) < .15f && Math.Abs(delta.Dot(b.Normal)) < .15f;
        }
    }
    private (Vector3 Position, Vector3 Normal)? Sample(Vector3 origin, float down)
    {
        using var query = PhysicsRayQueryParameters3D.Create(origin, origin + Vector3.Down * down, 1);
        using var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 || hit["normal"].AsVector3().Y < .35f) { return null; }
        return (hit["position"].AsVector3(), hit["normal"].AsVector3());
    }

    private static void Quad(ImmediateMesh mesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float first = -1, float second = 1)
    {
        Vertex(a, first); Vertex(b, second); Vertex(c, first);
        Vertex(b, second); Vertex(d, second); Vertex(c, first);
        void Vertex(Vector3 p, float across) { mesh.SurfaceSetUV(new(0, across)); mesh.SurfaceAddVertex(p); }
    }

    public override void _ExitTree()
    {
        _ring.Mesh = null; _chevron.Mesh = null;
        _ringCore.Mesh = null; _ringCore.MaterialOverride = null;
        _ring.MaterialOverride = null; _chevron.MaterialOverride = null;
        _ringMesh.Dispose(); _chevronMesh.Dispose(); _ringCoreMesh.Dispose(); _material.Dispose(); _shader.Dispose();
        _coreMaterial.Dispose(); _chevronMaterial.Dispose(); _chevronShader.Dispose();
    }
}
