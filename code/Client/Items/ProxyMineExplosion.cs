using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Bounded fireball, rising smoke and impulse presentation of a confirmed mine detonation.</summary>
internal sealed partial class ProxyMineExplosion : Node3D
{
    internal const float Duration = 2.7f;

    private readonly StandardMaterial3D _flashMaterial = new()
    {
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = new Color(1, 0.91f, 0.63f),
        EmissionEnabled = true,
        Emission = new Color(1, 0.53f, 0.13f),
        EmissionEnergyMultiplier = 3,
        NoDepthTest = false,
    };
    private readonly StandardMaterial3D _ringMaterial = new()
    {
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = new Color(1, 0.58f, 0.18f, 0.7f),
        EmissionEnabled = true,
        Emission = new Color(1, 0.37f, 0.07f),
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };
    private readonly MeshInstance3D _flash = new()
    {
        Mesh = new SphereMesh { Radius = 0.5f, Height = 1 },
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };
    private readonly MeshInstance3D _ring = new()
    {
        Mesh = new TorusMesh { InnerRadius = 0.94f, OuterRadius = 1, Rings = 80, RingSegments = 8 },
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        Position = new Vector3(0, 0.12f, 0),
    };
    private readonly OmniLight3D _light = new()
    {
        LightColor = new Color(1, 0.48f, 0.18f),
        OmniRange = 5,
        ShadowEnabled = false,
        Position = new Vector3(0, 0.7f, 0),
    };
    private readonly List<MeshInstance3D> _fireballs = new();
    private readonly List<ShaderMaterial> _fireMaterials = new();
    private GpuParticles3D _smoke = null!;
    private float _age;
    private bool _smokeStarted;

    public override void _Ready()
    {
        _flash.MaterialOverride = _flashMaterial;
        _flash.Position = new Vector3(0, 0.35f, 0);
        AddChild(_flash);
        _ring.MaterialOverride = _ringMaterial;
        AddChild(_ring);
        AddChild(_light);

        var shader = Networking.MatchResourceLoader.LoadResource<Shader>("res://assets/effects/ProxyMineFireball.gdshader");
        var sphere = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 32, Rings = 16 };
        for (int layer = 0; layer < 2; layer++)
        {
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("layer", layer);
            material.SetShaderParameter("phase", layer * 3.7f);
            var ball = new MeshInstance3D
            {
                Mesh = sphere,
                MaterialOverride = material,
                Position = new Vector3(0, 0.7f, 0),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            AddChild(ball);
            _fireballs.Add(ball);
            _fireMaterials.Add(material);
        }
        AddChild(Emitter("spark_01", 48, 0.58f, 4, 10, 0.045f, 0.13f,
            new Color(1, 0.65f, 0.19f), new Vector3(0, -9, 0)));
        _smoke = Emitter("smoke_01", 64, 2.15f, 1.7f, 3.8f, 0.46f, 1.38f,
            new Color(0.21f, 0.22f, 0.23f, 0.55f), new Vector3(0, 0.85f, 0));
        _smoke.Position = new Vector3(0, 0.45f, 0);
        _smoke.Emitting = false;
        _smoke.Explosiveness = 0.72f;
        var smokeProcess = (ParticleProcessMaterial)_smoke.ProcessMaterial;
        smokeProcess.Direction = Vector3.Up;
        smokeProcess.Spread = 34;
        smokeProcess.ColorRamp = new GradientTexture1D { Gradient = new Gradient
        {
            Colors = [new Color(1, 1, 1, 0), new Color(1, 1, 1, 0.9f), new Color(1, 1, 1, 0.55f), new Color(1, 1, 1, 0)],
            Offsets = [0f, 0.14f, 0.55f, 1f],
        } };
        AddChild(_smoke);
        AddChild(Emitter("spark_01", 12, 0.85f, 2.5f, 6, 0.075f, 0.15f,
            new Color(0.31f, 0.28f, 0.25f, 0.9f), new Vector3(0, -8, 0)));
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_age >= Duration) { QueueFree(); return; }

        float flash = Mathf.Clamp(1 - _age / 0.14f, 0, 1);
        _flash.Scale = Vector3.One * (0.6f + 1.7f * Mathf.Min(_age / 0.1f, 1));
        _flashMaterial.AlbedoColor = new Color(1, 0.91f, 0.63f, flash * 0.7f);

        float fireAge = _age - 0.06f;
        float growth = Mathf.Clamp(fireAge / 0.23f, 0, 1);
        float fireFade = 1 - Mathf.SmoothStep(0.38f, 0.85f, fireAge);
        float radius = 0.55f + 2.3f * Mathf.SmoothStep(0, 1, growth);
        for (int i = 0; i < _fireballs.Count; i++)
        {
            _fireballs[i].Visible = fireAge > 0 && fireFade > 0.001f;
            _fireballs[i].Scale = Vector3.One * radius * (i == 0 ? 0.78f : 1.0f);
            _fireMaterials[i].SetShaderParameter("age", Math.Max(0, fireAge));
            _fireMaterials[i].SetShaderParameter("strength", Mathf.Clamp(fireAge / 0.09f, 0, 1) * fireFade);
        }
        _light.LightEnergy = 3.6f * flash + fireFade * Mathf.Min(2.2f, growth * 2.2f);

        if (!_smokeStarted && _age >= 0.2f)
        {
            _smokeStarted = true;
            _smoke.Restart();
            _smoke.Emitting = true;
        }

        // This thin, fading wave is cinematic only; the Mine still affects one contacted vehicle.
        float ring = Mathf.Clamp(_age / 0.58f, 0, 1);
        _ring.Scale = new Vector3(0.35f + 5.9f * Mathf.SmoothStep(0, 1, ring), 0.18f, 0.35f + 5.9f * Mathf.SmoothStep(0, 1, ring));
        _ringMaterial.AlbedoColor = new Color(1, 0.69f, 0.35f, (1 - ring) * 0.52f);
        _ring.Visible = ring < 1;
    }

    public override void _ExitTree()
    {
        foreach (var ball in _fireballs) { ball.MaterialOverride = null; }
        foreach (var material in _fireMaterials) { material.Dispose(); }
        _flashMaterial.Dispose();
        _ringMaterial.Dispose();
    }

    private static GpuParticles3D Emitter(string texture, int amount, float lifetime,
        float speedMin, float speedMax, float sizeMin, float sizeMax, Color color, Vector3 gravity)
    {
        var material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            AlbedoTexture = Networking.MatchResourceLoader.LoadResource<Texture2D>($"res://assets/items/kenney/particles/{texture}.png"),
            AlbedoColor = color,
            VertexColorUseAsAlbedo = true,
        };
        return new GpuParticles3D
        {
            Amount = amount,
            Lifetime = lifetime,
            OneShot = true,
            Explosiveness = 1,
            LocalCoords = false,
            VisibilityAabb = new Aabb(new Vector3(-8, -3, -8), new Vector3(16, 14, 16)),
            ProcessMaterial = new ParticleProcessMaterial
            {
                Direction = Vector3.Up,
                Spread = 180,
                InitialVelocityMin = speedMin,
                InitialVelocityMax = speedMax,
                Gravity = gravity,
                ScaleMin = sizeMin,
                ScaleMax = sizeMax,
            },
            DrawPass1 = new QuadMesh { Size = Vector2.One, Material = material },
            Emitting = true,
        };
    }
}
