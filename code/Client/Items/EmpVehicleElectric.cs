using Godot;

namespace Trackstorm.Client.Items;

/// <summary>One fixed-budget electrical envelope per affected vehicle, regardless of claw count.
/// Short branching pulses follow the rendered body; they have no physics or damage.</summary>
internal sealed partial class EmpVehicleElectric : Node3D
{
    private const int Count = 96;
    private readonly MultiMesh _lines = new() { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, InstanceCount = Count };
    private readonly MultiMesh _halos = new() { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, InstanceCount = Count };
    private readonly StandardMaterial3D _material = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = new(.12f, .48f, 1), EmissionEnabled = true, Emission = new(.08f, .32f, 1), EmissionEnergyMultiplier = 2 };
    private readonly StandardMaterial3D _glow = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        AlbedoColor = new(.04f, .22f, 1, .32f) };
    private readonly BoxMesh _mesh = new() { Size = Vector3.One };
    private readonly BoxMesh _haloMesh = new() { Size = Vector3.One };
    private float _time;

    internal EmpVehicleElectric()
    {
        _mesh.Material = _material; _haloMesh.Material = _glow;
        _lines.Mesh = _mesh; _halos.Mesh = _haloMesh;
        AddChild(new MultiMeshInstance3D { Multimesh = _lines, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        AddChild(new MultiMeshInstance3D { Multimesh = _halos, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        int sample = (int)(_time * 24);
        // Eight short, independently wandering arcs avoid tracing a rigid body outline.
        // Each owns eight trunk segments and four fork segments within the fixed budget.
        for (int path = 0; path < 8; path++)
        {
            float pulse = .5f + .5f * MathF.Sin(_time * 19 + path * 2.7f);
            float width = .004f + pulse * .009f;
            Vector3 last = Point(path, 0, sample);
            for (int step = 1; step <= 8; step++)
            {
                Vector3 next = Point(path, step, sample);
                Segment(path * 12 + step - 1, last, next, width);
                last = next;
            }
            last = Point(path, 4, sample);
            Vector3 across = path < 4 ? Vector3.Up : Vector3.Right;
            for (int step = 1; step <= 4; step++)
            {
                Vector3 next = Point(path, 4 + step * .45f, sample) + across * (step * .09f);
                Segment(path * 12 + 7 + step, last, next, width * .65f);
                last = next;
            }
        }
    }

    private void Segment(int index, Vector3 from, Vector3 to, float width)
    {
        Vector3 direction = to - from;
        Basis basis = Basis.LookingAt(direction.Normalized(), Math.Abs(direction.Normalized().Y) > .98f ? Vector3.Right : Vector3.Up);
        _lines.SetInstanceTransform(index, new(new Basis(basis.X * width, basis.Y * width, basis.Z * direction.Length()), (from + to) / 2));
        _halos.SetInstanceTransform(index, new(new Basis(basis.X * width * 4, basis.Y * width * 4, basis.Z * direction.Length()), (from + to) / 2));
    }

    private static Vector3 Point(int path, float step, int sample)
    {
        float seed = MathF.Sin(sample / 7 * 13.7f + path * 7.1f);
        float jitter = MathF.Sin(sample * 12.3f + step * 31.7f + path * 7.1f) * .13f;
        float z = (path % 2 == 0 ? -1.25f : 1.05f) + seed * .3f + (step / 8f - .5f) * 1.45f;
        float cabin = Mathf.SmoothStep(0, 1, (z + 1.4f) / .6f) * (1 - Mathf.SmoothStep(0, 1, (z - .7f) / .55f));
        return path switch
        {
            < 4 => new((path < 2 ? -1 : 1) * (1.36f + Math.Abs(jitter) * .3f), .25f + jitter, z),
            < 6 => new(seed * .65f + jitter, .55f + cabin * .63f, z),
            _ => new(seed * .65f + jitter, -.72f, z)
        };
    }

    public override void _ExitTree()
    {
        foreach (var child in GetChildren().OfType<MultiMeshInstance3D>()) { child.Multimesh = null; }
        _lines.Mesh = _halos.Mesh = null; _mesh.Material = _haloMesh.Material = null;
        _lines.Dispose(); _halos.Dispose(); _mesh.Dispose(); _haloMesh.Dispose(); _material.Dispose(); _glow.Dispose();
    }
}
