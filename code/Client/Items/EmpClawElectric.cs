using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Fixed-budget electric ribbons, grip sparks and balloon fragments. No collision,
/// propagation, damage or persistent world objects. Freed with its owning claw.</summary>
internal sealed partial class EmpClawElectric : Node3D
{
    private readonly MultiMesh _segments = new() { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, InstanceCount = 72 };
    private readonly MultiMesh _flecks = new() { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, InstanceCount = 16 };
    private readonly StandardMaterial3D _arcMaterial = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = new(.16f, .55f, 1), EmissionEnabled = true, Emission = new(.05f, .3f, 1), EmissionEnergyMultiplier = 4 };
    private readonly StandardMaterial3D _fleckMaterial = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new(1, .7f, .16f) };
    private readonly BoxMesh _arcMesh = new() { Size = Vector3.One };
    private readonly BoxMesh _fleckMesh = new() { Size = Vector3.One };
    private float _clock;
    internal EmpClawVisualState Mode { get; set; }
    internal float StateAge { get; set; }
    internal bool Burst { get; set; }

    internal EmpClawElectric()
    {
        Name = "ElectricalPresentation";
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _arcMesh.Material = _arcMaterial;
        _fleckMesh.Material = _fleckMaterial;
        _segments.Mesh = _arcMesh;
        _flecks.Mesh = _fleckMesh;
        AddChild(new MultiMeshInstance3D { Multimesh = _segments, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        AddChild(new MultiMeshInstance3D { Multimesh = _flecks, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        _segments.VisibleInstanceCount = _flecks.VisibleInstanceCount = 0;
    }

    internal void Advance(float delta)
    {
        _clock += delta;
        bool flight = Mode == EmpClawVisualState.Fired;
        bool active = Mode == EmpClawVisualState.Activated;
        bool fizzle = Mode == EmpClawVisualState.Expired && StateAge < .3f;
        int count = flight || active || fizzle ? 72 : 0;
        _segments.VisibleInstanceCount = count;
        int sample = (int)(_clock * 24);
        for (int bolt = 0; bolt < count / 12; bolt++)
        {
            float angle = bolt * MathF.Tau / 6;
            Vector3 last = Point(bolt, 0, sample, angle, flight);
            for (int step = 1; step <= 12; step++)
            {
                Vector3 next = Point(bolt, step, sample, angle, flight);
                Vector3 direction = next - last;
                var basis = Basis.LookingAt(direction.Normalized(), Math.Abs(direction.Normalized().Dot(Vector3.Up)) > .98f ? Vector3.Right : Vector3.Up);
                float width = flight ? .009f : .006f;
                var segmentBasis = new Basis(basis.X * width, basis.Y * width, basis.Z * direction.Length());
                _segments.SetInstanceTransform(bolt * 12 + step - 1, new Transform3D(segmentBasis, (last + next) * .5f));
                last = next;
            }
        }
        bool pop = Mode == EmpClawVisualState.NoseDestroyed && StateAge < .4f;
        bool spark = Burst && StateAge < .23f;
        _flecks.VisibleInstanceCount = pop || spark ? 16 : 0;
        _fleckMaterial.AlbedoColor = pop ? new(.85f, .025f, .035f) : new(1, .72f, .2f);
        for (int i = 0; i < _flecks.VisibleInstanceCount; i++)
        {
            float a = i * 2.39996f;
            Vector3 velocity = new(MathF.Cos(a) * 1.3f, MathF.Sin(a) * 1.3f, -.5f - i % 3 * .25f);
            float size = pop ? .026f : .012f;
            _flecks.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new(size, size * (pop ? 1 : 3), .006f)),
                new Vector3(0, 0, pop ? -.16f : .04f) + velocity * StateAge));
        }
    }

    private static Vector3 Point(int bolt, int step, int sample, float angle, bool flight)
    {
        float t = step / 12f;
        float jitter = step is 0 or 12 ? 0 : MathF.Sin(sample * 8.73f + step * 17.3f + bolt * 31.4f) * .055f;
        float radius = flight ? .08f + t * .12f + jitter : .15f + t * .34f + jitter;
        return new(MathF.Cos(angle) * radius - MathF.Sin(angle) * jitter,
            MathF.Sin(angle) * radius + MathF.Cos(angle) * jitter,
            flight ? .07f + t * 2.8f : .025f - MathF.Sin(t * MathF.PI) * .025f);
    }

    public override void _ExitTree()
    {
        foreach (var instance in GetChildren().OfType<MultiMeshInstance3D>()) { instance.Multimesh = null; }
        _segments.Mesh = _flecks.Mesh = null;
        _arcMesh.Material = _fleckMesh.Material = null;
        _segments.Dispose(); _flecks.Dispose(); _arcMesh.Dispose(); _fleckMesh.Dispose();
        _arcMaterial.Dispose(); _fleckMaterial.Dispose();
    }
}
