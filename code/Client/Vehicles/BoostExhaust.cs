using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

/// <summary>Rack-mounted jet readied by selection; accepted Boost state alone ignites its bounded VFX.</summary>
internal sealed partial class BoostExhaust : Node3D
{
    internal const string AssetPath = "res://assets/vehicles/boost/BoostJet.glb";
    private readonly List<Resource> _owned = new();
    private readonly List<MeshInstance3D> _flames = new();
    private readonly List<ShaderMaterial> _materials = new();
    private Node3D _sleeve = null!;
    private Node3D _nozzle = null!;
    private Node3D[] _rams = [];
    private Node3D _outlet = null!;
    private Node3D _turbine = null!;
    private GpuParticles3D _smoke = null!;
    private GpuParticles3D _sparks = null!;
    private ShaderMaterial _throat = null!;
    private MeshInstance3D _throatMesh = null!;
    private float _progress;
    private float _age;
    private float _heat;
    private float _ignitionTime;
    private float _fanSpeed;
    private bool _burning;
    private float _tailTime;
    private float _tailStartEnergy;
    private bool _depleted;
    private bool _depletionPending;
    private bool _initialized;
    private bool _participating;
    private ulong _life;
    private Vector3 _previousPosition;

    internal Func<VehicleSnapshot?> Source { get; init; } = () => null;
    internal bool Deploy { get; set; }
    internal float Deployment => _progress;
    internal bool FlameVisible => _flames.Any(flame => flame.Visible);
    internal bool SmokeEmitting => _smoke.Emitting;
    internal bool SparksEmitting => _sparks.Emitting;
    internal float FlameEnergy { get; private set; }
    internal bool DepletionBurst => _depleted && _tailTime > 0;
    internal float FanAngle => _turbine.Rotation.Z;
    internal Vector3 OutletPosition => _outlet.GlobalPosition;

    public override void _Ready()
    {
        Name = "BoostExhaust";
        var model = Networking.MatchResourceLoader.LoadResource<PackedScene>(AssetPath).Instantiate<Node3D>();
        AddChild(model);
        var mount = (Node3D)model.FindChild("Mount", true, false);
        _sleeve = mount.GetNode<Node3D>("Sleeve");
        _nozzle = mount.GetNode<Node3D>("Nozzle");
        _rams = [mount.GetNode<Node3D>("Ram_L"), mount.GetNode<Node3D>("Ram_R")];
        _outlet = _nozzle.GetNode<Node3D>("Outlet");
        _turbine = _nozzle.GetNode<Node3D>("Turbine");
        var authored = _outlet.GetNode<MeshInstance3D>("FlameSurface");
        var shader = Networking.MatchResourceLoader.LoadResource<Shader>("res://assets/effects/BoostFlame.gdshader");
        for (int layer = 0; layer < 3; layer++)
        {
            var material = Own(new ShaderMaterial { Shader = shader });
            material.SetShaderParameter("layer", layer);
            var flame = layer == 0 ? authored : new MeshInstance3D { Mesh = authored.Mesh };
            if (layer > 0) { _outlet.AddChild(flame); }
            flame.MaterialOverride = material;
            flame.Scale = layer switch { 1 => new Vector3(.53f, .53f, .53f), 2 => new Vector3(1.18f, 1.18f, 1.12f), _ => Vector3.One };
            flame.Visible = false;
            flame.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            flame.ExtraCullMargin = 3;
            flame.VisibilityRangeEnd = 120;
            _flames.Add(flame);
            _materials.Add(material);
        }
        _throatMesh = _nozzle.GetNode<MeshInstance3D>("Nozzle_Boost_Throat");
        _throat = Own(new ShaderMaterial { Shader = Networking.MatchResourceLoader.LoadResource<Shader>("res://assets/effects/BoostThroat.gdshader") });
        _throatMesh.MaterialOverride = _throat;
        _smoke = CreateParticles("smoke_01", 192, 1.5f, new Color(.48f, .51f, .54f, .8f), 1.1f, 2.2f, 4, 7, 16);
        _smoke.Position = new Vector3(0, 0, .20f);
        _outlet.AddChild(_smoke);
        _sparks = CreateParticles("spark_01", 24, .28f, new Color(1, .45f, .08f, .85f), .035f, .085f, 7, 12, 16);
        _outlet.AddChild(_sparks);
        ApplyPose();
    }

