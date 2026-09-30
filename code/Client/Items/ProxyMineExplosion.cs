using Godot;

namespace Trackstorm.Client.Items;

/// <summary>A short-lived, contact-sized presentation of a confirmed mine detonation.</summary>
internal sealed partial class ProxyMineExplosion : Node3D
{
    internal const float Duration = 1.55f;

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
    private float _age;

    public override void _Ready()
    {
        _flash.MaterialOverride = _flashMaterial;
        _flash.Position = new Vector3(0, 0.35f, 0);
        AddChild(_flash);
        _ring.MaterialOverride = _ringMaterial;
        AddChild(_ring);
        AddChild(_light);

        AddChild(Emitter("fire_01", 48, 0.48f, 0.7f, 4.2f, 0.22f, 0.82f,
            new Color(1, 0.36f, 0.055f, 0.9f), new Vector3(0, 1.3f, 0)));
        AddChild(Emitter("spark_01", 48, 0.58f, 4, 10, 0.045f, 0.13f,
            new Color(1, 0.65f, 0.19f), new Vector3(0, -9, 0)));
        AddChild(Emitter("smoke_01", 30, 1.15f, 0.9f, 2.8f, 0.28f, 0.85f,
            new Color(0.23f, 0.23f, 0.24f, 0.43f), new Vector3(0, 0.8f, 0)));
        AddChild(Emitter("spark_01", 12, 0.85f, 2.5f, 6, 0.075f, 0.15f,
            new Color(0.31f, 0.28f, 0.25f, 0.9f), new Vector3(0, -8, 0)));
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_age >= Duration) { QueueFree(); return; }

        float flash = Mathf.Clamp(1 - _age / 0.23f, 0, 1);
        _flash.Scale = Vector3.One * (0.65f + 2.8f * Mathf.Min(_age / 0.12f, 1));
        _flashMaterial.AlbedoColor = new Color(1, 0.91f, 0.63f, flash * 0.78f);
        _light.LightEnergy = 3.6f * flash;

        float ring = Mathf.Clamp(_age / 0.34f, 0, 1);
        _ring.Scale = new Vector3(0.3f + 1.8f * ring, 0.25f, 0.3f + 1.8f * ring);
        _ringMaterial.AlbedoColor = new Color(1, 0.58f, 0.18f, (1 - ring) * 0.7f);
        _ring.Visible = ring < 1;
    }

    public override void _ExitTree()
    {
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
            VisibilityAabb = new Aabb(new Vector3(-5, -3, -5), new Vector3(10, 8, 10)),
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
