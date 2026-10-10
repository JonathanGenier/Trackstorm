using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Items;

/// <summary>Confirmed central fire and deterministic cosmetic fireworks. No physics bodies, lights or secondary hits.</summary>
internal sealed partial class MissileExplosion : Node3D
{
    internal const string ShaderPath = "res://assets/items/missile/MissileExplosion.gdshader";
    private readonly List<MeshInstance3D> _fire = [];
    private readonly List<(Vector3 Direction, float Reach, float Size, Color Color)> _fragments = [];
    private ShaderMaterial _fireMaterial = null!;
    private MultiMeshInstance3D _sparks = null!;
    private MissileExplosionSettings _settings = null!;
    private float _age;

    internal float BlastRadius { get; init; } = VehicleDimensions.Length;
    internal ulong Seed { get; init; }
    internal float CosmeticReach => _settings.ReachCarLengths * VehicleDimensions.Length;
    internal int FragmentCount => _fragments.Count;

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
        int count = (int)(160 * _settings.Density);
        for (int i = 0; i < count; i++)
        {
            float angle = Next() * Mathf.Tau;
            float elevation = .08f + Next() * .65f;
            var direction = new Vector3(MathF.Cos(angle), elevation, MathF.Sin(angle)).Normalized();
            Color color = i % 3 == 0 ? new(1, .045f, .015f) : i % 3 == 1 ? new(1, .62f, .07f) : new(1, .96f, .83f);
            _fragments.Add((direction, CosmeticReach * (.5f + Next() * .5f), .065f + Next() * .075f, color));
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
        var smoke = ItemPresentation.Particles("smoke_01", true, .75f);
        smoke.Amount = 18;
        var smokeProcess = (ParticleProcessMaterial)smoke.ProcessMaterial;
        smokeProcess.InitialVelocityMin = 1;
        smokeProcess.InitialVelocityMax = 3;
        smokeProcess.Spread = 55;
        smokeProcess.ScaleMin = .8f;
        smokeProcess.ScaleMax = 1.8f;
        smokeProcess.ColorRamp = new GradientTexture1D { Gradient = new Gradient
        {
            Colors = [new Color(1, 1, 1, 0), new Color(1, 1, 1, .6f), new Color(1, 1, 1, 0)],
            Offsets = [0, .2f, 1],
        } };
        ((StandardMaterial3D)smoke.DrawPass1.SurfaceGetMaterial(0)).VertexColorUseAsAlbedo = true;
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
        if (_age >= _settings.Duration) { QueueFree(); return; }
        UpdateVisuals();
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
        float progress = Math.Clamp(_age / (_settings.Duration * .78f), 0, 1);
        float travel = 1 - (1 - progress) * (1 - progress);
        float fade = 1 - Mathf.SmoothStep(.55f, 1, _age / _settings.Duration);
        for (int i = 0; i < _fragments.Count; i++)
        {
            var fragment = _fragments[i];
            Vector3 point = fragment.Direction * fragment.Reach * travel;
            // A downward curl stays within the cosmetic envelope. Fragments never test terrain or hit cars.
            point.Y *= 1 - .8f * progress * progress;
            var basis = new Basis(new Quaternion(Vector3.Up, fragment.Direction));
            basis = basis.Scaled(new Vector3(fragment.Size, fragment.Size * (1 + 5 * (1 - progress)), fragment.Size));
            _sparks.Multimesh.SetInstanceTransform(i, new Transform3D(basis, point));
            var color = fragment.Color * _settings.Intensity;
            color.A = fade;
            _sparks.Multimesh.SetInstanceColor(i, color);
        }
    }

    public override void _ExitTree()
    {
        foreach (var lobe in _fire) { lobe.MaterialOverride = null; }
        _fireMaterial?.Dispose();
    }
}
