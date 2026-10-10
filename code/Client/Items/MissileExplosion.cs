using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Items;

/// <summary>Confirmed central fire and deterministic cosmetic fireworks. No physics bodies, lights or secondary hits.</summary>
internal sealed partial class MissileExplosion : Node3D
{
    internal const string ShaderPath = "res://assets/items/missile/MissileExplosion.gdshader";
    private readonly List<MeshInstance3D> _fire = [];
    private readonly List<Fragment> _fragments = [];
    private readonly record struct Fragment(Vector3 Direction, float Reach, float Size, Color Color,
        float Lifetime, float Delay, float Drag, float TrailTime, float Phase);
    private ShaderMaterial _fireMaterial = null!;
    private MultiMeshInstance3D _sparks = null!;
    private float[] _sparkBuffer = [];
    private MissileExplosionSettings _settings = null!;
    private float _age;

    internal float BlastRadius { get; init; } = VehicleDimensions.Length;
    internal ulong Seed { get; init; }
    internal float CosmeticReach => _settings.ReachCarLengths * VehicleDimensions.Length;
    internal int FragmentCount => _fragments.Count;
    internal float Lifetime => Math.Max(_settings.Duration, _settings.SmokeDuration) + .1f;

    public override void _Ready()
    {
        _settings = MissileExplosionSettings.Current.Bounded();
        _fireMaterial = new ShaderMaterial { Shader = Networking.MatchResourceLoader.LoadResource<Shader>(ShaderPath) };
        _fireMaterial.SetShaderParameter("intensity", _settings.Intensity);
        var mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 24, Rings = 12 };
        for (int i = 0; i < 7; i++)
        {
            var lobe = new MeshInstance3D { Mesh = mesh, MaterialOverride = _fireMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(lobe); _fire.Add(lobe);
        }
        // All peers use the impact token to reproduce the same sparse burst directions.
        uint random = (uint)(Seed ^ (Seed >> 32)) | 1u;
        int count = (int)(224 * _settings.Density);
        _sparkBuffer = new float[count * 16];
        for (int i = 0; i < count; i++)
        {
            float angle = Next() * Mathf.Tau;
            float elevation = .2f + Next() * 1.1f;
            var direction = new Vector3(MathF.Cos(angle), elevation, MathF.Sin(angle)).Normalized();
            Color color = i % 3 == 0 ? new(1, .045f, .015f) : i % 3 == 1 ? new(1, .62f, .07f) : new(1, .96f, .83f);
            // Sparse festive accents retain the dominant red/gold/white identity.
            if (i % 12 == 0) { color = new(.15f, .8f, 1); }
            else if (i % 12 == 7) { color = new(1, .12f, .55f); }
            _fragments.Add(new(direction, CosmeticReach * (.65f + Next() * .35f), .065f + Next() * .05f, color,
                _settings.Duration * (.55f + Next() * .41f), Next() * .035f,
                1.5f + Next() * 1.5f, .018f + Next() * .022f, Next() * Mathf.Tau));
        }
        _sparks = new MultiMeshInstance3D
        {
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = new SphereMesh { Radius = .5f, Height = 1, RadialSegments = 6, Rings = 2, Material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                    VertexColorUseAsAlbedo = true,
                    NoDepthTest = false,
                } },
                InstanceCount = count,
            },
        };
        AddChild(_sparks);
        var smoke = ItemPresentation.Particles("smoke_01", true, _settings.SmokeDuration);
        smoke.Name = "ImpactSmoke";
        smoke.Amount = 18;
        var smokeProcess = (ParticleProcessMaterial)smoke.ProcessMaterial;
        smokeProcess.InitialVelocityMin = .3f;
        smokeProcess.InitialVelocityMax = 1.1f;
        smokeProcess.Spread = 55;
        smokeProcess.ScaleMin = 2.4f;
        smokeProcess.ScaleMax = 4.5f;
        var smokeGrowth = new Curve();
        smokeGrowth.AddPoint(new(0, .3f)); smokeGrowth.AddPoint(new(.5f, .85f)); smokeGrowth.AddPoint(new(1, 1));
        smokeProcess.ScaleCurve = new CurveTexture { Curve = smokeGrowth };
        smokeProcess.ColorRamp = new GradientTexture1D { Gradient = new Gradient
        {
            Colors = [new Color(1, 1, 1, 0), new Color(1, 1, 1, 1), new Color(1, 1, 1, .45f), new Color(1, 1, 1, 0)],
            Offsets = [0, .12f, .55f, 1],
        } };
        var smokeMaterial = (StandardMaterial3D)smoke.DrawPass1.SurfaceGetMaterial(0);
        smokeMaterial.VertexColorUseAsAlbedo = true;
        smokeMaterial.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
        smokeMaterial.BillboardKeepScale = true;
        smokeMaterial.AlbedoColor = new(.8f, .76f, .78f, _settings.SmokeOpacity);
        AddChild(smoke);
        UpdateVisuals();

        float Next()
        {
            random ^= random << 13; random ^= random >> 17; random ^= random << 5;
            return (random & 0xffffff) / 16777216f;
        }
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_age >= Lifetime) { QueueFree(); return; }
        if (_age < _settings.Duration) { UpdateVisuals(); }
        else
        {
            _sparks.Visible = false;
            foreach (var lobe in _fire) { lobe.Visible = false; }
        }
    }

    private void UpdateVisuals()
    {
        float fireAge = Math.Clamp(_age / .65f, 0, 1);
        _fireMaterial.SetShaderParameter("age", fireAge);
        // Even enlarged visual lobes remain inside the central blast envelope.
        float radius = Math.Min(BlastRadius * .98f, VehicleDimensions.Length * .65f * _settings.FireScale);
        float expansion = Mathf.SmoothStep(0, .16f, _age);
        for (int i = 0; i < _fire.Count; i++)
        {
            var lobe = _fire[i];
            float angle = i * Mathf.Tau / 6;
            Vector3 direction = i == 0 ? Vector3.Zero : new Vector3(MathF.Cos(angle), .3f + (i % 2) * .25f, MathF.Sin(angle)).Normalized();
            lobe.Position = direction * radius * .5f * expansion;
            lobe.Scale = Vector3.One * Math.Max(.001f, radius * (i == 0 ? .6f : .5f) * expansion);
            lobe.Visible = fireAge < 1;
        }
        for (int i = 0; i < _fragments.Count; i++)
        {
            var fragment = _fragments[i];
            float age = _age - fragment.Delay;
            float progress = Math.Clamp(age / fragment.Lifetime, 0, 1);
            Vector3 head = PositionAt(progress);
            Vector3 tail = PositionAt(Math.Max(0, progress - fragment.TrailTime / fragment.Lifetime));
            Vector3 tangent = head - tail;
            float length = Math.Clamp(tangent.Length(), fragment.Size, .8f);
            float width = fragment.Size * (1 - .65f * progress);
            var basis = new Basis(new Quaternion(Vector3.Up, tangent.LengthSquared() > .000001f ? tangent.Normalized() : fragment.Direction));
            // Scale the rotated LOCAL axes: Basis.Scaled stretches world Y into floating vertical sticks.
            basis = new Basis(basis.X * width, basis.Y * length, basis.Z * width);
            Vector3 origin = (head + tail) * .5f;
            float heat = MathF.Exp(-progress * 18);
            var color = fragment.Color.Lerp(new Color(1, .98f, .85f), heat * .65f);
            color *= _settings.Intensity * (1.4f + heat * .8f);
            float burn = .88f + .12f * MathF.Sin(fragment.Phase + progress * 35);
            color.A = age < 0 ? 0 : MathF.Pow(1 - progress, .3f) * (1 - Mathf.SmoothStep(.72f, 1, progress)) * burn;
            // Godot's documented row-major 3D transform + RGBA layout. One upload per effect,
            // rather than two native calls per fragment, keeps the brighter shell's CPU work bounded.
            int offset = i * 16;
            _sparkBuffer[offset] = basis.X.X; _sparkBuffer[offset + 1] = basis.Y.X; _sparkBuffer[offset + 2] = basis.Z.X; _sparkBuffer[offset + 3] = origin.X;
            _sparkBuffer[offset + 4] = basis.X.Y; _sparkBuffer[offset + 5] = basis.Y.Y; _sparkBuffer[offset + 6] = basis.Z.Y; _sparkBuffer[offset + 7] = origin.Y;
            _sparkBuffer[offset + 8] = basis.X.Z; _sparkBuffer[offset + 9] = basis.Y.Z; _sparkBuffer[offset + 10] = basis.Z.Z; _sparkBuffer[offset + 11] = origin.Z;
            _sparkBuffer[offset + 12] = color.R; _sparkBuffer[offset + 13] = color.G; _sparkBuffer[offset + 14] = color.B; _sparkBuffer[offset + 15] = color.A;

            Vector3 PositionAt(float time)
            {
                // Closed-form drag gives a fast burst, continued outward travel and a gravity-like arc.
                // Minimum upward direction is > .19; sag/travel <= .25, keeping the path inside Reach.
                // No collision queries, ground bounces or secondary impact events are involved.
                float travel = (1 - MathF.Exp(-fragment.Drag * time)) / (1 - MathF.Exp(-fragment.Drag));
                return fragment.Direction * fragment.Reach * travel + Vector3.Down * (fragment.Reach * .25f * time * time);
            }
        }
        if (_sparkBuffer.Length > 0) { RenderingServer.MultimeshSetBuffer(_sparks.Multimesh.GetRid(), _sparkBuffer); }
    }

    public override void _ExitTree()
    {
        foreach (var lobe in _fire) { lobe.MaterialOverride = null; }
        _fireMaterial?.Dispose();
    }
}
