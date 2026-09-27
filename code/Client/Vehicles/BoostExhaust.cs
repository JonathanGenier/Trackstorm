using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

/// <summary>One Blender-authored rear jet and its bounded VFX, reconstructed from accepted Boost state.</summary>
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
    private GpuParticles3D _smoke = null!;
    private GpuParticles3D _sparks = null!;
    private ShaderMaterial _throat = null!;
    private MeshInstance3D _throatMesh = null!;
    private float _progress;
    private float _age;
    private float _release;
    private float _heat;
    private bool _burning;
    private bool _initialized;
    private bool _participating;
    private ulong _life;
    private Vector3 _previousPosition;

    internal Func<VehicleSnapshot?> Source { get; init; } = () => null;
    internal float Deployment => _progress;
    internal bool FlameVisible => _flames.Any(flame => flame.Visible);
    internal bool SmokeEmitting => _smoke.Emitting;
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
        _smoke = CreateParticles("smoke_01", 96, 1.3f, new Color(.64f, .67f, .70f, .8f), 1, 2.3f, 4, 7, 20);
        _smoke.Position = new Vector3(0, 0, .20f);
        _outlet.AddChild(_smoke);
        _sparks = CreateParticles("spark_01", 10, .22f, new Color(1, .45f, .08f, .65f), .025f, .065f, 9, 16, 9);
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
        _release = active ? 0 : _release + dt;
        // Leave the outlet extended while the newest smoke clears, then retract smoothly.
        float target = active ? 1 : _release < .18f ? _progress : 0;
        _progress = Mathf.MoveToward(_progress, target, dt / (active ? .24f : .48f));
        ApplyPose();
        bool burning = active && _progress >= .999f;
        if (burning && !_burning) { _age = 0; }
        _burning = burning;
        _age += dt;
        _heat = Mathf.MoveToward(_heat, burning ? 1 : 0, dt * (burning ? 12 : 4));
        _throat.SetShaderParameter("heat", _heat);
        float distance = GetViewport().GetCamera3D()?.GlobalPosition.DistanceTo(GlobalPosition) ?? 0;
        float speed = Math.Clamp((state?.Speed ?? 0) / 65, 0, 1);
        for (int i = 0; i < _flames.Count; i++)
        {
            _flames[i].Visible = burning && distance < 120;
            if (!burning) { continue; }
            _materials[i].SetShaderParameter("energy", Mathf.SmoothStep(0, .04f, _age));
            _materials[i].SetShaderParameter("age", _age);
            _materials[i].SetShaderParameter("speed", speed);
        }
        _smoke.Emitting = burning && distance < 65;
        _sparks.Emitting = burning && distance < 35;
    }

    /// <summary>Life, visibility, teleport and resync boundaries discard old jet pose and GPU history.</summary>
    internal void Reset()
    {
        _burning = false;
        _progress = _heat = 0;
        _release = 1;
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
        _sleeve.Position = new Vector3(0, 0, .35f * p);
        _nozzle.Position = new Vector3(0, 0, .72f * p);
        foreach (var ram in _rams) { ram.Scale = new Vector3(1, 1, 1 + .72f * p / .26f); }
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
