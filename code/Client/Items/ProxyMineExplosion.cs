using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Bounded fireball, mushroom smoke and impulse presentation of a confirmed mine detonation.</summary>
internal sealed partial class ProxyMineExplosion : Node3D
{
    internal const string FireballTexturePath = "res://assets/effects/ProxyMineFireball.png";
    internal const string PlumeTexturePath = "res://assets/effects/ProxyMinePlume.png";
    internal const float Duration = 4.2f;

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
    private readonly StandardMaterial3D _fireCardMaterial = new()
    {
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        BillboardKeepScale = true,
    };
    private readonly StandardMaterial3D _smokeCardMaterial = new()
    {
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        BillboardKeepScale = true,
    };
    private readonly MeshInstance3D _fireCard = new()
    {
        Mesh = new QuadMesh { Size = Vector2.One },
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };
    private readonly MeshInstance3D _smokeCard = new()
    {
        Mesh = new QuadMesh { Size = Vector2.One },
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };
    private readonly MeshInstance3D _smokeColumnCard = new()
    {
        Mesh = new QuadMesh { Size = Vector2.One },
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };
    private readonly StandardMaterial3D _smokeColumnMaterial = new()
    {
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        BillboardKeepScale = true,
    };
    private GpuParticles3D _smokeStem = null!;
    private GpuParticles3D _smokeCap = null!;
    private float _age;
    private bool _stemStarted;
    private bool _capStarted;