    public override void _Process(double delta)
    {
        var state = Source();
        bool participating = state is { CanInteract: true } && IsVisibleInTree();
        bool discontinuity = !_initialized || _life != (state?.LifeId ?? 0) ||
            GlobalPosition.DistanceSquaredTo(_previousPosition) > 100 || (_participating && !participating);
        if (discontinuity)
        {
            Reset();
            for (int i = 0; i < _materials.Count; i++)
            {
                _materials[i].SetShaderParameter("phase", i * 2.7f + ((state?.VehicleId ?? 0) % 997) * 1.71f);
            }
        }
        _initialized = true;
        _previousPosition = GlobalPosition;
        _life = state?.LifeId ?? 0;
        _participating = participating;
        bool active = participating && state!.Movement.Nitro.Active;
        float dt = Math.Min((float)delta, .1f);
        bool ready = participating && Deploy;
        // Keep the outlet clear of the bay until the last combustion has finished.
        bool finishing = _burning || _tailTime > 0 || _depletionPending;
        _progress = Mathf.MoveToward(_progress, ready || finishing ? 1 : 0, dt / .24f);
        ApplyPose();
        bool firing = active && ready && _progress >= .999f;
        // Each accepted activation primes locally; release discards any pending ignition.
        _ignitionTime = firing ? _ignitionTime + dt : 0;
        bool burning = firing && _ignitionTime >= .30f;
        if (_depletionPending)
        {
            _depletionPending = false;
            if (participating && _progress >= .999f)
            {
                _depleted = true;
                _tailTime = .60f;
                _tailStartEnergy = Math.Max(FlameEnergy, .65f);
            }
        }
        else if (_burning && !burning && !DepletionBurst)
        {
            _depleted = false;
            _tailTime = .45f;
            _tailStartEnergy = FlameEnergy;
        }
        else { _tailTime = Math.Max(0, _tailTime - dt); }
        if (burning) { _tailTime = 0; _depleted = false; }
        _fanSpeed = Mathf.MoveToward(_fanSpeed, participating ? (firing ? 10 : 2) : 0, dt * 24);
        _turbine.RotateObjectLocal(Vector3.Back, _fanSpeed * dt);
        if (burning && !_burning) { _age = 0; }
        _burning = burning;
        _age += dt;
        float tail = _tailTime / (_depleted ? .60f : .45f);
        float burst = DepletionBurst ? MathF.Sin(Mathf.Clamp((1 - tail) / .35f, 0, 1) * Mathf.Pi) : 0;
        float decay = Mathf.SmoothStep(0, 1, tail);
        FlameEnergy = burning ? Mathf.SmoothStep(0, .04f, _age) : _tailStartEnergy * decay * (1 + burst * .8f);
        _heat = Mathf.MoveToward(_heat, Math.Min(1, FlameEnergy), dt * (burning ? 12 : 4));
        _throat.SetShaderParameter("heat", _heat);
        float distance = GetViewport().GetCamera3D()?.GlobalPosition.DistanceTo(GlobalPosition) ?? 0;
        float speed = Math.Clamp((state?.Speed ?? 0) / 65, 0, 1);
        for (int i = 0; i < _flames.Count; i++)
        {
            _flames[i].Visible = FlameEnergy > .001f && distance < 120;
            _materials[i].SetShaderParameter("energy", FlameEnergy);
            _materials[i].SetShaderParameter("plume", burning ? 1 : .12f + .88f * decay);
            _materials[i].SetShaderParameter("burst", burst);
            _materials[i].SetShaderParameter("shutdown", DepletionBurst ? 1 - tail : 0);
            _materials[i].SetShaderParameter("age", _age);
            _materials[i].SetShaderParameter("speed", speed);
        }
        _smoke.Emitting = (firing || _tailTime > 0) && distance < 65;
        _smoke.AmountRatio = burning || DepletionBurst ? 1 : firing ? .55f : Math.Max(.05f, tail * .55f);
        _sparks.Emitting = (firing || (DepletionBurst && tail > .45f)) && distance < 35;
        _sparks.AmountRatio = burning ? .25f : 1;
    }

