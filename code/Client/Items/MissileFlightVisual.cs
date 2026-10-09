using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Reconstructable powered flight, bounded world-space particles, removed with its projectile.</summary>
internal sealed partial class MissileFlightVisual : Node3D
{
    internal const string ConfettiShaderPath = "res://assets/items/missile/BurningConfetti.gdshader";
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

    private void Configure()
    {
        foreach (var emitter in _emitters) { emitter.Emitting = false; emitter.QueueFree(); }
        _emitters.Clear();
        var tuning = MissileVfxSettings.Current;
        _settings = tuning;
        AddEmitter("OrangeExhaust", null, 48, .12f, tuning.FlameWidth, .005f,
            new(1, .4f, .035f, 1), new(1, .13f, .005f, 0), tuning.FlameLength / .12f, 7, true, false);
        AddEmitter("CrimsonSmoke", "smoke_01", (int)(48 * tuning.Density), tuning.SmokeLifetime, .12f, tuning.SmokeSize,
            new(.22f, .009f, .022f, tuning.SmokeOpacity), new(.09f, .004f, .012f, 0), 1.5f, 20, false, false);
        AddEmitter("BurningRedConfetti", null, (int)(32 * tuning.Density), tuning.ConfettiLifetime, tuning.ConfettiSize, tuning.ConfettiSize * .7f,
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
            Name = name, Amount = Math.Clamp(amount, 1, 144), Lifetime = lifetime,
            Position = new(0, 0, .73f), LocalCoords = name == "OrangeExhaust",
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new(-180, -60, -180), new(360, 120, 360)),
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = paper ? new(.7f, 1) : Vector2.One, Material = paper ? new ShaderMaterial { Shader = Networking.MatchResourceLoader.LoadResource<Shader>(ConfettiShaderPath) } : material },
            Emitting = true, FixedFps = 60,
        };
        AddChild(emitter);
        _emitters.Add(emitter);
    }
}
