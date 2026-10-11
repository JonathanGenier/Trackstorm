using Godot;
using Trackstorm.Core.Items;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class WeaponAimChecks
{
    private async Task CheckStickyEngagement()
    {
        var host = _arenas[0].Driver.Host!;
        host.Items.RemovePlayer(Shooter);
        Position(new(0, Ground, 35), new(0, Ground, 0), new(70, Ground, 0));
        host.Items.Grant(host.World, Shooter, HeldItem.MachineGun);
        host.Items.Grant(host.World, Shooter, HeldItem.Missile);
        _heldTargets = (new(0, Ground, 0), new(70, Ground, 0));
        Camera.ResetFollow(); await Frames(180);
        Vector3 Center() => _arenas[1].Bodies[1].VisualTransform * new Vector3(0, .3f, 0);
        await AimAtCurrent(Center); await Frames(30);
        Require(_arenas[1].AssistedCar == 1, "Sticky acquisition selects a visible living rival car");
        var tuning = host.Items.Configuration.Aim;
        host.TryConfigure(0, new Dictionary<string, double> { ["items.aim_mouse_pull"] = 0, ["items.aim_stick_pull"] = 0 }, out _);
        await Frames(30);
        _heldTargets = (new(2, Ground, 0), new(70, Ground, 0)); await Frames(90);
        float withoutPull = Camera.ProjectRayNormal(ViewCenter).AngleTo(Center() - Camera.GlobalPosition);
        host.TryConfigure(0, new Dictionary<string, double> { ["items.aim_mouse_pull"] = tuning.MousePull, ["items.aim_stick_pull"] = tuning.StickPull }, out _);
        _heldTargets = (new(0, Ground, 0), new(70, Ground, 0)); await Frames(90);
        Camera.ResetFollow(); await Frames(45); await AimAtCurrent(Center);
        _heldTargets = (new(2, Ground, 0), new(70, Ground, 0));
        await Frames(12);
        float initialError = Camera.ProjectRayNormal(ViewCenter).AngleTo(Center() - Camera.GlobalPosition);
        await Frames(90);
        float settled = Camera.ProjectRayNormal(ViewCenter).AngleTo(Center() - Camera.GlobalPosition);
        Require(_arenas[1].AssistedCar == 1 && settled < .012f && settled < initialError * .6f,
            $"Camera attraction follows a displaced car without input/recentering: {initialError:F5} -> {settled:F5} rad");
        Require(settled < withoutPull * .2f, $"Attraction improves retention alignment over disabled pull ({withoutPull:F5} rad unassisted)");
        for (int i = 0; i < 90; i++)
        {
            _heldTargets = (new(2 + i * .04f, Ground, 0), new(70, Ground, 0));
            if (i % 4 == 0) { Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true }); Send(new InputEventMouseMotion { ScreenRelative = new(i % 8 == 0 ? -1 : 1, 0) }); }
            await Frames(1);
            RequireShot(_arenas[1].AssistedCar == 1 && _arenas[1].AimOverlay.Bounds.HasValue, "Small-error moving target retention has no bracket flicker");
        }
        await Capture("sticky-moving-car");
        Require(true, "Moving target retained through 90 frames of small mouse errors");
        var local = _arenas[1].Driver.LocalState!;
        var pivot = local.ObservedPhysics.Position + N.Vector3.Transform(WeaponAim.Pivot, local.ObservedPhysics.Orientation);
        var expected = N.Vector3.Normalize(Vehicles.VehicleBody.ToCore(Center()) - pivot);
        Require(N.Vector3.Dot(expected, _arenas[1].Driver.DesiredAim!.Value) > .999f, "Small alternating corrections retain aim near the moving body centre");

        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        float beforeBreakaway = Camera.Rotation.Y;
        Send(new InputEventMouseMotion { ScreenRelative = new(-70, 0) }); await Frames(4);
        // PNG capture can leave several catch-up physics ticks before any render follow.
        // Observe two process boundaries so the production camera actually consumes input.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print($"STICKY_BREAK mouse yaw={beforeBreakaway}->{Camera.Rotation.Y} held={_input.Adapter.MouseLookHeld} target={_arenas[1].AssistedCar} bounds={_arenas[1].AimOverlay.Bounds}");
        Require(_arenas[1].AssistedCar == 0 && !_arenas[1].AimOverlay.Bounds.HasValue, "Deliberate mouse breakaway promptly restores free aim");
        int freeShots = 0;
        var raycast = _arenas[0].Driver.RaycastWeapon!;
        _arenas[0].Driver.RaycastWeapon = (owner, start, end) =>
        {
            if (owner == Shooter)
            {
                var accepted = host.Items.Aims.Single(a => a.Vehicle == Shooter);
                RequireShot(N.Vector3.Dot(N.Vector3.Normalize(end - start), accepted.Direction) > MathF.Cos(host.Items.Configuration.MachineGunSpread * MathF.PI / 180) - .00001f,
                    "Free fire retains configured authoritative spread");
                freeShots++;
            }
            return raycast(owner, start, end);
        };
        _arenas[1].Driver.RequestItemUse(); _firingPeers.Add(Shooter); await Frames(30);
        _firingPeers.Clear(); await Frames(30); _arenas[0].Driver.RaycastWeapon = raycast;
        Require(freeShots > 10 && _arenas[1].AssistedCar == 0, $"Unframed cursor-directed free fire produces actual rounds ({freeShots})");
        await AimAtCurrent(Center); await Frames(30);
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = -.3f }); await Frames(8);
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0 }); await Frames(30);
        Require(_arenas[1].AssistedCar == 1, "Small controller corrections retain the same framed car");
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = -1 }); await Frames(8);
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0 });
        Require(_arenas[1].AssistedCar == 0, "Full controller aim-away releases without trapping input");

        await AimAtCurrent(Center); await Frames(30);
        for (int i = 0; i < 60; i++)
        {
            // A second car crosses behind the retained car; proximity must not steal selection.
            _heldTargets = (new(5.56f, Ground, 0), new(5.56f + MathF.Sin(i * .1f) * 3, Ground, -6));
            await Frames(1);
            RequireShot(_arenas[1].AssistedCar == 1, "Nearby crossing rival cannot steal a still-visible retained car");
        }
        Require(true, "Crossing rear car does not cause target switching or bracket flicker");
        _heldTargets = (new(5.56f, Ground, 0), new(3.5f, Ground, 14)); await Frames(30);
        Require(_arenas[1].AssistedCar != 1, "Intervening car breaks retention of the covered car");
        _heldTargets = (new(5.56f, Ground, 0), new(70, Ground, 0)); await Frames(30);
        await AimAtCurrent(Center);
        var cover = new StaticBody3D { Position = Camera.GlobalPosition.Lerp(Center(), .65f), CollisionLayer = 1 };
        cover.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(6, 6, 1) } });
        _arenas[1].AddChild(cover); await Frames(8);
        Require(_arenas[1].AssistedCar == 0 && !_arenas[1].AimOverlay.Bounds.HasValue, "Solid non-vehicle cover releases assistance and never acquires brackets");
        cover.Free(); await Frames(20); await AimAtCurrent(Center);
        await CheckStickyPreferences();
        await CheckAcceptedAimOutage();
        await CheckNonVehicleTargets();
        _input.Adapter.GameplaySuppressed = true; await Frames(8);
        Require(_arenas[1].AssistedCar == 0 && !_arenas[1].AimOverlay.Marker.HasValue, "Suppression retires acquired identity and HUD");
        _input.Adapter.GameplaySuppressed = false;
        _heldTargets = null;
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        Camera.ResetFollow(); await Frames(30);
    }

    private async Task CheckStickyPreferences()
    {
        var settings = new Settings.PlayerSettingsController();
        settings.Initialize(_input.Adapter, System.IO.Path.Combine(_output, "sticky-settings.json")); AddChild(settings);
        _arenas[1].CameraSettings = settings;
        foreach (int fps in new[] { 30, 60, 144 })
        foreach (double gain in new[] { .25, 3.0 })
        {
            Engine.MaxFps = fps;
            settings.UpdateSettings(new());
            Position(new(0, Ground, 35), new(0, Ground, 0), new(70, Ground, 0));
            _heldTargets = (new(0, Ground, 0), new(70, Ground, 0));
            Camera.ResetFollow(); await Frames(45);
            await AimAtCurrent(() => _arenas[1].Bodies[1].VisualTransform * new Vector3(0, .3f, 0));
            settings.UpdateSettings(settings.Current with { HorizontalLookSensitivity = gain, MouseAimSensitivity = gain,
                StickAimSensitivity = gain, CameraRecenterSpeed = 3, CameraFov = gain < 1 ? 50 : 90, InvertY = gain > 1 });
            _heldTargets = (new(2, Ground, 0), new(70, Ground, 0)); await Frames(75);
            float error = Camera.ProjectRayNormal(ViewCenter).AngleTo(_arenas[1].Bodies[1].VisualTransform * new Vector3(0, .3f, 0) - Camera.GlobalPosition);
            Require(_arenas[1].AssistedCar == 1 && error < .015f,
                $"Retained moving-car aim survives {fps} FPS, gain {gain}, FOV {Camera.Fov}, recenter 3: error={error:F5} rad");
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false }); await Frames(4);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_arenas[1].AssistedCar == 0 && !_arenas[1].AimOverlay.Bounds.HasValue,
                $"RMB release clears assisted feedback at {fps} FPS, gain {gain}, FOV {Camera.Fov}");
            // Existing right-stick intent acquires independently of the released mouse.
            Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = .16f }); await Frames(2);
            Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0 }); await Frames(45);
            Require(!_input.Adapter.MouseLookHeld && _arenas[1].AssistedCar == 1, "Controller camera intent retains assistance without physical RMB");
            Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = -1 });
            await Frames(8);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_arenas[1].AssistedCar == 0, $"Full controller breakaway remains immediate at {fps} FPS and sensitivity gain {gain}");
            await Frames(30);
            Require(_arenas[1].AssistedCar == 0, "Continuing deliberate input cannot repeatedly reacquire the car");
            Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0 });
            settings.UpdateSettings(new());
            await AimAtCurrent(() => _arenas[1].Bodies[1].VisualTransform * new Vector3(0, .3f, 0));
            settings.UpdateSettings(settings.Current with { HorizontalLookSensitivity = gain, MouseAimSensitivity = gain });
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            Send(new InputEventMouseMotion { ScreenRelative = new(-30, 0) });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_arenas[1].AssistedCar == 0, $"Mouse sweep breakaway remains immediate at {fps} FPS and sensitivity gain {gain}");
        }
        Engine.MaxFps = 30;
        settings.UpdateSettings(new());
        await AimAtCurrent(() => _arenas[1].Bodies[1].VisualTransform * new Vector3(0, .3f, 0)); await Frames(20);
        _arenas[1].CameraSettings = null; settings.QueueFree();
    }

    private async Task CheckNonVehicleTargets()
    {
        var host = _arenas[0].Driver.Host!;
        var passing = new Items.ItemPresentation(); _arenas[1].AddChild(passing);
        for (int i = 0; i < 30; i++)
        {
            Vector3 across = Camera.GlobalPosition + Camera.ProjectRayNormal(ViewCenter) * 18 + Camera.GlobalBasis.X * ((i - 15) * .12f);
            passing.Apply(new ItemPublication((ulong)i + 1, host.Snapshot(), [],
                [new MissileState(99000, 1, Vehicles.VehicleBody.ToCore(across), new N.Vector3(10, 0, 0), 120)], []));
            await Frames(1);
            RequireShot(_arenas[1].AssistedCar == 1, "Passing missile cannot steal acquired car assistance");
        }
        passing.Free();
        Require(true, "Production missile crossing an engaged view never replaces the retained car");
        _heldTargets = (new(75, Ground, 0), new(-75, Ground, 0));
        Camera.ResetFollow(); await Frames(30);
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        Vector3 point = Camera.GlobalPosition + Camera.ProjectRayNormal(ViewCenter) * 18;
        var visuals = new Items.ItemPresentation(); _arenas[1].AddChild(visuals);
        var missile = new MissileState(99001, 1, Vehicles.VehicleBody.ToCore(point), new N.Vector3(0, 0, -20), 120);
        visuals.Apply(new ItemPublication(1, host.Snapshot(), [], [missile], []));
        await Frames(15);
        Require(_arenas[1].AssistedCar == 0 && !_arenas[1].AimOverlay.Bounds.HasValue, "Production missile presentation under cursor never becomes an assisted target");
        var mine = new ProxyMineState(99002, 1, Vehicles.VehicleBody.ToCore(point), N.Vector3.Zero, N.Vector3.UnitY, 0);
        visuals.Apply(new ItemPublication(2, host.Snapshot(), [], [], [], mines: [mine])); await Frames(15);
        Require(_arenas[1].AssistedCar == 0 && !_arenas[1].AimOverlay.Bounds.HasValue, "Production mine presentation under cursor never becomes an assisted target");
        visuals.Free();
        var rock = Networking.MatchResourceLoader.LoadResource<PackedScene>("res://assets/environment/models/RockCluster.glb").Instantiate<Node3D>();
        _arenas[1].AddChild(rock); rock.GlobalPosition = point; await Frames(15);
        Require(_arenas[1].AssistedCar == 0 && !_arenas[1].AimOverlay.Bounds.HasValue, "Production rock artwork never becomes an assisted target");
        rock.Free();
        var pickup = (Node3D)_arenas[1].Pickups.GetChildren().OfType<Node3D>().First().Duplicate();
        _arenas[1].AddChild(pickup); pickup.GlobalPosition = point; pickup.Show();
        foreach (var child in pickup.GetChildren().OfType<Node3D>()) { child.Show(); }
        await Frames(15);
        Require(_arenas[1].AssistedCar == 0 && !_arenas[1].AimOverlay.Bounds.HasValue, "Production pickup presentation never becomes an assisted target");
        pickup.Free();
        RequireCentered("Non-vehicle objects retain the centred ordinary cursor");
    }
}
