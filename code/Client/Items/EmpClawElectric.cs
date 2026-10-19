using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Fixed-budget electric ribbons, grip sparks and balloon fragments. No collision,
/// propagation, damage or persistent world objects. Freed with its owning claw.</summary>
internal sealed partial class EmpClawElectric : Node3D
{
    private const int SegmentCount = 120;
    private readonly MultiMesh _segments = new() { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, InstanceCount = SegmentCount };
    private readonly MultiMesh _halos = new() { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, InstanceCount = SegmentCount };
    private readonly MultiMesh _flecks = new() { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, InstanceCount = 32 };
    private readonly StandardMaterial3D _arcMaterial = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = new(.72f, .88f, 1), EmissionEnabled = true, Emission = new(.30f, .55f, 1), EmissionEnergyMultiplier = 3 };
    private readonly StandardMaterial3D _haloMaterial = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        AlbedoColor = new(.06f, .25f, 1, .30f) };
    private readonly StandardMaterial3D _fleckMaterial = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new(1, .7f, .16f) };
    private readonly BoxMesh _arcMesh = new() { Size = Vector3.One };
    private readonly BoxMesh _haloMesh = new() { Size = Vector3.One };
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
        _haloMesh.Material = _haloMaterial;
        _fleckMesh.Material = _fleckMaterial;
        _segments.Mesh = _arcMesh;
        _halos.Mesh = _haloMesh;
        _flecks.Mesh = _fleckMesh;
        AddChild(new MultiMeshInstance3D { Multimesh = _segments, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        AddChild(new MultiMeshInstance3D { Multimesh = _halos, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        AddChild(new MultiMeshInstance3D { Multimesh = _flecks, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        _segments.VisibleInstanceCount = _halos.VisibleInstanceCount = _flecks.VisibleInstanceCount = 0;
    }

    internal void Advance(float delta)
    {
        _clock += delta;
        bool flight = Mode == EmpClawVisualState.Fired;
        bool active = Mode == EmpClawVisualState.Activated;
        bool fizzle = Mode == EmpClawVisualState.Expired && StateAge < .3f;
        int count = flight || active || fizzle ? SegmentCount : 0;
        _segments.VisibleInstanceCount = _halos.VisibleInstanceCount = count;
        int sample = (int)(_clock * 30);
        for (int bolt = 0; bolt < count / 20; bolt++)
        {
            float angle = bolt * MathF.Tau / 6;
            Vector3 last = Point(bolt, 0, sample, angle, flight);
            for (int step = 1; step <= 16; step++)
            {
                Vector3 next = Point(bolt, step, sample, angle, flight);
                Vector3 direction = next - last;
                var basis = Basis.LookingAt(direction.Normalized(), Math.Abs(direction.Normalized().Dot(Vector3.Up)) > .98f ? Vector3.Right : Vector3.Up);
                float width = (flight ? .0030f : .0022f) * (1.2f - step / 24f);
                var segmentBasis = new Basis(basis.X * width, basis.Y * width, basis.Z * direction.Length());
                int index = bolt * 20 + step - 1;
                _segments.SetInstanceTransform(index, new Transform3D(segmentBasis, (last + next) * .5f));
                _halos.SetInstanceTransform(index, new Transform3D(new Basis(basis.X * width * 4, basis.Y * width * 4, basis.Z * direction.Length()), (last + next) * .5f));
                last = next;
            }
            // Short forks grow from the contact discharge, rather than six uniform spokes.
            last = Point(bolt, 8, sample, angle, flight);
            for (int branch = 0; branch < 4; branch++)
            {
                Vector3 next = last + new Vector3(MathF.Cos(angle + .6f) * .025f, MathF.Sin(angle + .6f) * .025f,
                    flight ? .09f : .003f) + new Vector3(MathF.Sin(sample + branch * 8.1f), MathF.Cos(bolt + branch * 11.1f), 0) * .009f;
                Vector3 direction = next - last;
                var basis = Basis.LookingAt(direction.Normalized(), Math.Abs(direction.Normalized().Y) > .98f ? Vector3.Right : Vector3.Up);
                int index = bolt * 20 + 16 + branch;
                _segments.SetInstanceTransform(index, new Transform3D(new Basis(basis.X * .001f, basis.Y * .001f, basis.Z * direction.Length()), (last + next) * .5f));
                _halos.SetInstanceTransform(index, new Transform3D(new Basis(basis.X * .005f, basis.Y * .005f, basis.Z * direction.Length()), (last + next) * .5f));
                last = next;
            }
        }
        bool pop = Mode == EmpClawVisualState.NoseDestroyed && StateAge < .4f;
        bool spark = Burst && StateAge < .35f;
        _flecks.VisibleInstanceCount = pop || spark ? 32 : 0;
        _fleckMaterial.AlbedoColor = pop ? new(.85f, .025f, .035f) : new(1, .72f, .2f);
        for (int i = 0; i < _flecks.VisibleInstanceCount; i++)
        {
            float a = i * 2.39996f;
            float speed = pop ? 1.3f : 1.8f + i % 5 * .4f;
            Vector3 velocity = new(MathF.Cos(a) * speed, MathF.Sin(a) * speed, -.5f - i % 3 * .25f);
            float size = pop ? .020f : .004f;
            var basis = new Basis(new Quaternion(Vector3.Up, velocity.Normalized()));
            _flecks.SetInstanceTransform(i, new Transform3D(basis.Scaled(new(size, size * (pop ? 1 : 8), .004f)),
                new Vector3(0, 0, pop ? -.16f : .04f) + velocity * StateAge + Vector3.Down * StateAge * StateAge * 3));
        }
    }

    private static Vector3 Point(int bolt, int step, int sample, float angle, bool flight)
    {
        float t = step / 16f;
        float jitter = step is 0 or 16 ? 0 : MathF.Sin(sample * 8.73f + step * 17.3f + bolt * 31.4f) * .018f;
        float radius = flight ? .08f + t * .10f + jitter : .14f + t * (.19f + bolt % 3 * .04f) + jitter;
        return new(MathF.Cos(angle) * radius - MathF.Sin(angle) * jitter,
            MathF.Sin(angle) * radius + MathF.Cos(angle) * jitter,
            flight ? .07f + t * 2.8f : .025f - MathF.Sin(t * MathF.PI) * .025f);
    }

    public override void _ExitTree()
    {
        foreach (var instance in GetChildren().OfType<MultiMeshInstance3D>()) { instance.Multimesh = null; }
        _segments.Mesh = _halos.Mesh = _flecks.Mesh = null;
        _arcMesh.Material = _haloMesh.Material = _fleckMesh.Material = null;
        _segments.Dispose(); _halos.Dispose(); _flecks.Dispose(); _arcMesh.Dispose(); _haloMesh.Dispose(); _fleckMesh.Dispose();
        _arcMaterial.Dispose(); _haloMaterial.Dispose(); _fleckMaterial.Dispose();
    }
}
