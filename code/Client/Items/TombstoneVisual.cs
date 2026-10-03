using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>Blender-authored armor reconstructed from accepted health and lifecycle; no collision ownership.</summary>
internal sealed partial class TombstoneVisual : Node3D
{
    internal const string AssetPath = "res://assets/items/tombstone/Tombstone.glb";
    internal static readonly Vector3 MountedCenter = new(0, .25f, 3.25f);
    private Node3D _center = null!;
    private Node3D[] _wings = [];
    private Node3D[] _panels = [];
    private readonly List<ShaderMaterial> _materials = [];
    private float _hp = TombstoneState.DefaultHP;
    private float _flash;
    private float _expansion;
    private float _death = -1;
    private bool _world;
    private bool _seeded;
    private Vector3 _size = new(6.6f, 2.5f, .6f);
    private Vector3 _releaseOffset;
    private Quaternion _releaseRotation = Quaternion.Identity;
    private float _release;
    private float _fold;
    private float _releaseFold;
    private float _centerFold;
    private float _releaseCenterFold;
    private Vector3 _releaseScale = Vector3.One;
    private GpuParticles3D _sparks = null!;

    internal float Expansion => _expansion;
    internal float PresentedHP => _hp;
    internal float Fold => _fold;
    internal float CenterFold => _world ? _releaseCenterFold * Mathf.SmoothStep(0, 1, _release) : _centerFold;
    internal void SetFold(float fold, float centerFold = 0)
    { _fold = Mathf.Clamp(fold, 0, 1); _centerFold = Mathf.Clamp(centerFold, 0, 1); Pose(); }

    public override void _Ready()
    {
        var model = MatchResourceLoader.LoadResource<PackedScene>(AssetPath).Instantiate<Node3D>();
        AddChild(model);
        _center = (Node3D)model.FindChild("Center", true, false);
        _panels = [(Node3D)model.FindChild("Center_L", true, false), (Node3D)model.FindChild("Center_R", true, false)];
        _wings = [ (Node3D)model.FindChild("Wing_L", true, false), (Node3D)model.FindChild("Wing_R", true, false) ];
        var shader = MatchResourceLoader.LoadResource<Shader>("res://assets/items/tombstone/Tombstone.gdshader");
        foreach (var mesh in model.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>())
        {
            var original = mesh.GetActiveMaterial(0) as StandardMaterial3D;
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("base_color", original?.AlbedoColor.SrgbToLinear() ?? new Color(.24f, .27f, .28f));
            material.SetShaderParameter("metal", original?.Metallic ?? .7f);
            material.SetShaderParameter("lamp", mesh.Name.ToString().Contains("AmberLamp", StringComparison.Ordinal) ? 1f : 0f);
            mesh.MaterialOverride = material;
            _materials.Add(material);
        }
        _sparks = ItemPresentation.Particles("spark_01", true, .28f);
        _sparks.Amount = 12;
        _sparks.Emitting = false;
        _sparks.Position = new(0, .15f, .19f);
        if (_sparks.ProcessMaterial is ParticleProcessMaterial particles)
        {
            particles.InitialVelocityMin = .5f;
            particles.InitialVelocityMax = 2.5f;
            particles.ScaleMin = .025f;
            particles.ScaleMax = .07f;
            particles.Gravity = new(0, -5, 0);
        }
        AddChild(_sparks);
        Pose();
    }

    internal void Observe(TombstoneState state, bool reseed = false)
    {
        if (reseed) { _flash = 0; _sparks.Emitting = false; }
        if (_seeded && !reseed && state.HP < _hp) { _flash = 1; _sparks.Restart(); }
        _hp = state.HP;
        _seeded = true;
        foreach (var material in _materials) { material.SetShaderParameter("wear", 1 - _hp / TombstoneState.DefaultHP); }
    }

    internal void SetWorld(Vector3 size, bool animate, Transform3D? from = null, float fold = 0, float centerFold = 0)
    {
        _world = true;
        _size = size;
        _expansion = animate ? 0 : 1;
        _release = 0;
        _releaseOffset = Vector3.Zero;
        _releaseRotation = Quaternion.Identity;
        _releaseScale = Vector3.One;
        _releaseFold = fold;
        _releaseCenterFold = centerFold;
        if (animate && from is { } start)
        {
            _releaseOffset = GetParent<Node3D>().ToLocal(start.Origin);
            _releaseRotation = GetParent<Node3D>().GlobalBasis.GetRotationQuaternion().Inverse() * start.Basis.Orthonormalized().GetRotationQuaternion();
            _releaseScale = start.Basis.Scale;
            _release = 1;
        }
        Pose();
    }

    internal void BreakApart()
    {
        _death = 0;
        _flash = 1;
        _sparks.Restart();
    }

    public override void _Process(double delta)
    {
        float dt = Math.Max(0, (float)delta);
        _flash = Math.Max(0, _flash - dt * 7);
        if (_death >= 0)
        {
            _death += dt;
            if (_death >= .8f) { QueueFree(); return; }
            _center.Position += new Vector3(0, -2.5f * _death * dt, .3f * dt);
            for (int i = 0; i < _wings.Length; i++)
            {
                _wings[i].Position += new Vector3((i == 0 ? -1 : 1) * dt, -3 * _death * dt, dt * .5f);
                _wings[i].RotateZ((i == 0 ? -1 : 1) * dt * 1.8f);
            }
            Scale *= MathF.Exp(-dt * Math.Max(0, _death - .4f) * 18);
        }
        else
        {
            if (_world) { _expansion = Math.Min(1, _expansion + dt / .42f); }
            _release = Math.Max(0, _release - dt / .18f);
            Pose();
        }
        foreach (var material in _materials) { material.SetShaderParameter("flash", _flash); }
    }

    private void Pose()
    {
        if (_center is null) { return; }
        float expansion = Mathf.SmoothStep(0, 1, _expansion);
        _center.Position = Vector3.Zero;
        for (int i = 0; i < _wings.Length; i++)
        {
            int side = i == 0 ? -1 : 1;
            float centerFold = _world ? _releaseCenterFold * Mathf.SmoothStep(0, 1, _release) : _centerFold;
            _panels[i].Position = new(0, 0, .32f);
            _panels[i].Rotation = new(0, -side * MathF.PI / 2 * centerFold, 0);
            // Open the forward-wrapping side panels outward to the same plane.
            float fold = _world ? _releaseFold * Mathf.SmoothStep(0, 1, _release) : _fold;
            // The folding hinge slides the nested wing behind the central plate,
            // avoiding coplanar overlapping armor in the compact rack pose.
            _wings[i].Position = new(side * 1.85f, 0, -.32f - .4f * fold);
            _wings[i].Rotation = new(0, side * MathF.PI / 2 * (1 + fold) * (1 - expansion), 0);
        }
        if (_world)
        {
            Scale = new Vector3(_size.X / 6.6f, _size.Y / 2.5f, _size.Z / .6f) * Vector3.One.Lerp(_releaseScale, Mathf.SmoothStep(0, 1, _release));
            Position = _releaseOffset * Mathf.SmoothStep(0, 1, _release);
            Quaternion = Quaternion.Identity.Slerp(_releaseRotation, Mathf.SmoothStep(0, 1, _release));
        }
    }

    public override void _Notification(int what)
    {
        // A destroyed wall reparents this visual before removing its native body.
        // Leaving the tree during that move does not end material ownership.
        if (what == NotificationPredelete)
        { foreach (var material in _materials) { material.Dispose(); } }
    }
}
