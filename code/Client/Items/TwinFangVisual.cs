using Godot;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>Confirmed-shot-driven Twin Fang rig; drums yaw, guns pitch, feeds flex and advance.</summary>
internal sealed partial class TwinFangVisual : Node3D
{
    internal const string AssetPath = "res://assets/items/machine-gun/TwinFang.glb";
    internal const string CartridgePath = "res://assets/items/machine-gun/Cartridge.glb";
    internal const string FlashShaderPath = "res://assets/items/machine-gun/MuzzleFlash.gdshader";
    internal const string MetalShaderPath = "res://assets/items/machine-gun/WornMetal.gdshader";
    private const int Links = 32;
    private readonly Node3D _pitch;
    private readonly Node3D[] _guns;
    private readonly MeshInstance3D[] _flashes;
    private readonly GpuParticles3D[] _smoke;
    private readonly List<(MultiMesh Mesh, Transform3D Source, int Side)> _belts = [];
    private readonly Vector3[] _path = new Vector3[65];
    private readonly float[] _distance = new float[65];
    private readonly float[] _pulse = new float[2];
    private readonly float[] _phase = new float[2];
    private readonly float[] _feed = new float[2];
    private ulong _lastTick;
    private readonly float[] _heat = new float[2];
    private readonly float[] _shotAge = [10, 10];

