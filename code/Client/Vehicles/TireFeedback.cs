using Godot;
using Trackstorm.Core.Vehicles;
using Trackstorm.Core.Settings;

namespace Trackstorm.Client.Vehicles;

/// <summary>Bounded wheel-contact presentation, independent of native forces and authoritative observations.</summary>
internal sealed partial class TireFeedback : Node3D
{
    // Continuity guard, not a styling control: bounds bridging across missing support/teleports.
    private const float MaximumSegmentLength = 8;
    private TireMarkBatch _batch = null!;
    private float _wakeLifetime = .9f;
    private readonly PhysicsRayQueryParameters3D _ray = new() { CollisionMask = 1 };
    private readonly Godot.Collections.Array<Rid> _excluded = new();
    private readonly Vector3?[] _previous = new Vector3?[4];
    private readonly SurfaceIdentity?[] _surfaces = new SurfaceIdentity?[4];
    private readonly GpuParticles3D[] _spray = new GpuParticles3D[4];

    private MultiMesh _wakes = null!;
    private ShaderMaterial _wakeMaterial = null!;
    private PlaneMesh _wakeMesh = null!;
    private int _nextWake;
    private int _wakeCount;
    private float _elapsed;
    private float _sampleTime;

    private float _lastWake = -3;
    private ulong _life;
    internal Func<(Transform3D Pose, VehicleSnapshot State, VehicleConfiguration Configuration)?> Source { get; init; } = () => null;
    // Verification/embedding multiplier; player tuning comes from the arena settings source.
    internal float Density { get; set; } = 1;
    internal int MarksWritten { get; private set; }
    internal int WaterSamples { get; private set; }
    internal IReadOnlySet<SurfaceIdentity> Observed => _observed;
    private readonly HashSet<SurfaceIdentity> _observed = new();
    private readonly float[] _wakeTimes = new float[4];
    private readonly (float Duration, float Width, float Intensity, float Fade, float Density, float Speed)[] _profiles = new (float, float, float, float, float, float)[7];
    private TireEffectSettings? _tuning;
    private float _duration, _width, _intensity, _fade, _spacing, _speed, _spraySpeed, _slip, _distance, _quality;

    private void RefreshTuning(TireEffectSettings tuning)
    {
        if (ReferenceEquals(_tuning, tuning)) { return; }
        _tuning = tuning;
        _duration = tuning["tire.lifetime"]; _width = tuning["tire.width"]; _intensity = tuning["tire.intensity"];
        _fade = tuning["tire.fade"]; _spacing = tuning["tire.spacing"]; _speed = tuning["tire.speed"];
        _spraySpeed = tuning["tire.spray_speed"];
        _slip = tuning["tire.slip"]; _distance = tuning["tire.distance"]; _quality = tuning["tire.quality"];
        string[] surfaces = ["asphalt", "concrete", "dirt", "grass", "mud", "deep_mud", "water"];
        for (int i = 0; i < surfaces.Length; i++)
        {
            string key = "tire." + surfaces[i] + ".";
            _profiles[i] = (tuning[key + "duration"], tuning[key + "width"], tuning[key + "intensity"], tuning[key + "fade"], tuning[key + "density"], tuning[key + "speed"]);
        }
    }

    private (float Duration, float Width, float Intensity, float Fade, float Density, float Speed) Profile(SurfaceIdentity? identity) => _profiles[identity switch
    {
        SurfaceIdentity.Asphalt => 0, SurfaceIdentity.Concrete => 1, SurfaceIdentity.Dirt => 2,
        SurfaceIdentity.Grass => 3, SurfaceIdentity.Mud => 4, SurfaceIdentity.DeepMud => 5, _ => 6,
    }];

