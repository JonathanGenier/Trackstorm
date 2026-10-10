using Godot;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Verification;

public sealed partial class WeaponAimChecks
{
    private async Task CheckAimLook()
    {
        var arena = _arenas[1];
        var host = _arenas[0].Driver.Host!;
        var settings = new Settings.PlayerSettingsController();
        settings.Initialize(_input.Adapter, System.IO.Path.Combine(_output, "look-settings.json")); AddChild(settings);
        arena.CameraSettings = settings;
        // Match the isolated recovery fixture: clear long sightlines above map
        // props, while native gravity and chassis collision remain active.
        float floor = Ground + 35;
        var platforms = new List<StaticBody3D>();
        foreach (var peer in _arenas)
        {
            var platform = new StaticBody3D { Position = new(0, floor - 1.65f, 0), CollisionLayer = 1 };
            platform.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(500, 1, 500) } });
            peer.AddChild(platform); platforms.Add(platform);
        }
        Vector3 Center() => arena.Bodies[1].VisualTransform * new Vector3(0, .3f, 0);
        foreach (float distance in new[] { 6f, 35f, 200f })
        foreach (int fps in new[] { 30, 60, 144 })
        {
            Engine.MaxFps = fps;
            float gain = fps == 30 ? .25f : fps == 60 ? 1 : 3;
            settings.UpdateSettings(new() { CameraFov = fps == 30 ? 50 : 90 });
            host.Items.RemovePlayer(Shooter);
            Position(new(0, floor, distance), new(0, floor, 0), new(100, floor, 0));
            _heldTargets = (new(0, floor, 0), new(100, floor, 0));
            host.Items.Grant(host.World, Shooter, HeldItem.MachineGun);
            Camera.ResetFollow(); await Frames(90); await AimAtCurrent(Center); await Frames(30);
            Require(arena.AssistedCar == 1, "Visible target acquired before measuring camera-input retention");
            settings.UpdateSettings(settings.Current with { MouseAimSensitivity = gain });
            var releases = new List<string>();
            arena.AimReleaseObserved = releases.Add;
            // A modest outward nudge, then its return. At high gain reduce physical
            // travel so this remains a correction rather than a large view sweep.
            float pixels = 16 / Math.Max(1, gain);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                Send(new InputEventMouseMotion { ScreenRelative = new(pixels, 0) }); await Frames(12);
                Send(new InputEventMouseMotion { ScreenRelative = new(-pixels, 0) }); await Frames(12);
            }
            GD.Print($"LOOK_CORRECTION distance={distance} fps={fps} gain={gain} target={arena.AssistedCar} releases={string.Join(';', releases)}");
            Require(arena.AssistedCar == 1 && releases.Count == 0, "Repeated modest outward/return camera movements retain the same car");
            await Frames(30);
            // Slow outward placement reaches about four degrees, beyond the old
            // 3.25-degree departure boundary, without a fast physical sweep.
            for (int step = 0; step < 24; step++)
            {
                Send(new InputEventMouseMotion { ScreenRelative = new(1 / gain, 0) }); await Frames(4);
            }
            float error = Camera.ProjectRayNormal(ViewCenter).AngleTo(Center() - Camera.GlobalPosition);
            GD.Print($"LOOK_PLACEMENT distance={distance} fps={fps} error={error:F5} releases={string.Join(';', releases)}");
            Require(arena.AssistedCar == 1 && releases.Count == 0 && error > .05f && error < .085f,
                "Slow deliberate camera placement around four degrees retains useful engagement");
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false }); await Frames(2);
            Require(arena.AssistedCar == 0, "RMB release overrides the more forgiving look tolerance immediately");
            settings.UpdateSettings(new()); await AimAtCurrent(Center); await Frames(30);
            Send(new InputEventMouseMotion { ScreenRelative = new(70, 0) }); await Frames(8);
            Require(arena.AssistedCar == 0, "Clear outward sweep still releases promptly");
            await Frames(30);
            Require(arena.AssistedCar == 0, "Stopping after a deliberate sweep does not reacquire the dismissed car");
            arena.AimReleaseObserved = null;
        }
        _heldTargets = null; arena.CameraSettings = null; settings.QueueFree();
        foreach (var platform in platforms) { platform.QueueFree(); }
    }
}