    public override void _Ready()
    {
        _flash.MaterialOverride = _flashMaterial;
        _flash.Position = new Vector3(0, 0.35f, 0);
        AddChild(_flash);
        _ring.MaterialOverride = _ringMaterial;
        AddChild(_ring);
        AddChild(_light);

        _fireCardMaterial.AlbedoTexture = Networking.MatchResourceLoader.LoadResource<Texture2D>(FireballTexturePath);
        _fireCard.MaterialOverride = _fireCardMaterial;
        AddChild(_fireCard);
        _smokeCardMaterial.AlbedoTexture = Networking.MatchResourceLoader.LoadResource<Texture2D>(PlumeTexturePath);
        _smokeCard.MaterialOverride = _smokeCardMaterial;
        AddChild(_smokeCard);
        _smokeColumnMaterial.AlbedoTexture = _smokeCardMaterial.AlbedoTexture;
        _smokeColumnCard.MaterialOverride = _smokeColumnMaterial;
        AddChild(_smokeColumnCard);
        AddChild(Emitter("spark_01", 48, 0.58f, 4, 10, 0.045f, 0.13f,
            new Color(1, 0.65f, 0.19f), new Vector3(0, -9, 0)));
        // The rising card and particles occupy the fireball's volume during the crossfade.
        _smokeStem = Emitter("smoke_01", 64, 3.25f, 2.25f, 3.2f, 0.44f, 0.86f,
            new Color(0.22f, 0.21f, 0.2f, 0.43f), new Vector3(0, 0.3f, 0));
        _smokeStem.Position = new Vector3(0, 0.35f, 0);
        _smokeStem.Emitting = false;
        _smokeStem.Explosiveness = 0.48f;
        var stemProcess = (ParticleProcessMaterial)_smokeStem.ProcessMaterial;
        stemProcess.Direction = Vector3.Up;
        stemProcess.Spread = 12;
        stemProcess.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        stemProcess.EmissionSphereRadius = 0.28f;
        SetSmokeFade(stemProcess);
        AddChild(_smokeStem);

        _smokeCap = Emitter("smoke_01", 96, 3.25f, 1.7f, 2.5f, 0.68f, 1.25f,
            new Color(0.24f, 0.23f, 0.22f, 0.4f), new Vector3(0, 0.15f, 0));
        _smokeCap.Position = new Vector3(0, 2.3f, 0);
        _smokeCap.Emitting = false;
        _smokeCap.Explosiveness = 0.72f;
        var capProcess = (ParticleProcessMaterial)_smokeCap.ProcessMaterial;
        capProcess.Direction = Vector3.Up;
        capProcess.Spread = 78;
        capProcess.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        capProcess.EmissionSphereRadius = 0.42f;
        SetSmokeFade(capProcess);
        AddChild(_smokeCap);
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

        float fireGrowth = Mathf.SmoothStep(0, 1, Mathf.Clamp((_age - 0.05f) / 0.26f, 0, 1));
        float fireFade = 1 - Mathf.SmoothStep(0.57f, 1.04f, _age);
        _fireCard.Visible = _age > 0.05f && fireFade > 0.001f;
        _fireCard.Scale = Vector3.One * (0.8f + 5.1f * fireGrowth);
        _fireCard.Position = new Vector3(0, 1.05f + 0.45f * Mathf.SmoothStep(0.25f, 0.9f, _age), 0);
        _fireCardMaterial.AlbedoColor = new Color(1, 1, 1, fireFade);

        float smokeRise = Mathf.SmoothStep(0.3f, 2.25f, _age);
        float smokeOpacity = Mathf.SmoothStep(0.29f, 0.83f, _age) *
            (1 - Mathf.SmoothStep(2.05f, 3.65f, _age));
        _smokeCard.Visible = smokeOpacity > 0.001f;
        _smokeCard.Scale = new Vector3(2.9f + 3.5f * smokeRise, 2.8f + 0.8f * smokeRise, 1);
        _smokeCard.Position = new Vector3(0, 1.12f + 3.8f * smokeRise, 0);
        _smokeCardMaterial.AlbedoColor = new Color(0.39f, 0.37f, 0.35f, 0.62f * smokeOpacity);
        float columnOpacity = Mathf.SmoothStep(0.36f, 0.83f, _age) *
            (1 - Mathf.SmoothStep(1.9f, 3.45f, _age));
        _smokeColumnCard.Visible = columnOpacity > 0.001f;
        _smokeColumnCard.Scale = new Vector3(1.8f + 0.65f * smokeRise, 2.8f + 3.0f * smokeRise, 1);
        _smokeColumnCard.Position = new Vector3(0, 1.2f + 1.45f * smokeRise, 0);
        _smokeColumnMaterial.AlbedoColor = new Color(0.37f, 0.35f, 0.33f, 0.35f * columnOpacity);
        _light.LightEnergy = 3.6f * flash + fireFade * Mathf.Min(2.8f, fireGrowth * 2.8f);

        if (!_stemStarted && _age >= 0.17f)
        {
            _stemStarted = true;
            _smokeStem.Restart();
            _smokeStem.Emitting = true;
        }
        if (!_capStarted && _age >= 0.37f)
        {
            _capStarted = true;
            _smokeCap.Restart();
            _smokeCap.Emitting = true;
        }

        // This thin, fading wave is cinematic only; the Mine still affects one contacted vehicle.
        float ring = Mathf.Clamp(_age / 0.58f, 0, 1);
        _ring.Scale = new Vector3(0.35f + 5.9f * Mathf.SmoothStep(0, 1, ring), 0.18f, 0.35f + 5.9f * Mathf.SmoothStep(0, 1, ring));
        _ringMaterial.AlbedoColor = new Color(1, 0.69f, 0.35f, (1 - ring) * 0.52f);
        _ring.Visible = ring < 1;
    }

    public override void _ExitTree()
    {
        _fireCard.MaterialOverride = null;
        _smokeCard.MaterialOverride = null;
        _smokeColumnCard.MaterialOverride = null;
        _fireCardMaterial.Dispose();
        _smokeCardMaterial.Dispose();
        _smokeColumnMaterial.Dispose();
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
            AlbedoTexture = Networking.MatchResourceLoader.LoadResource<Texture2D>(
                $"res://assets/items/kenney/particles/{texture}.png"),
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

    private static void SetSmokeFade(ParticleProcessMaterial process)
    {
        process.ColorRamp = new GradientTexture1D { Gradient = new Gradient
        {
            Colors = [new Color(1, 1, 1, 0), new Color(1, 1, 1, 0.92f), new Color(1, 1, 1, 0.62f), new Color(1, 1, 1, 0)],
            Offsets = [0f, 0.12f, 0.47f, 1f],
        } };
    }
}
