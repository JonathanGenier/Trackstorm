using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

/// <summary>Bounded wheel-contact presentation, independent of native forces and authoritative observations.</summary>
internal sealed partial class TireFeedback : Node3D
{
    internal const int Capacity = 512;
    private readonly Vector3?[] _previous = new Vector3?[4];
    private readonly SurfaceIdentity?[] _surfaces = new SurfaceIdentity?[4];
    private readonly GpuParticles3D[] _spray = new GpuParticles3D[4];
    private readonly MultiMeshInstance3D _marks = new() { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    private MultiMesh _instances = null!;
    private ShaderMaterial _material = null!;
    private PlaneMesh _mesh = null!;
    private MultiMesh _wakes = null!;
    private ShaderMaterial _wakeMaterial = null!;
    private PlaneMesh _wakeMesh = null!;
    private int _nextWake;
    private int _wakeCount;
    private float _elapsed;
    private float _sampleTime;
    private int _next;
    private ulong _life;
    internal Func<(Transform3D Pose, VehicleSnapshot State, VehicleConfiguration Configuration)?> Source { get; init; } = () => null;
    // A later local optimization setting can lower density without touching session state.
    internal float Density { get; set; } = 1;
    internal int MarksWritten { get; private set; }
    internal int WaterSamples { get; private set; }
    internal IReadOnlySet<SurfaceIdentity> Observed => _observed;
    private readonly HashSet<SurfaceIdentity> _observed = new();

    public override void _Ready()
    {
        TopLevel = true;
        GlobalTransform = Transform3D.Identity;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/effects/TireTrack.gdshader") };
        _mesh = new PlaneMesh { Size = Vector2.One, Material = _material };
        _instances = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = _mesh, InstanceCount = Capacity, VisibleInstanceCount = 0 };
        _marks.Multimesh = _instances;
        AddChild(_marks);
        _wakeMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/effects/WaterWake.gdshader") };
        _wakeMesh = new PlaneMesh { Size = Vector2.One, Material = _wakeMaterial };
        _wakes = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = _wakeMesh, InstanceCount = 32, VisibleInstanceCount = 0 };
        AddChild(new MultiMeshInstance3D { Multimesh = _wakes, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        for (int i = 0; i < 4; i++)
        {
            _spray[i] = Items.ItemPresentation.Particles("smoke_01", false, .65f);
            _spray[i].Amount = 24;
            _spray[i].Emitting = false;
            AddChild(_spray[i]);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _elapsed += (float)delta;
        _material.SetShaderParameter("elapsed", _elapsed);
        _wakeMaterial.SetShaderParameter("elapsed", _elapsed);
        _sampleTime += (float)delta;
        if (_sampleTime < .05f / Math.Clamp(Density, .25f, 1)) { return; }
        _sampleTime = 0;
        if (Density <= 0 || Source() is not { } source || !source.State.CanInteract ||
            GetViewport().GetCamera3D() is { } camera && camera.GlobalPosition.DistanceSquaredTo(source.Pose.Origin) > 10000)
        {
            Stop();
            return;
        }
        if (_life != source.State.LifeId) { Stop(); _life = source.State.LifeId; }
        float speed = source.State.Speed;
        int wheel = 0;
        foreach (float z in new[] { -source.Configuration.Wheelbase / 2, source.Configuration.Wheelbase / 2 })
        {
            foreach (float x in new[] { -VehicleDimensions.WheelTrack / 2, VehicleDimensions.WheelTrack / 2 })
            {
                Vector3 origin = source.Pose * new Vector3(x, 0, z);
                using var ray = PhysicsRayQueryParameters3D.Create(origin, origin - source.Pose.Basis.Y * (source.Configuration.SuspensionLength + .08f), 1,
                    new Godot.Collections.Array<Rid> { ((PhysicsBody3D)GetParent()).GetRid() });
                var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
                Vector3? waterPoint = FindWater(origin);
                if (waterPoint is null && (hit.Count == 0 || hit["normal"].AsVector3().Y < .55f || !source.State.Movement.Grounded))
                {
                    _previous[wheel] = null;
                    _spray[wheel++].Emitting = false;
                    continue;
                }
                var identity = waterPoint.HasValue ? SurfaceIdentity.Water : SurfaceIdentityResolver.Resolve(hit["collider"].AsGodotObject(), hit["position"].AsVector3());
                Vector3 point = waterPoint ?? hit["position"].AsVector3();
                Vector3 normal = waterPoint.HasValue ? Vector3.Up : hit["normal"].AsVector3();
                bool soft = identity is SurfaceIdentity.Dirt or SurfaceIdentity.Grass or SurfaceIdentity.Mud or SurfaceIdentity.DeepMud;
                bool water = identity == SurfaceIdentity.Water;
                if (identity is { } observed) { _observed.Add(observed); }
                if (water)
                {
                    WaterSamples++;
                    if (speed > .7f && wheel >= 2)
                    {
                        _wakes.SetInstanceTransform(_nextWake, new Transform3D(Basis.Identity.Scaled(new Vector3(1.2f,1,1.8f)), point + Vector3.Up*.025f));
                        _wakes.SetInstanceCustomData(_nextWake, new Color(_elapsed,0,0,0));
                        _nextWake = (_nextWake+1)%32;
                        _wakes.VisibleInstanceCount = Math.Min(++_wakeCount,32);
                    }
                }
                var style = Style(identity);
                var spray = _spray[wheel];
                spray.GlobalPosition = point + normal * .06f;
                spray.Emitting = speed > 2 && (soft || water);
                var process = (ParticleProcessMaterial)spray.ProcessMaterial;
                process.Direction = (normal - source.Pose.Basis.Z * .3f).Normalized();
                process.Spread = water ? 65 : 40;
                process.InitialVelocityMin = water ? 1.5f : .5f;
                process.InitialVelocityMax = Math.Clamp(speed * (water ? .35f : .12f), 1, 6);
                process.Gravity = new Vector3(0, water || identity is SurfaceIdentity.Mud or SurfaceIdentity.DeepMud ? -8 : .3f, 0);
                process.ScaleMin = water ? .06f : .1f;
                process.ScaleMax = water ? .22f : identity is SurfaceIdentity.Dirt ? .8f : .25f;
                ((StandardMaterial3D)((QuadMesh)spray.DrawPass1).Material).AlbedoColor = water ? new Color(.6f, .8f, .85f, .6f) : new Color(style.Color, .35f);
                bool mark = speed > .8f && !water && (soft || identity is SurfaceIdentity.Asphalt or SurfaceIdentity.Concrete && source.State.Movement.Drifting);
                if (mark && _surfaces[wheel] == identity && _previous[wheel] is { } previous)
                {
                    Vector3 direction = point - previous;
                    float length = direction.Length();
                    if (length is > .12f and < 3.5f)
                    {
                        Vector3 forward = direction.Slide(normal).Normalized();
                        if (!forward.IsZeroApprox())
                        {
                            var basis = new Basis(normal.Cross(forward).Normalized() * style.Width, normal, forward * (length + .08f));
                            _instances.SetInstanceTransform(_next, new Transform3D(basis, (point + previous) / 2 + normal * .018f));
                            _instances.SetInstanceColor(_next, style.Color);
                            _instances.SetInstanceCustomData(_next, new Color(_elapsed, 20, style.Roughness, soft ? 1 : 0));
                            _next = (_next + 1) % Capacity;
                            MarksWritten++;
                            _instances.VisibleInstanceCount = Math.Min(Capacity, MarksWritten);
                            _previous[wheel] = point;
                        }
                    }
                    else if (length >= 3.5f) { _previous[wheel] = point; }
                }
                else { _previous[wheel] = mark ? point : null; }
                _surfaces[wheel++] = identity;
            }
        }
    }

    internal static (Color Color, float Width, float Roughness) Style(SurfaceIdentity? identity) => identity switch
    {
        SurfaceIdentity.Asphalt => (new Color(.016f, .018f, .02f, .72f), .26f, .9f),
        SurfaceIdentity.Concrete => (new Color(.035f, .037f, .04f, .55f), .26f, .9f),
        SurfaceIdentity.Dirt => (new Color(.12f, .075f, .035f, .65f), .3f, 1f),
        SurfaceIdentity.Grass => (new Color(.24f, .15f, .065f, .85f), .34f, 1f),
        SurfaceIdentity.Mud => (new Color(.038f, .024f, .012f, .8f), .34f, .5f),
        SurfaceIdentity.DeepMud => (new Color(.022f, .014f, .008f, .94f), .43f, .32f),
        _ => (Colors.Transparent, 0, 1),
    };

    private Vector3? FindWater(Vector3 origin)
    {
        foreach (var member in GetTree().GetNodesInGroup("water_terrain"))
        {
            if (member is not Node3D terrain || terrain.GetWorld3D() != GetWorld3D() || !terrain.HasMeta("water_level") || !terrain.HasMeta("surface_bounds")) { continue; }
            Vector4 bounds = terrain.GetMeta("surface_bounds").AsVector4();
            Vector3 local = terrain.ToLocal(origin);
            if (local.X < bounds.X || local.Z < bounds.Y || local.X > bounds.X + bounds.Z || local.Z > bounds.Y + bounds.W) { continue; }
            float level = terrain.ToGlobal(new Vector3(0, terrain.GetMeta("water_level").AsSingle(), 0)).Y;
            if (origin.Y - VehicleDimensions.RideHeight > level + .05f || origin.Y < level - 2f) { continue; }
            if (SurfaceIdentityResolver.Resolve(terrain, origin) == SurfaceIdentity.Water) { return new Vector3(origin.X, level, origin.Z); }
        }
        return null;
    }

    private void Stop()
    {
        Array.Clear(_previous);
        Array.Clear(_surfaces);
        foreach (var emitter in _spray) { emitter.Emitting = false; }
    }

    public override void _ExitTree()
    {
        _marks.Multimesh = null;
        _instances.Dispose();
        _wakes.Dispose();
        _wakeMesh.Dispose();
        _wakeMaterial.Dispose();
        _mesh.Dispose();
        _material?.Dispose();
        foreach (var emitter in _spray)
        {
            var mesh = (QuadMesh)emitter.DrawPass1!;
            var material = mesh.Material;
            var process = emitter.ProcessMaterial;
            emitter.DrawPass1 = null;
            emitter.ProcessMaterial = null;
            material?.Dispose();
            mesh.Dispose();
            process?.Dispose();
        }
    }
}
