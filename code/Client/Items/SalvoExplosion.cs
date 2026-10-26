using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Three token-seeded cosmetic bursts of one confirmed explosion. Never creates gameplay events.</summary>
internal sealed partial class SalvoExplosion : Node3D
{
    internal const int SparksPerBurst = 48;
    internal const int SmokeCount = 20;
    internal SalvoExplosionSettings Settings { get; private set; } = new();
    private static readonly Color[] Palette = [new(1, .04f, .18f), new(.08f, .9f, 1), new(1, .64f, .04f), new(.65f, .12f, 1), new(.12f, 1, .32f)];
    private readonly List<(Vector3 Direction, float Speed, float Life, float Size, Color Color)> _sparks = [];
    private readonly List<MeshInstance3D> _flashes = [];
    private MultiMeshInstance3D _sparkView = null!;
    private ShaderMaterial _smokeMaterial = null!;
    private readonly float[] _buffer = new float[SparksPerBurst * 3 * 16];
    private float _age;
    internal ulong Seed { get; init; }
    internal float Age => _age;
    internal int BurstsStarted => Math.Clamp((int)(_age / Settings.BurstSpacing) + 1, 0, 3);

    public override void _Ready()
    {
        Name = "SalvoExplosion";
        Settings = SalvoExplosionSettings.Current;
        uint random = (uint)(Seed ^ (Seed >> 32)) | 1u;
        var material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            VertexColorUseAsAlbedo = true };
        _sparkView = new MultiMeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
                Mesh = new SphereMesh { Radius = .5f, Height = 1, RadialSegments = 6, Rings = 2, Material = material }, InstanceCount = SparksPerBurst * 3 } };
        AddChild(_sparkView);
        for (int burst = 0; burst < 3; burst++)
        {
            Color color = Palette[(burst + (int)(Seed % 5)) % Palette.Length];
            var flash = new MeshInstance3D { Mesh = new QuadMesh { Size = new(2, 2) },
                MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                    BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, BillboardKeepScale = true,
                    AlbedoTexture = Networking.MatchResourceLoader.LoadResource<Texture2D>("res://assets/items/kenney/particles/spark_01.png"),
                    AlbedoColor = new Color(color.R * Settings.Brightness, color.G * Settings.Brightness, color.B * Settings.Brightness, 1) },
                Position = Vector3.Up * .8f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(flash); _flashes.Add(flash);
            for (int i = 0; i < SparksPerBurst; i++)
            {
                float azimuth = Next() * Mathf.Tau, y = -.15f + Next() * 1.15f;
                float horizontal = MathF.Sqrt(1 - y * y);
                _sparks.Add((new(MathF.Cos(azimuth) * horizontal, y, MathF.Sin(azimuth) * horizontal),
                    3 + Next() * 3, .5f + Next() * .3f, .026f + Next() * .025f,
                    i % 4 == 0 ? new Color(1, .95f, .65f) : Palette[(burst + i % 2 + (int)(Seed % 5)) % Palette.Length]));
            }
        }
        _smokeMaterial = new ShaderMaterial { Shader = Networking.MatchResourceLoader.LoadResource<Shader>("res://assets/items/salvo/SalvoExplosionSmoke.gdshader") };
        _smokeMaterial.SetShaderParameter("smoke_texture", Networking.MatchResourceLoader.LoadResource<Texture2D>("res://assets/items/kenney/particles/smoke_01.png"));
        _smokeMaterial.SetShaderParameter("visual_scale", Settings.SmokeScale);
        _smokeMaterial.SetShaderParameter("lifetime", Settings.SmokeLifetime);
        _smokeMaterial.SetShaderParameter("opacity", Settings.SmokeOpacity);
        _smokeMaterial.SetShaderParameter("burst_spacing", Settings.BurstSpacing);
        float extent = Settings.SmokeScale * 2.5f + 2;
        var smoke = new MultiMeshInstance3D { Name = "ExplosionSmoke", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new(Vector3.One * -extent, Vector3.One * extent * 2),
            Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true,
                Mesh = new QuadMesh { Size = Vector2.One, Material = _smokeMaterial }, InstanceCount = SmokeCount } };
        AddChild(smoke);
        for (int i = 0; i < SmokeCount; i++)
        {
            float angle = Next() * Mathf.Tau, distance = Next() * .65f * Settings.SmokeScale;
            smoke.Multimesh.SetInstanceTransform(i, new(Basis.Identity, new Vector3(MathF.Cos(angle) * distance,
                (.15f + Next() * .5f) * Settings.SmokeScale, MathF.Sin(angle) * distance)));
            smoke.Multimesh.SetInstanceCustomData(i, new(Next(), Next(), Next(), 1));
        }
        Advance(0);
        float Next() { random ^= random << 13; random ^= random >> 17; random ^= random << 5; return (random & 0xffffff) / 16777216f; }
    }

    public override void _Process(double delta) => Advance((float)delta);

    internal void Advance(float seconds)
    {
        _age += Math.Max(0, seconds);
        if (_age >= Settings.SmokeLifetime) { QueueFree(); return; }
        _smokeMaterial.SetShaderParameter("age", _age);
        for (int burst = 0; burst < 3; burst++)
        {
            float age = _age - burst * Settings.BurstSpacing;
            float duration = Math.Min(.12f, Settings.BurstSpacing * .75f);
            _flashes[burst].Visible = age >= 0 && age < duration;
            _flashes[burst].Scale = Vector3.One * Math.Max(.001f, Settings.FireworkScale * .34f * MathF.Sin(Math.Clamp(age / duration, 0, 1) * MathF.PI));
        }
        _sparkView.Visible = _age < Settings.BurstSpacing * 2 + .83f;
        if (!_sparkView.Visible) { return; }
        for (int i = 0; i < _sparks.Count; i++)
        {
            var spark = _sparks[i];
            float age = _age - (i / SparksPerBurst) * Settings.BurstSpacing;
            float time = Math.Clamp(age, 0, spark.Life), progress = time / spark.Life;
            Vector3 head = PositionAt(time), tail = PositionAt(Math.Max(0, time - .035f));
            Vector3 tangent = head - tail;
            var rotation = new Basis(new Quaternion(Vector3.Up, tangent.LengthSquared() > .000001f ? tangent.Normalized() : spark.Direction));
            float width = Settings.FireworkScale * spark.Size * (1 - progress * .7f);
            var basis = new Basis(rotation.X * width, rotation.Y * Math.Max(width, tangent.Length()), rotation.Z * width);
            Vector3 position = (head + tail) * .5f;
            Color color = spark.Color * Settings.Brightness * (1 - progress / 2.2f);
            color.A = age < 0 ? 0 : 1 - Mathf.SmoothStep(.35f, 1, progress);
            int o = i * 16;
            _buffer[o] = basis.X.X; _buffer[o+1] = basis.Y.X; _buffer[o+2] = basis.Z.X; _buffer[o+3] = position.X;
            _buffer[o+4] = basis.X.Y; _buffer[o+5] = basis.Y.Y; _buffer[o+6] = basis.Z.Y; _buffer[o+7] = position.Y;
            _buffer[o+8] = basis.X.Z; _buffer[o+9] = basis.Y.Z; _buffer[o+10] = basis.Z.Z; _buffer[o+11] = position.Z;
            _buffer[o+12] = color.R; _buffer[o+13] = color.G; _buffer[o+14] = color.B; _buffer[o+15] = color.A;
            Vector3 PositionAt(float t) => Vector3.Up * .8f + Settings.FireworkScale *
                (spark.Direction * (spark.Speed * (1 - MathF.Exp(-3 * t)) / 3) + Vector3.Down * (t * t * .9f));
        }
        RenderingServer.MultimeshSetBuffer(_sparkView.Multimesh.GetRid(), _buffer);
    }
}
