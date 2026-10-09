using Godot;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class WeaponAimChecks
{
    private async Task CheckAimRecovery()
    {
        var arena = _arenas[1];
        var host = _arenas[0].Driver.Host!;
        var losses = new List<string>();
        var settings = new Settings.PlayerSettingsController();
        settings.Initialize(_input.Adapter, System.IO.Path.Combine(_output, "recovery-settings.json")); AddChild(settings);
        arena.CameraSettings = settings;
        // Clear long sightlines above both maps' authored walls/bridge; native physics
        // still supplies flight, steering and landing on this explicitly isolated floor.
        float floor = Ground + 35;
        var platforms = new List<StaticBody3D>();
        foreach (var peer in _arenas)
        {
            var platform = new StaticBody3D { Position = new(0, floor - 1.65f, 0), CollisionLayer = 1 };
            platform.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(500, 1, 500) } });
            platform.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(500, 1, 500) } });
            peer.AddChild(platform); platforms.Add(platform);
        }
        arena.AimReleaseObserved = reason =>
        {
            losses.Add(reason);
            var shooter = host.World.GetVehicle(Shooter);
            GD.Print($"AIM_RELEASE {reason} shooterHP={shooter.Damage.CurrentHP:F2} outside={shooter.OutOfBounds} lastDamage={shooter.Damage.LastDamage?.Attribution.Source}");
        };
        float cone = host.Items.Configuration.Aim.AssistDegrees;
        void Assist(bool enabled) => host.TryConfigure(0, new Dictionary<string, double> { ["items.aim_assist_degrees"] = enabled ? cone : 0 }, out _);
        Vector3 Center() => arena.Bodies[1].VisualTransform * new Vector3(0, .3f, 0);
        int acquired = 0, cases = 0, unexpected = 0;
        foreach (float distance in OS.GetCmdlineUserArgs().Contains("--recovery-far") ? new[] { 200f } : new[] { 6f, 35f, 200f })
        foreach (float fov in new[] { 50f, 90f })
        {
            Engine.MaxFps = fov == 50 ? 30 : 144;
            settings.UpdateSettings(new() { CameraFov = fov });
            host.Items.RemovePlayer(Shooter);
            Position(new(0, floor, distance), new(0, floor, 0), new(100, floor, 0));
            _heldTargets = (new(0, floor, 0), new(100, floor, 0));
            _targetOrientation = N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, MathF.PI / 2);
            host.Items.Grant(host.World, Shooter, HeldItem.MachineGun);
            Assist(false); Camera.ResetFollow(); await Frames(150);
            // A broadside close body edge; far aim starts 3.5 degrees beside its centre.
            float offset = distance == 6 ? 2.4f : distance == 35 ? 3.2f : 13f;
            await AimAtCurrent(() => Center() + Vector3.Right * offset); await Frames(30);
            float measured = arena.Bodies[Shooter].VisualPosition.DistanceTo(arena.Bodies[1].VisualPosition);
            float error = Camera.ProjectRayNormal(ViewCenter).AngleTo(Center() - Camera.GlobalPosition);
            settings.UpdateSettings(settings.Current with { MouseAimSensitivity = fov == 50 ? .25 : 3, StickAimSensitivity = fov == 50 ? .25 : 3 });
            Assist(true); await Frames(100);
            cases++; if (arena.AssistedCar == 1) { acquired++; }
            GD.Print($"ACQUISITION distance={measured:F3}m FOV={fov} fps={Engine.MaxFps} gain={settings.Current.MouseAimSensitivity} offset={offset:F2}m initialError={error:F5} acquired={arena.AssistedCar} {arena.AimTargetDiagnostics(1)}");
            // Establish centre aim independently so failed acquisition cannot hide retention defects.
            settings.UpdateSettings(settings.Current with { MouseAimSensitivity = 1 });
            await AimAtCurrent(Center); await Frames(45); losses.Clear();
            Require(arena.AssistedCar == 1, "Visible centre target established before retention measurement");
            GD.Print($"JOLT_START {arena.AimTargetDiagnostics(1)} acquired={arena.AssistedCar}");
            int retained = 0;
            for (int i = 0; i < 180; i++)
            {
                if (i % 45 == 0)
                {
                    // Real native flight and landing, initialized through a coherent authority observation.
                    var state = host.World.GetVehicle(Shooter);
                    var launch = new VehiclePhysicsState(state.ObservedPhysics.Position, state.ObservedPhysics.Orientation,
                        new N.Vector3(i % 90 == 0 ? 4 : -4, 7, 0), N.Vector3.Zero);
                    var world = host.World.State;
                    host.World.Restore(new(world.Tick, world.LastInput, world.Vehicles.Select(v => v.VehicleId != Shooter ? v :
                        new VehicleSnapshot(v.VehicleId, v.LifeId, new(world.Tick, launch, false, false, 0, 0), v.Damage, launch)), world.Match));
                    _arenas[0].Bodies[Shooter].Apply(launch);
                    Camera.Motion.Impulse(1);
                    settings.UpdateSettings(settings.Current with { CameraHeight = i % 90 == 0 ? 3 : 1.25 });
                }
                _heldTargets = (new(MathF.Sin(i * .035f) * .4f, floor, 0), new(100, floor, 0));
                await Frames(1, throttle: 12000, steering: 5000);
                if (arena.AssistedCar == 1) { retained++; }
            }
            int angular = losses.Count(reason => reason.Contains("angular"));
            unexpected += angular;
            GD.Print($"JOLTS distance={distance} retained={retained}/180 angularLosses={angular} releases={string.Join(';', losses)}");
            Require(retained == 180 && losses.Count == 0, "Repeated visible native/camera jolts do not drop or replace the acquired car");
            await Capture($"recovery-{distance}-{fov}");
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false }); await Frames(8);
            Require(arena.AssistedCar == 0, "RMB release bypasses all recovery tolerance");
            await AimAtCurrent(Center);
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = .16f }); await Frames(2);
            Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0 }); await Frames(45);
            Require(arena.AssistedCar == 1, "Controller acquires after the mouse releases without a new binding");
            var cover = new StaticBody3D { Position = Camera.GlobalPosition.Lerp(Center(), .7f), CollisionLayer = 1 };
            cover.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(4, 4, 1) } }); arena.AddChild(cover);
            await Frames(3);
            Require(arena.AssistedCar == 0 && losses.Any(reason => reason.Contains("occluded")), "Real native cover releases immediately, independently of framing recovery");
            cover.Free();
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        }
        settings.UpdateSettings(new()); Engine.MaxFps = 60;
        Position(new(0, floor, 35), new(0, floor, 0), new(100, floor, 0));
        _heldTargets = (new(0, floor, 0), new(100, floor, 0));
        Camera.ResetFollow(); await Frames(90); await AimAtCurrent(Center);
        for (int i = 0; i < 4; i++) { Send(new InputEventMouseMotion { ScreenRelative = new(1, 0) }); await Frames(4); }
        await Frames(30);
        float placement = Camera.ProjectRayNormal(ViewCenter).AngleTo(Center() - Camera.GlobalPosition);
        Camera.ViewDownAngle = 13.5f; await Frames(4);
        Require(arena.AssistedCar == 1 && arena.AimRecoverySeconds > 0, "Visible angular displacement enters recovery rather than discarding the chosen fine placement");
        Send(new InputEventMouseMotion { ScreenRelative = new(.6f, 0) });
        await Frames(60);
        float restored = Camera.ProjectRayNormal(ViewCenter).AngleTo(Center() - Camera.GlobalPosition);
        Require(arena.AssistedCar == 1 && arena.AimRecoverySeconds == 0 && Math.Abs(restored - placement) < .006f,
            $"Bounded recovery preserves a small intentional correction: offset={placement:F5}->{restored:F5}rad");
        Camera.ViewDownAngle = 3.5f; await Frames(4);
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false }); await Frames(2);
        Require(arena.AssistedCar == 0, "RMB release is immediate during angular recovery");
        await AimAtCurrent(Center); await Frames(30); losses.Clear();
        float pull = host.Items.Configuration.Aim.MousePull;
        host.TryConfigure(0, new Dictionary<string, double> { ["items.aim_mouse_pull"] = 0 }, out _);
        await Frames(30); Camera.ViewDownAngle = 13.5f; await Frames(45);
        Require(arena.AssistedCar == 0 && losses.Any(reason => reason.Contains("angular recovery exhausted")), "Unrecoverable visible angular error expires instead of creating permanent lock-on");
        host.TryConfigure(0, new Dictionary<string, double> { ["items.aim_mouse_pull"] = pull }, out _);
        Camera.ViewDownAngle = 3.5f;
        _heldTargets = null; _targetOrientation = N.Quaternion.Identity;
        arena.AimReleaseObserved = null;
        arena.CameraSettings = null; settings.QueueFree(); Engine.MaxFps = 30;
        foreach (var platform in platforms) { platform.Free(); }
        Require(acquired == cases && unexpected == 0, $"Body-aware acquisition {acquired}/{cases}; unintended angular releases={unexpected}");
    }
}
