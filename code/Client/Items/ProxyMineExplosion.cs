using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Bounded dirt, debris and pressure presentation of a confirmed Mine detonation.</summary>
internal sealed partial class ProxyMineExplosion : Node3D
{
    internal const string PlumeTexturePath = "res://assets/effects/ProxyMinePlume.png";
    internal const string PressureWaveShaderPath = "res://assets/effects/ProxyMinePressureWave.gdshader";
    internal const float Duration = 3.8f;

    private ShaderMaterial _waveMaterial = null!;
    private readonly MeshInstance3D _wave = new()
    {
        Mesh = new PlaneMesh { Size = Vector2.One },
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
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
        _waveMaterial = new ShaderMaterial
        {
            Shader = Networking.MatchResourceLoader.LoadResource<Shader>(PressureWaveShaderPath),
        };
        _wave.MaterialOverride = _waveMaterial;
        AddChild(_wave);
        // The authoritative impact is at the Mine, which can sit above uneven terrain.
        // Place the cosmetic front on the actual ground so it remains readable.
        using (var ray = PhysicsRayQueryParameters3D.Create(GlobalPosition + Vector3.Up * 2,
            GlobalPosition + Vector3.Down * 4, 1))
        {
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
            if (hit.Count > 0)
            {
                Vector3 normal = hit["normal"].AsVector3().Normalized();
                if (normal.Y >= 0.55f)
                {
                    _wave.GlobalPosition = hit["position"].AsVector3() + normal * 0.06f;
                    _wave.Quaternion = new Quaternion(Vector3.Up, normal);
                }
            }
        }
        _plumeMaterial.AlbedoTexture = Networking.MatchResourceLoader.LoadResource<Texture2D>(PlumeTexturePath);
        _plume.MaterialOverride = _plumeMaterial;
        AddChild(_plume);

        // A broad, heavy ground burst precedes the lighter dust that climbs and disperses.
        AddChild(Dust(104, 1.7f, 4.5f, 9.0f, 0.8f, 1.9f,
            new Color(0.35f, 0.28f, 0.21f, 0.35f), 78, new Vector3(0, -0.8f, 0), 0.5f));
        AddChild(Dust(64, 2.5f, 2.0f, 4.0f, 0.95f, 2.2f,
            new Color(0.39f, 0.35f, 0.29f, 0.2f), 86, new Vector3(0, 0.1f, 0), 0.6f));
        var risingDust = Dust(96, 3.1f, 7.0f, 13.0f, 1.1f, 2.8f,
            new Color(0.42f, 0.38f, 0.32f, 0.2f), 50, new Vector3(0, -1.2f, 0), 0.5f);
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

        // The diffuse ground front is cosmetic; only the contacted car receives impulse.
        float wave = Mathf.Clamp(_age / 0.54f, 0, 1);
        float radius = 0.35f + 5.9f * Mathf.SmoothStep(0, 1, wave);
        _wave.Scale = new Vector3(radius * 2.55f, 1, radius * 2.55f);
        _waveMaterial.SetShaderParameter("opacity", 0.85f * (1 - wave));
        _wave.Visible = wave < 1;

        // One large textured volume ties the near-ground dust to the higher particle cloud.
        float rise = Mathf.SmoothStep(0.1f, 2.25f, _age);
        float opacity = Mathf.SmoothStep(0.08f, 0.4f, _age) *
            (1 - Mathf.SmoothStep(1.85f, 3.45f, _age));
        _plume.Visible = opacity > 0.001f;
        _plume.Scale = new Vector3(9.6f + 12.0f * rise, 6.9f + 7.5f * rise, 1);
        _plume.Position = new Vector3(0, 2.85f + 13.35f * rise, 0);
        _plumeMaterial.AlbedoColor = new Color(0.48f, 0.42f, 0.34f, 0.19f * opacity);
    }

    public override void _ExitTree()
    {
        _wave.MaterialOverride = null;
        _plume.MaterialOverride = null;
        _waveMaterial.Dispose();
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
            VisibilityAabb = new Aabb(new Vector3(-18, -2, -18), new Vector3(36, 30, 36)),
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
