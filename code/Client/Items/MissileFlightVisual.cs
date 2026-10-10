using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Reconstructable powered flight; stopped smoke can finish under the arena presentation owner.</summary>
internal sealed partial class MissileFlightVisual : Node3D
{
    internal const string ConfettiShaderPath = "res://assets/items/missile/BurningConfetti.gdshader";
    internal const string FlameShaderPath = "res://assets/items/missile/MissileFlame.gdshader";
    private readonly List<MeshInstance3D> _flames = [];
    private readonly MissileVisual _model = new();
    private readonly List<GpuParticles3D> _emitters = new();
    private MissileVfxSettings? _settings;
    private Vector3 _sample;
    private Vector3 _velocity;
    private float _sampleAge;
    private bool _observed;

    public override void _Ready()
    {
        AddChild(_model);
        _model.SetDeployment(1, 1);
        var flameMaterial = new ShaderMaterial { Shader = Networking.MatchResourceLoader.LoadResource<Shader>(FlameShaderPath) };
        for (int i = 0; i < 3; i++)
        {
            var flame = new MeshInstance3D { Name = "FlameCore" + i,
                Mesh = new QuadMesh { Material = flameMaterial },
                Basis = new Basis(Vector3.Back, i * MathF.PI / 3) * new Basis(Vector3.Right, MathF.PI / 2),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(flame); _flames.Add(flame);
        }
        Configure();
    }

    internal void Observe(Vector3 position, Vector3 velocity)
    {
        _sample = position;
        _velocity = velocity;
        _sampleAge = 0;
        if (!_observed || Position.DistanceSquaredTo(position) > 400)
        { Position = position; _observed = true; }
    }

    public override void _Process(double delta)
    {
        float seconds = Math.Max(0, (float)delta);
        if (_settings != MissileVfxSettings.Current) { Configure(); }
        _sampleAge = Math.Min(.10f, _sampleAge + seconds);
        Vector3 target = _sample + _velocity * _sampleAge;
        Position = Position.Lerp(target, 1 - MathF.Exp(-45 * seconds));
        if (_velocity.LengthSquared() > .001f)
        { Quaternion = Quaternion.Slerp(new Quaternion(Vector3.Forward, _velocity.Normalized()), 1 - MathF.Exp(-30 * seconds)); }
    }

    internal MissileSmokeResidue? RetireSmoke()
    {
        var smoke = _emitters.Where(e => e.Name == "CrimsonSmoke" || e.Name == "SmokeResidue").ToArray();
        if (smoke.Length == 0) { return null; }
        var residue = new MissileSmokeResidue { Lifetime = (float)smoke.Max(e => e.Lifetime) + .1f };
        GetParent().AddChild(residue);
        foreach (var emitter in smoke)
        {
            emitter.Emitting = false;
            emitter.Reparent(residue, true);
            _emitters.Remove(emitter);
        }
        return residue;
    }

    private void Configure()
    {
        foreach (var emitter in _emitters) { emitter.Emitting = false; emitter.QueueFree(); }
        _emitters.Clear();
        var tuning = MissileVfxSettings.Current;
        _settings = tuning;
        foreach (var flame in _flames)
        {
            ((QuadMesh)flame.Mesh).Size = new(tuning.FlameWidth * 2, tuning.FlameLength);
            flame.Position = new(0, 0, .73f + tuning.FlameLength * .5f);
        }
        AddEmitter("OrangeExhaust", null, 20, .12f, tuning.FlameWidth * .45f, .005f,
            new(1, .4f, .035f, 1), new(1, .13f, .005f, 0), tuning.FlameLength / .12f, 7, true, false);
        // A continuous local exhaust plume avoids gaps when a 120 m/s missile travels metres per render frame.
        // Separate world-space wisps carry the lingering history; neither layer follows gameplay collisions.
        AddEmitter("CrimsonSmoke", "smoke_01", (int)(48 * tuning.Density), .45f, .4f, tuning.SmokeSize,
            new(.85f, .8f, .82f, tuning.SmokeOpacity), new(.65f, .61f, .64f, 0), tuning.SmokeTrailLength / .45f, 7, false, false);
        AddEmitter("SmokeResidue", "smoke_01", (int)(96 * tuning.Density), tuning.SmokeLifetime, tuning.SmokeSize * .8f, tuning.SmokeSize * 3,
            new(.8f, .74f, .78f, tuning.SmokeOpacity * .6f), new(.65f, .61f, .64f, 0), .4f, 40, false, false);
        AddEmitter("BurningRedConfetti", null, (int)(56 * tuning.Density), tuning.ConfettiLifetime, tuning.ConfettiSize, tuning.ConfettiSize * .7f,
            new(.8f, .018f, .025f, 1), new(.15f, .006f, .003f, 0), 8, 32, false, true);
        AddEmitter("RedEmbers", null, (int)(24 * tuning.Density), tuning.EmberLifetime, .045f, .008f,
            new(1, .08f, .012f, 1), new(.5f, .008f, .005f, 0), 5, 24, true, false);
    }

    private void AddEmitter(string name, string? texture, int amount, float lifetime, float startSize, float endSize,
        Color start, Color end, float speed, float spread, bool luminous, bool paper)
    {
        if (amount <= 0) { return; }
        var material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = paper ? BaseMaterial3D.BillboardModeEnum.Disabled : BaseMaterial3D.BillboardModeEnum.Particles,
            BillboardKeepScale = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = texture is null ? new GradientTexture2D
            {
                Width = 32, Height = 32, Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new(.5f, .5f), FillTo = new(1, .5f),
                Gradient = new Gradient { Colors = [Colors.White, Colors.White, new Color(1, 1, 1, 0)], Offsets = [0, .25f, 1] },
            } : Networking.MatchResourceLoader.LoadResource<Texture2D>($"res://assets/items/kenney/particles/{texture}.png"),
            EmissionEnabled = luminous,
            Emission = luminous ? start : Colors.Black,
            EmissionEnergyMultiplier = luminous ? 1.7f : 0,
        };
        var gradient = new Gradient { Colors = [start, new Color(start.R, start.G, start.B, start.A * .8f), end], Offsets = [0, .45f, 1] };
        float maximumSize = Math.Max(startSize, endSize);
        var scale = new Curve();
        scale.AddPoint(new(0, startSize / maximumSize)); scale.AddPoint(new(1, endSize / maximumSize));
        var process = new ParticleProcessMaterial
        {
            Direction = Vector3.Back, Spread = spread,
            InitialVelocityMin = speed * .6f, InitialVelocityMax = speed,
            Gravity = paper ? new(0, -2, 0) : new(0, .5f, 0),
            AngularVelocityMin = paper ? -280 : -30, AngularVelocityMax = paper ? 280 : 30,
            AngleMin = 0, AngleMax = 360,
            ScaleMin = maximumSize, ScaleMax = maximumSize * 1.2f,
            ScaleCurve = new CurveTexture { Curve = scale },
            ColorRamp = new GradientTexture1D { Gradient = gradient },
        };
        var emitter = new GpuParticles3D
        {
            Name = name, Amount = Math.Clamp(amount, 1, 192), Lifetime = lifetime,
            Position = new(0, 0, .73f), LocalCoords = name is "OrangeExhaust" or "CrimsonSmoke",
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = name == "SmokeResidue"
                ? new Aabb(new(-2000, -2000, -2000), new(4000, 4000, 4000))
                : new Aabb(new(-180, -60, -180), new(360, 120, 360)),
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = paper ? new(.7f, 1) : Vector2.One, Material = paper ? new ShaderMaterial { Shader = Networking.MatchResourceLoader.LoadResource<Shader>(ConfettiShaderPath) } : material },
            Emitting = true, FixedFps = 60,
        };
        AddChild(emitter);
        _emitters.Add(emitter);
    }
}