    public override void _Ready()
    {
        TopLevel = true;
        GlobalTransform = Transform3D.Identity;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _excluded.Add(((PhysicsBody3D)GetParent()).GetRid());
        _ray.Exclude = _excluded;
        _batch = TireMarkBatch.For(GetParent().GetParent());
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
        if (_elapsed >= 3600)
        {
            _elapsed = 0;
            _lastWake = -3;
            _wakeCount = _nextWake = 0;
            Array.Clear(_wakeTimes);
            _wakes.VisibleInstanceCount = 0;
        }
        var tuning = _batch.Settings;
        RefreshTuning(tuning);
        _wakeMaterial.SetShaderParameter("elapsed", _elapsed);
        _wakeMaterial.SetShaderParameter("visibility_distance", _distance);
        if (_wakeCount > 0 && _elapsed - _lastWake >= _wakeLifetime)
        {
            _wakes.VisibleInstanceCount = 0;
            _wakeCount = _nextWake = 0;
            _wakeLifetime = .1f;
        }
        _sampleTime += (float)delta;
        if (_sampleTime < .05f / Math.Clamp(Density * _quality, .5f, 1)) { return; }
        _sampleTime = 0;
        if (Density * _quality <= 0 || Source() is not { } source || !source.State.CanInteract ||
            GetViewport().GetCamera3D() is { } camera && camera.GlobalPosition.DistanceSquaredTo(source.Pose.Origin) > _distance * _distance)
        {
            Stop();
            return;
        }
        if (_life != source.State.LifeId) { Stop(); _life = source.State.LifeId; }
        float speed = source.State.Speed;
        var waterTerrains = GetTree().GetNodesInGroup("water_terrain");
        var space = GetWorld3D().DirectSpaceState;
        for (int wheel = 0; wheel < 4; wheel++)
        {
            float z = (wheel < 2 ? -1 : 1) * source.Configuration.Wheelbase / 2;
            float x = (wheel % 2 == 0 ? -1 : 1) * VehicleDimensions.WheelTrack / 2;
            Vector3 origin = source.Pose * new Vector3(x, 0, z);
            _ray.From = origin;
            _ray.To = origin - source.Pose.Basis.Y * (source.Configuration.SuspensionLength + .08f);
            using var hit = space.IntersectRay(_ray);
            Vector3? waterPoint = FindWater(origin, waterTerrains);
            if (waterPoint is null && (hit.Count == 0 || hit["normal"].AsVector3().Y < .55f || !source.State.Movement.Grounded))
            {
                _previous[wheel] = null;
                _spray[wheel].Emitting = false;
                continue;
            }
            var identity = waterPoint.HasValue ? SurfaceIdentity.Water : SurfaceIdentityResolver.Resolve(hit["collider"].AsGodotObject(), hit["position"].AsVector3());
            Vector3 point = waterPoint ?? hit["position"].AsVector3();
            Vector3 normal = waterPoint.HasValue ? Vector3.Up : hit["normal"].AsVector3();
            bool soft = identity is SurfaceIdentity.Dirt or SurfaceIdentity.Grass or SurfaceIdentity.Mud or SurfaceIdentity.DeepMud;
            bool water = identity == SurfaceIdentity.Water;
            var profile = Profile(identity);
            float density = profile.Density * Density * _quality;
            if (identity is { } observed) { _observed.Add(observed); }
            if (water)
            {
                WaterSamples++;
                if (speed > _speed * profile.Speed && density > 0 && wheel >= 2 && _elapsed - _wakeTimes[wheel] >= .05f / density)
                {
                    _wakes.SetInstanceTransform(_nextWake, new Transform3D(Basis.Identity.Scaled(new Vector3(1.2f * _width * profile.Width,1,1.8f * _width * profile.Width)), point + Vector3.Up*.025f));
                    _wakes.SetInstanceCustomData(_nextWake, new Color(_elapsed, profile.Duration, Math.Min(profile.Duration, profile.Duration * _fade * profile.Fade), Math.Clamp(_intensity * profile.Intensity, 0, 2)));
                    _nextWake = (_nextWake+1)%32;
                    _wakeCount = Math.Min(_wakeCount + 1, 32);
                    _wakes.VisibleInstanceCount = _wakeCount;
                    _lastWake = _elapsed;
                    _wakeTimes[wheel] = _elapsed;
                    _wakeLifetime = Math.Max(_wakeLifetime, profile.Duration);
                }
            }
            var style = Style(identity);
            var spray = _spray[wheel];
            spray.GlobalPosition = point + normal * .06f;
            spray.Emitting = density > 0 && speed > _spraySpeed * profile.Speed && (soft || water);
            spray.AmountRatio = Math.Clamp(density, 0, 1);
            var process = (ParticleProcessMaterial)spray.ProcessMaterial;
            process.Direction = (normal - source.Pose.Basis.Z * .3f).Normalized();
            process.Spread = water ? 65 : 40;
            process.InitialVelocityMin = water ? 1.5f : .5f;
            process.InitialVelocityMax = Math.Clamp(speed * (water ? .35f : .12f), 1, 6);
            process.Gravity = new Vector3(0, water || identity is SurfaceIdentity.Mud or SurfaceIdentity.DeepMud ? -8 : .3f, 0);
            process.ScaleMin = water ? .06f : .1f;
            process.ScaleMax = water ? .22f : identity is SurfaceIdentity.Dirt ? .8f : .25f;
            ((StandardMaterial3D)((QuadMesh)spray.DrawPass1).Material).AlbedoColor = water ? new Color(.6f, .8f, .85f, Math.Clamp(.6f * _intensity * profile.Intensity, 0, 1)) : new Color(style.Color, Math.Clamp(.35f * _intensity * profile.Intensity, 0, 1));
            float slip = wheel < 2 ? source.State.Movement.FrontSlip : source.State.Movement.RearSlip;
            bool mark = density > 0 && speed > _speed * profile.Speed && !water && (soft || identity is SurfaceIdentity.Asphalt or SurfaceIdentity.Concrete && source.State.Movement.Drifting && slip >= _slip);
            if (mark && _surfaces[wheel] == identity && _previous[wheel] is { } previous)
            {
                Vector3 direction = point - previous;
                float length = direction.Length();
                if (length >= Math.Min(MaximumSegmentLength * .75f, _spacing / Math.Max(density, .01f)) && length < MaximumSegmentLength)
                {
                    Vector3 forward = direction.Slide(normal).Normalized();
                    if (!forward.IsZeroApprox())
                    {
                        var basis = new Basis(normal.Cross(forward).Normalized() * style.Width * _width * profile.Width, normal, forward * (length + .08f));
                        float duration = _duration * profile.Duration;
                        _batch.Write(new Transform3D(basis, (point + previous) / 2 + normal * .018f),
                            new Color(style.Color, Math.Clamp(style.Color.A * _intensity * profile.Intensity, 0, 1)),
                            duration, Math.Clamp(duration * _fade * profile.Fade, .01f, duration), style.Roughness, soft);
                        MarksWritten++;
                        _previous[wheel] = point;
                    }
                }
                else if (length >= MaximumSegmentLength) { _previous[wheel] = point; }
            }
            else { _previous[wheel] = mark ? point : null; }
            _surfaces[wheel] = identity;
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

    private Vector3? FindWater(Vector3 origin, Godot.Collections.Array<Node> waterTerrains)
    {
        foreach (var member in waterTerrains)
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
        _ray.Dispose();

        _wakes.Dispose();
        _wakeMesh.Dispose();
        _wakeMaterial.Dispose();

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
