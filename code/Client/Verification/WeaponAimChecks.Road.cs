using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class WeaponAimChecks
{
    // The shooter traverses the authored southern rhythm terrain, without added
    // collision, pose corrections or impulses. Only the rival follows a fixture path.
    private async Task CheckAimRoad()
    {
        var arena = _arenas[1];
        var host = _arenas[0].Driver.Host!;
        var settings = new Settings.PlayerSettingsController();
        settings.Initialize(_input.Adapter, System.IO.Path.Combine(_output, "road-settings.json")); AddChild(settings);
        arena.CameraSettings = settings;
        Vector3 Center() => arena.Bodies[1].VisualTransform * new Vector3(0, .3f, 0);
        N.Vector3 GroundAt(float x, float z)
        {
            using var query = PhysicsRayQueryParameters3D.Create(new(x, 30, z), new(x, -20, z), 1);
            using var hit = arena.GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (hit.Count == 0) { throw new InvalidOperationException($"No production terrain at {x},{z}"); }
            return VehicleBody.ToCore(hit["position"].AsVector3()) + N.Vector3.UnitY * VehicleDimensions.RideHeight;
        }
        foreach (float distance in new[] { 8f, 35f, 200f })
        foreach (int mode in OS.GetCmdlineUserArgs().Contains("--road-corrections") ? new[] { 1 } : new[] { 0, 1, 2 })
        {
            Engine.MaxFps = mode == 0 ? 30 : mode == 1 ? 60 : 144;
            settings.UpdateSettings(new() { CameraFov = mode == 0 ? 50 : 90 });
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            host.Items.RemovePlayer(Shooter);
            var start = GroundAt(-120, 48);
            Position(start, GroundAt(-120 + distance, 48), GroundAt(150, 65));
            var world = host.World.State;
            var pose = new VehiclePhysicsState(start, N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, -MathF.PI / 2), N.Vector3.Zero, N.Vector3.Zero);
            host.World.Restore(new(world.Tick, world.LastInput, world.Vehicles.Select(v => v.VehicleId != Shooter ? v :
                new VehicleSnapshot(v.VehicleId, v.LifeId, new(world.Tick, pose, true, false, 0, 0), v.Damage, pose)), world.Match));
            _arenas[0].Bodies[Shooter].Apply(pose);
            _targetOrientation = pose.Orientation;
            _heldTargets = (GroundAt(-120 + distance, 48), GroundAt(150, 65));
            host.Items.Grant(host.World, Shooter, HeldItem.MachineGun);
            Camera.ResetFollow(); await Frames(150);
            float cone = host.Items.Configuration.Aim.AssistDegrees;
            host.TryConfigure(0, new Dictionary<string, double> { ["items.aim_assist_degrees"] = 0 }, out _); await Frames(30);
            await AimAtCurrent(() => Center() + Vector3.Up * Math.Max(1.2f, distance * .04f)); await Frames(20);
            float initialError = Camera.ProjectRayNormal(ViewCenter).AngleTo(Center() - Camera.GlobalPosition);
            var acquisitionClock = System.Diagnostics.Stopwatch.StartNew();
            host.TryConfigure(0, new Dictionary<string, double> { ["items.aim_assist_degrees"] = cone }, out _);
            int acquisitionTicks = 0;
            while (arena.AssistedCar != 1 && acquisitionTicks < 120) { await Frames(1); acquisitionTicks++; }
            GD.Print($"ROAD_ACQUIRE distance={distance} mode={mode} measured={arena.Bodies[Shooter].VisualPosition.DistanceTo(arena.Bodies[1].VisualPosition):F3}m initialError={initialError:F5}rad ticks={acquisitionTicks} elapsed={acquisitionClock.Elapsed.TotalSeconds:F3}s");
            Require(arena.AssistedCar == 1, "Near-body cursor miss acquires without corrective input on production terrain");
            await Frames(90);
            settings.UpdateSettings(settings.Current with { MouseAimSensitivity = mode == 0 ? .25 : 1, StickAimSensitivity = mode == 2 ? 3 : 1 });
            if (mode == 2)
            {
                Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
                Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = .16f }); await Frames(2);
                Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0 }); await Frames(30);
            }
            var releases = new List<string>();
            arena.AimReleaseObserved = reason =>
            {
                releases.Add(reason);
                string geometry = arena.AssistedCar != 0 ? arena.AimTargetDiagnostics(arena.AssistedCar) : "capability ended";
                GD.Print($"ROAD_RELEASE distance={distance} mode={mode} {reason} {geometry}");
            };
            int retained = 0, acquiredAt = -1;
            float minY = float.MaxValue, maxY = float.MinValue, maxHeave = 0, maxError = 0;
            var errors = new List<float>();
            var acceptedErrors = new List<float>();
            var readyErrors = new List<float>();
            int restricted = 0;
            for (int tick = 0; tick < 360; tick++)
            {
                var state = host.World.GetVehicle(Shooter);
                _heldTargets = (GroundAt(state.ObservedPhysics.Position.X + distance, 48), GroundAt(150, 65));
                if (mode == 1 && tick % 24 == 0 && tick < 288) { Send(new InputEventMouseMotion { ScreenRelative = new(0, tick < 144 ? 1.5f : -1.5f) }); }
                await Frames(1, throttle: 20000);
                float y = state.ObservedPhysics.Position.Y;
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                maxHeave = Math.Max(maxHeave, Math.Abs(state.ObservedPhysics.LinearVelocity.Y));
                float error = Camera.ProjectRayNormal(ViewCenter).AngleTo(Center() - Camera.GlobalPosition);
                errors.Add(error); maxError = Math.Max(maxError, error);
                if (host.Items.Aims.FirstOrDefault(a => a.Vehicle == Shooter) is { } accepted)
                {
                    var to = N.Vector3.Normalize(VehicleBody.ToCore(Center()) - accepted.Origin);
                    acceptedErrors.Add(MathF.Acos(Math.Clamp(N.Vector3.Dot(to, accepted.Direction), -1, 1)));
                    if (accepted.Ready) { readyErrors.Add(acceptedErrors[^1]); }
                    else { restricted++; }
                    RequireShot(!WeaponAim.IntersectsBody(WeaponAim.Pivot, WeaponAim.Direction(accepted.Yaw, accepted.Pitch)), "Road tracking preserves authoritative self-clearance");
                }
                if (arena.AssistedCar == 1) { retained++; if (acquiredAt < 0) { acquiredAt = tick; } }
            }
            var end = host.World.GetVehicle(Shooter);
            float readyError = readyErrors.Count == 0 ? 0 : readyErrors.TakeLast(60).Average();
            GD.Print($"ROAD distance={distance} mode={mode} fps={Engine.MaxFps} fov={Camera.Fov} acquiredTick={acquiredAt} retained={retained}/360 releases={releases.Count} reasons={string.Join(';', releases)} travel={end.ObservedPhysics.Position.X - start.X:F3}m rise={maxY-minY:F3}m heave={maxHeave:F3}m/s maxError={maxError:F5} finalMeanError={errors.TakeLast(60).Average():F5}rad acceptedMeanError={acceptedErrors.TakeLast(60).Average():F5}rad readySamples={readyErrors.Count} restricted={restricted} readyMeanError={readyError:F5}rad");
            Require(end.ObservedPhysics.Position.X - start.X > 15 && maxY-minY > .1f && maxHeave > .1f, "Shooter drove actual production elevation changes");
            Require(retained == 360 && releases.Count == 0, "Visible moving rival survives repeated authored rollers and small corrections without replacement");
            Require(errors.TakeLast(60).Average() < .006f && readyError < .03f,
                "Camera and unrestricted accepted aim recover alignment; self-blocked close aim remains restricted");
            await Capture($"road-{distance}-{mode}");
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            // Capture can leave catch-up physics ticks before camera presentation.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Frames(3);
            Require(arena.AssistedCar == 0, "Release clears immediately after rough-road retention");
            arena.AimReleaseObserved = null;
        }
        _heldTargets = null; _targetOrientation = N.Quaternion.Identity;
        arena.CameraSettings = null; settings.QueueFree(); Engine.MaxFps = 30;
    }
}