    internal TwinFangVisual()
    {
        Name = "TwinFang";
        var model = Networking.MatchResourceLoader.LoadResource<PackedScene>(AssetPath).Instantiate<Node3D>();
        AddChild(model);
        foreach (var mesh in Descendants(model).OfType<MeshInstance3D>())
        {
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.Mesh.SurfaceGetMaterial(surface) is not StandardMaterial3D original) { continue; }
                var worn = CreateSkin(mesh, original);
                mesh.SetSurfaceOverrideMaterial(surface, worn);
            }
        }
        _pitch = (Node3D)model.FindChild("FangPitch", true, false);
        _guns = new[] { "Gun_-1", "Gun_1" }.Select(name => (Node3D)model.FindChild(name, true, false)).ToArray();
        _flashes = _guns.Select((gun, index) =>
        {
            var flash = new MeshInstance3D { Name = "MuzzleFlash", Position = new(index == 0 ? -.28896f : .28896f, 0, -WeaponAim.MachineGunMuzzleDistance), Visible = false,
                Mesh = new QuadMesh { Size = new(.32f, .32f) },
                MaterialOverride = new ShaderMaterial { Shader = Networking.MatchResourceLoader.LoadResource<Shader>(FlashShaderPath) },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            foreach (float angle in new[] { 0f, MathF.PI / 2 })
            {
                flash.AddChild(new MeshInstance3D { Mesh = flash.Mesh, MaterialOverride = flash.MaterialOverride,
                    Position = new(0, 0, -.06f), Rotation = new(MathF.PI / 2, angle, 0),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            }
            gun.AddChild(flash);
            return flash;
        }).ToArray();
        _smoke = _guns.Select((gun, index) =>
        {
            var smoke = ItemPresentation.Particles("smoke_01", false, .22f);
            smoke.Name = "BarrelSmoke"; smoke.Position = new(index == 0 ? -.28896f : .28896f, 0, -WeaponAim.MachineGunMuzzleDistance);
            smoke.Amount = 3; smoke.Emitting = false; smoke.Visible = false;
            var process = (ParticleProcessMaterial)smoke.ProcessMaterial;
            process.ScaleMin = .02f; process.ScaleMax = .045f;
            process.InitialVelocityMin = .12f; process.InitialVelocityMax = .3f;
            process.ColorRamp = new GradientTexture1D { Gradient = new Gradient { Colors = [new(1, 1, 1, .07f), new(1, 1, 1, 0)], Offsets = [0, 1] } };
            gun.AddChild(smoke); return smoke;
        }).ToArray();
        var cartridge = Networking.MatchResourceLoader.LoadResource<PackedScene>(CartridgePath).Instantiate<Node3D>();
        foreach (var mesh in Descendants(cartridge).OfType<MeshInstance3D>())
        {
            for (int side = 0; side < 2; side++)
            {
                var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh.Mesh, InstanceCount = Links };
                AddChild(new MultiMeshInstance3D { Name = "Feed_" + side, Multimesh = multi, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off });
                _belts.Add((multi, mesh.Transform, side));
            }
        }
        cartridge.Free();
        Present(0, 0, false);
    }

    internal float FeedPhase => _phase[0] + _phase[1];
    internal bool FlashVisible => _flashes.Any(flash => flash.Visible);
    internal float Pitch => _pitch.Rotation.X;

    internal Vector3 Muzzle(int side) => _guns[side].GlobalTransform * new Vector3(
        side == 0 ? -.28896f : .28896f, 0, -WeaponAim.MachineGunMuzzleDistance);

    internal void Observe(ItemSlot? inventory, IEnumerable<ItemEvent> events, ulong tick)
    {
        if (inventory is null)
        { Stop(); return; }
        if (tick <= _lastTick) { return; }
        _lastTick = tick;
        var shots = events.Where(shot => shot.Item == HeldItem.MachineGun && shot.Token == inventory.Active.Token).ToArray();
        if (shots.Length == 0 && (inventory.Active.Item != HeldItem.MachineGun || inventory.EngagedToken != inventory.Active.Token))
        { Stop(); return; }
        foreach (var shot in shots)
        {
            int side = shot.MachineGunBarrel;
            _pulse[side] = 1;
            _feed[side] = Math.Min(4, _feed[side] + 1);
            _heat[side] = Math.Min(1, _heat[side] + .036f);
            _shotAge[side] = 0;
        }
    }

    internal void Stop()
    {
        Array.Clear(_pulse); Array.Clear(_feed);
        foreach (var flash in _flashes) { flash.Visible = false; }
        foreach (var smoke in _smoke) { smoke.Emitting = false; smoke.Visible = false; }
        foreach (var gun in _guns) { gun.Position = Vector3.Zero; }
        Array.Fill(_shotAge, 10);
        Array.Clear(_heat);
    }

    internal void Present(float pitch, float delta, bool ready)
    {
        _pitch.Rotation = new(pitch, 0, 0);
        if (!ready) { Stop(); }
        var settings = MachineGunVfxSettings.Current;
        for (int side = 0; side < 2; side++)
        {
            _heat[side] = Math.Max(0, _heat[side] - delta * .3f);
            // Spread confirmed rounds across frames: advancing exactly one
            // identical cartridge per frame would make the belt look stationary.
            float advance = _feed[side] * (1 - MathF.Exp(-delta * 30));
            _feed[side] -= advance;
            _phase[side] = (_phase[side] + advance) % Links;
            _guns[side].Position = new(0, 0, .025f * _pulse[side]);
            _flashes[side].Visible = ready && _pulse[side] > .12f && _shotAge[side] < .045f && settings.FlashScale > 0;
            _flashes[side].Scale = Vector3.One * (.75f + _pulse[side] * .5f) * settings.FlashScale;
            _smoke[side].Visible = _smoke[side].Emitting = ready && _shotAge[side] < .06f && _heat[side] > .35f && settings.SmokeIntensity > 0;
            ((ParticleProcessMaterial)_smoke[side].ProcessMaterial).Color = new(1, 1, 1, settings.SmokeIntensity);
            // Render a newly confirmed pulse once even when a frame exceeds its
            // envelope; decay afterward so 30 FPS cannot erase every shot.
            _pulse[side] = Math.Max(0, _pulse[side] - delta * 32);
            _shotAge[side] += delta;
        }
        foreach (var (mesh, source, side) in _belts)
        {
            int sign = side == 0 ? -1 : 1;
            Vector3 start = new(sign * .1755f, -.592f, .169f);
            Vector3 end = _pitch.Transform * new Vector3(sign * .53f, .027f, -.2502f + _guns[side].Position.Z);
            _path[0] = start; _distance[0] = 0;
            for (int sample = 1; sample < _path.Length; sample++)
            {
                _path[sample] = FeedPoint(start, end, sign, sample / 64f);
                _distance[sample] = _distance[sample - 1] + _path[sample].DistanceTo(_path[sample - 1]);
            }
            for (int link = 0; link < Links; link++)
            {
                float along = (link + _phase[side] % 1) / Links * _distance[^1];
                int sample = 1;
                while (sample < 64 && _distance[sample] < along) { sample++; }
                float fraction = (along - _distance[sample - 1]) / Math.Max(.00001f, _distance[sample] - _distance[sample - 1]);
                Vector3 position = _path[sample - 1].Lerp(_path[sample], fraction);
                Vector3 tangent = (_path[sample] - _path[sample - 1]).Normalized();
                Vector3 across = Vector3.Right;
                Vector3 forward = across.Cross(tangent).Normalized();
                var basis = new Basis(tangent.Cross(forward).Normalized(), tangent, forward);
                mesh.SetInstanceTransform(link, new Transform3D(basis.Scaled(new(1.5f, 1, 1)), position) * source);
            }
        }
    }

    private Vector3 FeedPoint(Vector3 start, Vector3 end, int sign, float t)
    {
        // The concealed central feed throat passes inside the bearing housing,
        // keeping the visible loops clear of fixed rack rails at every yaw.
        Vector3 throat = new(sign * .13f, -.37f, .07f);
        return t < .4f ? Bezier(start, start + Vector3.Up * .09f, throat - Vector3.Up * .10f, throat, t / .4f) :
            Bezier(throat, new(sign * .60f, -.15f, -.20f), end + _pitch.Basis * new Vector3(sign * .075f, -.12f, -.08f), end, (t - .4f) / .6f);
    }

    private static Vector3 Bezier(Vector3 p, Vector3 a, Vector3 b, Vector3 q, float t)
    { float s = 1 - t; return s * s * s * p + 3 * s * s * t * a + 3 * s * t * t * b + t * t * t * q; }

    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            yield return child;
            foreach (Node descendant in Descendants(child)) { yield return descendant; }
        }
    }
}
