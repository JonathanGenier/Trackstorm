using Godot;
using Trackstorm.Core.Items;
using Trackstorm.Client.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class WeaponAimChecks
{
    private async Task CheckAccuracy()
    {
        var host = _arenas[0].Driver.Host!;
        var settings = new Settings.PlayerSettingsController();
        settings.Initialize(_input.Adapter, System.IO.Path.Combine(_output, "accuracy-settings.json")); AddChild(settings);
        _arenas[1].CameraSettings = settings;
        var original = host.Items.Configuration;
        // A larger fixture magazine collects 30 seconds of unchanged 80-round/s fire.
        Require(host.TryConfigure(0, new Dictionary<string, double> { ["items.machine_gun_capacity"] = 2400 }, out _), "Accuracy sample magazine configured");
        var nativeRay = _arenas[0].Driver.RaycastWeapon!;
        foreach (float yaw in new[] { 0f, 45f, 90f })
        foreach (float fov in new[] { 50f, 90f })
        {
            host.Items.RemovePlayer(Shooter);
            settings.UpdateSettings(settings.Current with { CameraFov = fov });
            _heldShooter = new(0, Ground + 35, 110);
            N.Vector3 target = _heldShooter.Value + new N.Vector3(0, 0, WeaponAim.Pivot.Z - MathF.Sqrt(40000 - 1.51f * 1.51f));
            _heldTargets = (target, new(100, Ground + 35, 0));
            _targetOrientation = N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, Mathf.DegToRad(yaw));
            Position(_heldShooter.Value, target, _heldTargets.Value.Second);
            host.Items.Grant(host.World, Shooter, HeldItem.MachineGun);
            Camera.ResetFollow(); await Frames(180);
            await AimAtCurrent(() => _arenas[1].Bodies[1].VisualTransform * new Vector3(0, .3f, 0));
            await Frames(90);
            Require(_arenas[1].AssistedCar == 1 && _arenas[1].AimOverlay.Bounds.HasValue, $"200m car acquired: yaw={yaw}, FOV={fov}");
            int shots = 0, inside = 0, collisions = 0;
            float minimumDistance = float.MaxValue, maximumDistance = 0;
            _arenas[0].Driver.RaycastWeapon = (owner, start, end) =>
            {
                var hit = nativeRay(owner, start, end);
                if (owner != Shooter) { return hit; }
                var car = _arenas[1].Bodies[1];
                Vector3 center = car.VisualTransform * new Vector3(0, .3f, 0);
                Vector3 origin = VehicleBody.ToGodot(start), direction = VehicleBody.ToGodot(N.Vector3.Normalize(end - start));
                float distance = origin.DistanceTo(center);
                minimumDistance = Math.Min(minimumDistance, distance); maximumDistance = Math.Max(maximumDistance, distance);
                Vector3 planeNormal = -Camera.GlobalBasis.Z;
                Vector3 point = origin + direction * ((center - origin).Dot(planeNormal) / direction.Dot(planeNormal));
                RequireShot(_arenas[1].AssistedCar == 1 && _arenas[1].AimOverlay.Bounds.HasValue, "Every accuracy shot has a valid framed car");
                if (_arenas[1].AimOverlay.Bounds!.Value.HasPoint(Camera.UnprojectPosition(point))) { inside++; }
                if (hit?.Vehicle == 1) { collisions++; }
                shots++;
                return hit;
            };
            Require(_arenas[1].Driver.RequestItemUse(), "Accuracy fire uses ordinary remote sustained-use request");
            _firingPeers.Add(Shooter);
            await Until(() => shots >= 1200, "Half-magazine actual-ray sample collected", 1400);
            await Capture($"accuracy-yaw-{yaw}-fov-{fov}");
            await Until(() => shots == 2400, "Complete authoritative magazine sampled (including non-tracers)", 2600);
            _firingPeers.Clear(); await Frames(45);
            float percent = inside * 100f / shots;
            GD.Print($"ACCURACY yaw={yaw} fov={fov} shots={shots} inside={inside} outside={shots - inside} percent={percent:F3} nativeVehicleHits={collisions} distance={minimumDistance:F3}..{maximumDistance:F3}m spread={host.Items.Configuration.MachineGunSpread}deg");
            Require(percent is >= 76 and <= 84 && minimumDistance > 199.8f && maximumDistance < 200.2f,
                $"200m grouping within statistical tolerance: {inside}/{shots} ({percent:F2}%) yaw={yaw} FOV={fov}; collision count {collisions} is separate");
            _arenas[0].Driver.RaycastWeapon = nativeRay;
        }
        host.TryConfigure(0, new Dictionary<string, double> { ["items.machine_gun_capacity"] = original.MachineGunCapacity }, out _);
        _heldShooter = null; _heldTargets = null; _targetOrientation = N.Quaternion.Identity;
        _arenas[1].CameraSettings = null; settings.QueueFree();
    }
}