    /// <summary>Confirmed disposal of the selected Nitro capability, never inferred from input or predicted ticks.</summary>
    internal void Exhausted() => _depletionPending = true;

    /// <summary>Life, visibility, teleport and resync boundaries discard old jet pose and GPU history.</summary>
    internal void Reset()
    {
        _burning = false;
        _depleted = _depletionPending = false;
        _tailTime = _tailStartEnergy = FlameEnergy = 0;
        _progress = _heat = 0;
        _ignitionTime = _fanSpeed = 0;
        _turbine.Rotation = Vector3.Zero;
        Deploy = false;
        ApplyPose();
        _throat.SetShaderParameter("heat", 0);
        foreach (var flame in _flames) { flame.Visible = false; }
        foreach (var emitter in new[] { _smoke, _sparks }) { emitter.Emitting = false; emitter.Restart(); emitter.Emitting = false; }
    }

    public override void _ExitTree()
    {
        foreach (var flame in _flames) { flame.MaterialOverride = null; }
        _throatMesh.MaterialOverride = null;
        foreach (var emitter in new[] { _smoke, _sparks }) { emitter.ProcessMaterial = null; emitter.DrawPass1 = null; }
        for (int i = _owned.Count - 1; i >= 0; i--) { _owned[i].Dispose(); }
    }

    private void ApplyPose()
    {
        float p = Mathf.SmoothStep(0, 1, _progress);
        _sleeve.Position = new Vector3(0, 0, .14f * p);
        _nozzle.Position = new Vector3(0, 0, .28f * p);
        foreach (var ram in _rams) { ram.Scale = new Vector3(1, 1, 1 + .28f * p / .26f); }
    }

    private GpuParticles3D CreateParticles(string texture, int amount, float lifetime, Color color, float minScale, float maxScale, float minSpeed, float maxSpeed, float spread)
    {
        var particles = Items.ItemPresentation.Particles(texture, false, lifetime);
        particles.Emitting = false;
        particles.Amount = amount;
        particles.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        particles.VisibilityAabb = new Aabb(new Vector3(-70, -10, -70), new Vector3(140, 35, 140));
        var process = Own((ParticleProcessMaterial)particles.ProcessMaterial);
        var quad = Own((QuadMesh)particles.DrawPass1);
        var material = Own((StandardMaterial3D)quad.Material);
        material.AlbedoColor = color;
        material.VertexColorUseAsAlbedo = true;
        material.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
        process.Direction = Vector3.Back;
        process.Spread = spread;
        process.InitialVelocityMin = minSpeed;
        process.InitialVelocityMax = maxSpeed;
        process.ScaleMin = minScale;
        process.ScaleMax = maxScale;
        process.Gravity = new Vector3(0, texture == "smoke_01" ? .7f : -.8f, 0);
        process.AngleMin = -180;
        process.AngleMax = 180;
        var gradient = Own(new Gradient());
        gradient.SetColor(0, new Color(1, 1, 1, 0));
        gradient.SetColor(1, new Color(1, 1, 1, 0));
        gradient.AddPoint(.14f, Colors.White);
        gradient.AddPoint(.45f, new Color(1, 1, 1, .6f));
        process.ColorRamp = Own(new GradientTexture1D { Gradient = gradient });
        return particles;
    }

    private T Own<T>(T resource) where T : Resource { _owned.Add(resource); return resource; }
}
