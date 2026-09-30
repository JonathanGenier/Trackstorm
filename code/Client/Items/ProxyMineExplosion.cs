using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Bounded dirt, debris and pressure presentation of a confirmed Mine detonation.</summary>
internal sealed partial class ProxyMineExplosion : Node3D
{
    internal const string PlumeTexturePath = "res://assets/effects/ProxyMinePlume.png";
    internal const float Duration = 3.8f;

    private readonly StandardMaterial3D _ringMaterial = new()
    {
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = new Color(0.72f, 0.67f, 0.57f, 0),
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };
    private readonly MeshInstance3D _ring = new()
    {
        Mesh = new TorusMesh { InnerRadius = 0.94f, OuterRadius = 1, Rings = 80, RingSegments = 8 },
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        Position = new Vector3(0, 0.12f, 0),
    };
    private readonly StandardMaterial3D _plumeMaterial = new()
    {
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        BillboardKeepScale = true,
    };
    private readonly MeshInstance3D _plume = new()
    {
        Mesh = new QuadMesh { Size = Vector2.One },
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };
    private float _age;

    public override void _Ready()
    {
        _ring.MaterialOverride = _ringMaterial;
        AddChild(_ring);
        _plumeMaterial.AlbedoTexture = Networking.MatchResourceLoader.LoadResource<Texture2D>(PlumeTexturePath);
        _plume.MaterialOverride = _plumeMaterial;
        AddChild(_plume);

        // A broad, heavy ground burst precedes the lighter dust that climbs and disperses.
        AddChild(Dust(104, 1.7f, 3.2f, 6.2f, 0.38f, 0.85f,
            new Color(0.35f, 0.28f, 0.21f, 0.55f), 74, new Vector3(0, -0.8f, 0), 0.34f));
        AddChild(Dust(64, 2.5f, 1.2f, 2.4f, 0.48f, 1.05f,
            new Color(0.39f, 0.35f, 0.29f, 0.31f), 86, new Vector3(0, 0.1f, 0), 0.42f));
        var risingDust = Dust(96, 3.1f, 4.8f, 8.2f, 0.54f, 1.25f,
            new Color(0.42f, 0.38f, 0.32f, 0.32f), 44, new Vector3(0, -1.1f, 0), 0.32f);
        risingDust.Position = new Vector3(0, 0.3f, 0);
        AddChild(risingDust);

        var debrisMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.18f, 0.14f, 0.11f),
            Roughness = 1,
        };
        AddChild(new GpuParticles3D
        {
            Amount = 38,
            Lifetime = 1.55f,
            OneShot = true,
            Explosiveness = 1,
            LocalCoords = false,
            VisibilityAabb = new Aabb(new Vector3(-9, -3, -9), new Vector3(18, 16, 18)),
            ProcessMaterial = new ParticleProcessMaterial
            {
                Direction = Vector3.Up,
                Spread = 66,
                InitialVelocityMin = 6,
                InitialVelocityMax = 11,
                Gravity = new Vector3(0, -16, 0),
                ScaleMin = 0.55f,
                ScaleMax = 1.8f,
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = 0.22f,
            },
            DrawPass1 = new SphereMesh
            {
                Radius = 0.11f,
                Height = 0.19f,
                RadialSegments = 5,
                Rings = 3,
                Material = debrisMaterial,
            },
            Emitting = true,
        });
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_age >= Duration) { QueueFree(); return; }

        // The ring is a short cosmetic pressure cue; only the contacted car receives impulse.
        float wave = Mathf.Clamp(_age / 0.54f, 0, 1);
        float radius = 0.35f + 5.9f * Mathf.SmoothStep(0, 1, wave);
        _ring.Scale = new Vector3(radius, 0.16f, radius);
        _ringMaterial.AlbedoColor = new Color(0.73f, 0.68f, 0.58f, 0.44f * (1 - wave));
        _ring.Visible = wave < 1;

        // One large textured volume ties the near-ground dust to the higher particle cloud.
        float rise = Mathf.SmoothStep(0.1f, 2.25f, _age);
        float opacity = Mathf.SmoothStep(0.08f, 0.4f, _age) *
            (1 - Mathf.SmoothStep(1.85f, 3.45f, _age));
        _plume.Visible = opacity > 0.001f;
        _plume.Scale = new Vector3(3.2f + 4.0f * rise, 2.3f + 2.5f * rise, 1);
        _plume.Position = new Vector3(0, 0.95f + 4.45f * rise, 0);
        _plumeMaterial.AlbedoColor = new Color(0.48f, 0.42f, 0.34f, 0.4f * opacity);
    }

    public override void _ExitTree()
    {
        _ring.MaterialOverride = null;
        _plume.MaterialOverride = null;
        _ringMaterial.Dispose();
        _plumeMaterial.Dispose();
    }

    private static GpuParticles3D Dust(int amount, float lifetime, float speedMin,
        float speedMax, float sizeMin, float sizeMax, Color color, float spread,
        Vector3 gravity, float emissionRadius)
    {
        var material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            AlbedoTexture = Networking.MatchResourceLoader.LoadResource<Texture2D>(
                "res://assets/items/kenney/particles/smoke_01.png"),
            AlbedoColor = color,
            VertexColorUseAsAlbedo = true,
        };
        return new GpuParticles3D
        {
            Amount = amount,
            Lifetime = lifetime,
            OneShot = true,
            Explosiveness = 0.9f,
            LocalCoords = false,
            VisibilityAabb = new Aabb(new Vector3(-10, -2, -10), new Vector3(20, 17, 20)),
            ProcessMaterial = new ParticleProcessMaterial
            {
                Direction = Vector3.Up,
                Spread = spread,
                InitialVelocityMin = speedMin,
                InitialVelocityMax = speedMax,
                Gravity = gravity,
                ScaleMin = sizeMin,
                ScaleMax = sizeMax,
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = emissionRadius,
                ColorRamp = new GradientTexture1D { Gradient = new Gradient
                {
                    Colors = [new Color(1, 1, 1, 0), new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.78f), new Color(1, 1, 1, 0)],
                    Offsets = [0f, 0.08f, 0.52f, 1f],
                } },
            },
            DrawPass1 = new QuadMesh { Size = Vector2.One, Material = material },
            Emitting = true,
        };
    }
}
