using Godot;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Verification;

public sealed partial class WeaponAimChecks
{
    private async Task CheckHeldAimFlight()
    {
        var host = _arenas[0].Driver.Host!;
        // Isolate visible-target retention from the oval bridge roof. Cover release is
        // exercised separately; this landing floor stays above production obstructions.
        float floor = _oval ? Ground + 30 : Ground;
        var platforms = new List<StaticBody3D>();
        if (_oval)
        {
            foreach (var arena in _arenas)
            {
                var platform = new StaticBody3D { Position = new(0, floor - 1.65f, 0), CollisionLayer = 1 };
                platform.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(200, 1, 200) } });
                arena.AddChild(platform); platforms.Add(platform);
            }
        }
        Position(new(0, floor, 35), new(0, floor, -25), new(70, floor, -25));
        _heldTargets = (new(0, floor, -25), new(70, floor, -25));
        Camera.ResetFollow(); await Frames(90);
        await AimAtCurrent(() => _arenas[1].Bodies[1].VisualTransform * new Vector3(0, .3f, 0));
        await Frames(30);
        Require(_arenas[1].AssistedCar == 1, "Held engagement established before native ballistic launch");
        var pose = host.World.GetVehicle(Shooter).ObservedPhysics;
        // Fixture supplies launch velocity; native gravity/contact own the flight and landing.
        var launch = new Trackstorm.Core.Vehicles.VehiclePhysicsState(pose.Position + System.Numerics.Vector3.UnitY * .2f, pose.Orientation, new(0, 13, -5), System.Numerics.Vector3.Zero);
        var world = host.World.State;
        host.World.Restore(new(world.Tick, world.LastInput, world.Vehicles.Select(state => state.VehicleId != Shooter ? state :
            new Trackstorm.Core.Vehicles.VehicleSnapshot(state.VehicleId, state.LifeId, new(world.Tick, launch, false, false, 0, 0), state.Damage, launch)), world.Match));
        _arenas[0].Bodies[Shooter].Apply(launch);
        int airborne = 0, retainedAir = 0, grounded = 0, retainedGround = 0;
        bool loggedLoss = false;
        float peak = pose.Position.Y;
        for (int i = 0; i < 240; i++)
        {
            await Frames(1);
            var state = host.World.GetVehicle(Shooter);
            if (_arenas[1].AssistedCar != 1 && !loggedLoss)
            {
                loggedLoss = true;
                Vector3 target = _arenas[1].Bodies[1].VisualTransform * new Vector3(0, .3f, 0);
                using var query = PhysicsRayQueryParameters3D.Create(Camera.GlobalPosition, target, 1);
                using var hit = _arenas[1].GetWorld3D().DirectSpaceState.IntersectRay(query);
                GD.Print($"FLIGHT_LOSS frame={i} lens={Camera.GlobalPosition} target={target} error={Camera.ProjectRayNormal(ViewCenter).AngleTo(target - Camera.GlobalPosition)} cover={hit} active={_input.Adapter.CameraAimActive}");
            }
            peak = Math.Max(peak, state.ObservedPhysics.Position.Y);
            if (!state.Movement.Grounded)
            {
                airborne++; if (_arenas[1].AssistedCar == 1) { retainedAir++; }
            }
            else if (airborne > 10)
            {
                grounded++; if (_arenas[1].AssistedCar == 1) { retainedGround++; }
            }
        }
        Require(peak - pose.Position.Y > 2 && airborne > 30 && grounded > 30,
            $"Native jump/landing exercised: rise={peak - pose.Position.Y:F2}m airborne={airborne} landed={grounded} frames");
        Require(retainedAir >= airborne - 3 && retainedGround >= grounded - 3,
            $"Stationary held mouse retains through launch/flight/landing: air={retainedAir}/{airborne}, ground={retainedGround}/{grounded}");
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false }); await Frames(120);
        Require(_arenas[1].AssistedCar == 0, "Post-landing RMB release clears engagement without reacquisition");
        _heldTargets = null;
        foreach (var platform in platforms) { platform.Free(); }
    }

    private async Task CheckHeldAimLifecycle()
    {
        var host = _arenas[0].Driver.Host!;
        host.Items.RemovePlayer(Shooter);
        Position(new(0, Ground, 35), new(0, Ground, 0), new(70, Ground, 0));
        _heldTargets = (new(0, Ground, 0), new(70, Ground, 0));
        host.Items.Grant(host.World, Shooter, HeldItem.MachineGun);
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0 });
        Camera.ResetFollow(); await Frames(180);
        Require(_arenas[1].AssistedCar == 0 && !_arenas[1].AimOverlay.Bounds.HasValue && _arenas[1].Driver.DesiredAim.HasValue,
            "RMB initially up: visible rival cannot acquire; ordinary desired aim remains available");
        int rays = 0;
        var native = _arenas[0].Driver.RaycastWeapon!;
        _arenas[0].Driver.RaycastWeapon = (owner, start, end) =>
        {
            if (owner == Shooter) { rays++; }
            return native(owner, start, end);
        };
        // Actual Godot button events -> adapter capture -> ordinary driver held/press path.
        _captureInput = true;
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true }); await Frames(30);
        Require(rays > 20 && _arenas[1].AssistedCar == 0, $"LMB free fire without RMB produces authoritative rays ({rays}) without acquiring");
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false }); await Frames(10);
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true }); await Frames(90);
        Require(_arenas[1].AssistedCar == 1, "Press and hold RMB acquires and retains with no mouse movement or firing");
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true }); await Frames(20);
        int beforeRelease = rays;
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(_arenas[1].AssistedCar == 0 && !_arenas[1].AimOverlay.Bounds.HasValue && _arenas[1].Driver.DesiredAim.HasValue,
            "Release while still centred immediately clears assistance, preserving weapon aim");
        float releasedYaw = Camera.Rotation.Y;
        _heldTargets = (new(2, Ground, 0), new(70, Ground, 0));
        await Frames(60);
        Require(rays - beforeRelease > 50 && _arenas[1].AssistedCar == 0,
            $"LMB remains held across RMB release: {rays - beforeRelease} subsequent rays, no released-button reacquisition");
        Require(Math.Abs(Mathf.AngleDifference(releasedYaw, Camera.Rotation.Y)) < .003f,
            "Released camera does not follow the previously selected moving rival");
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false }); await Frames(10);
        _heldTargets = (new(0, Ground, 0), new(70, Ground, 0)); await Frames(20);
        for (int i = 0; i < 3; i++)
        {
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true }); await Frames(6);
            Require(_arenas[1].AssistedCar == 1, "Repress starts fresh eligible acquisition");
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false }); await Frames(6);
            Require(_arenas[1].AssistedCar == 0, "Short RMB tap cannot latch engagement");
        }
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true }); await Frames(20);
        ulong revision = _input.Adapter.CameraAimRevision;
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true }); await Frames(20);
        Require(_input.Adapter.CameraAimRevision > revision && _arenas[1].AssistedCar == 1,
            "Release/repress between renders crosses a fresh input-intent boundary");
        _restoreScriptFocus = false;
        _input._Notification((int)NotificationApplicationFocusOut); await Frames(6);
        Require(!_input.Adapter.CameraAimActive && _arenas[1].AssistedCar == 0 && !_arenas[1].Driver.DesiredAim.HasValue,
            "Production focus-loss notification clears assistance and suppresses ordinary input");
        _input._Notification((int)NotificationApplicationFocusIn); await Frames(12);
        Require(!_input.Adapter.CameraAimActive && _arenas[1].AssistedCar == 0,
            "Focus return does not restore the old held engagement");
        _restoreScriptFocus = true;
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = .16f }); await Frames(3);
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0 }); await Frames(60);
        Require(!_input.Adapter.MouseLookHeld && _arenas[1].AssistedCar == 1,
            "Existing remappable controller camera input enables neutral retention without RMB");
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 1 }); await Frames(12);
        Require(_arenas[1].AssistedCar == 0, "Controller deliberate outward input releases its engagement");
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0 });
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        _captureInput = false;
        _arenas[0].Driver.RaycastWeapon = native;
        _heldTargets = null;
        await Frames(30);
    }
}
