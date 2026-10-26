using Godot;

namespace Trackstorm.Client.Items;

/// <summary>One token-seeded, shader-animated three-crate smash. No physics, damage or per-piece networking.</summary>
internal sealed partial class CarnivalSmash : Node3D
{
    internal const string AssetPath = "res://assets/items/carnival-crate/CarnivalSmash.glb";
    internal const string ShaderPath = "res://assets/items/carnival-crate/CarnivalSmash.gdshader";
    internal const string SparkShaderPath = "res://assets/items/carnival-crate/CarnivalSmashSpark.gdshader";
    internal const int WoodCount = 72;
    internal const int FoilCount = 96;
    internal const int StarCount = 12;
    internal const int CoinCount = 12;
    internal const int BoltCount = 12;
    internal const int SparkCount = 140;
    internal const float Lifetime = 12;
    private readonly List<ShaderMaterial> _materials = [];
    private float _age;
    private float _retiring = -1;
    private MeshInstance3D _flash = null!;
    private StandardMaterial3D _flashMaterial = null!;
    internal ulong Token { get; init; }
    internal required CarnivalSmashResources Resources { get; init; }
    internal CarnivalSmashSettings Settings { get; init; } = CarnivalSmashSettings.Current;
    internal float InitialAge { get; init; }
    internal float Age => _age;
    internal Vector3 FloorPoint { get; init; }
    internal Vector3 FloorNormal { get; init; } = Vector3.Up;

    public override void _Ready()
    {
        // Cleanup uses elapsed presentation time even when gameplay is paused.
        ProcessMode = ProcessModeEnum.Always;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _age = InitialAge;
        uint random = (uint)(Token ^ (Token >> 32)) | 1u;
        _flashMaterial = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, BillboardKeepScale = true,
            AlbedoTexture = Resources.FlashTexture };
        _flash = new MeshInstance3D { Mesh = Resources.Flash, MaterialOverride = _flashMaterial, Position = Vector3.Up*1.4f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_flash);
        for (int kind = 0; kind < 4; kind++)
        {
            var material = new ShaderMaterial { Shader = kind == 3 ? Resources.SparkShader : Resources.SurfaceShader };
            material.SetShaderParameter("burst_transform", GlobalTransform);
            material.SetShaderParameter("burst_inverse", GlobalTransform.AffineInverse());
            material.SetShaderParameter("effect_scale", Settings.Size);
            if (kind != 3)
            {
                material.SetShaderParameter("kind", kind);
                material.SetShaderParameter("floor_point", FloorPoint);
                material.SetShaderParameter("floor_normal", FloorNormal);
                material.SetShaderParameter("red_wood", Resources.Red);
                material.SetShaderParameter("ivory_wood", Resources.Ivory);
                material.SetShaderParameter("raw_wood", Resources.Raw);
            }
            _materials.Add(material);
            int batches = kind is 0 or 2 ? 3 : 1;
            for (int batch = 0; batch < batches; batch++)
            {
                int count = kind switch { 0 => WoodCount / 3, 1 => FoilCount,
                    2 => batch switch { 0 => StarCount, 1 => CoinCount, _ => BoltCount }, _ => SparkCount };
                Mesh mesh = kind switch { 0 => Resources.Wood[batch], 1 => Resources.Foil, 2 => Resources.GoldShapes[batch], _ => Resources.Spark };
                var instances = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = mesh, InstanceCount = count };
                var view = new MultiMeshInstance3D { Name = $"Smash{kind}_{batch}", Multimesh = instances, MaterialOverride = material,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, CustomAabb = new(new(-4, -12, -4), new(8, 18, 8)) };
                var buffer = new float[count*16];
                for (int i = 0; i < count; i++)
                {
                    int crate = i % 3;
                    Vector3 centre = crate switch { 0 => new(-.77f, .72f, 0), 1 => new(.77f, .72f, 0), _ => new(0, 2.12f, 0) };
                    Vector3 point = centre + new Vector3((Next()-.5f)*.85f, (Next()-.5f)*.85f, (Next()-.5f)*1.1f);
                    float azimuth = Next(), energy = Next(), phase = Next();
                    if (kind == 2) point = new(Mathf.Cos(azimuth*Mathf.Tau)*1.24f, .3f+Next()*2.25f, Mathf.Sin(azimuth*Mathf.Tau)*.52f);
                    float life = kind switch { 0 => 4+Next()*3, 1 => 2.5f+Next()*2, 2 => 2+Next()*2, _ => .6f+Next()*.7f };
                    Vector3 scale = kind switch
                    {
                        0 => Vector3.One*(.5f+Next()*.3f),
                        1 => new(.045f+Next()*.04f, .12f+Next()*.12f, .3f),
                        2 => Vector3.One*(.3f+Next()*.12f),
                        _ => new(.018f, .035f+Next()*.16f, .018f),
                    };
                    var basis = Basis.FromEuler(new(Next()*Mathf.Tau, Next()*Mathf.Tau, Next()*Mathf.Tau)).Scaled(scale);
                    int o = i*16;
                    buffer[o] = basis.X.X; buffer[o+1] = basis.Y.X; buffer[o+2] = basis.Z.X; buffer[o+3] = point.X;
                    buffer[o+4] = basis.X.Y; buffer[o+5] = basis.Y.Y; buffer[o+6] = basis.Z.Y; buffer[o+7] = point.Y;
                    buffer[o+8] = basis.X.Z; buffer[o+9] = basis.Y.Z; buffer[o+10] = basis.Z.Z; buffer[o+11] = point.Z;
                    buffer[o+12] = azimuth; buffer[o+13] = energy; buffer[o+14] = phase; buffer[o+15] = life;
                }
                RenderingServer.MultimeshSetBuffer(instances.GetRid(), buffer);
                AddChild(view);
            }
        }
        Advance(0);
        float Next() { random ^= random << 13; random ^= random >> 17; random ^= random << 5; return (random & 0xffffff) / 16777216f; }
    }

    public override void _Process(double delta) => Advance((float)delta);

    internal void Retire() { if (_retiring < 0) _retiring = 0; }

    internal void Advance(float seconds)
    {
        _age += Math.Max(0, seconds);
        if (_retiring >= 0) _retiring += Math.Max(0, seconds);
        if (_age >= Lifetime || _retiring >= .2f) { QueueFree(); return; }
        _flash.Visible = _age < .24f;
        if (_flash.Visible)
        {
            _flash.Scale = Vector3.One*(.55f+_age*2)*Settings.Size;
            _flashMaterial.AlbedoColor = new(2.4f, 1.5f, .18f, 1-Mathf.SmoothStep(0, .24f, _age));
        }
        foreach (var material in _materials)
        {
            material.SetShaderParameter("age", _age);
            material.SetShaderParameter("retire_opacity", _retiring < 0 ? 1 : 1-_retiring/.2f);
        }
    }
}
