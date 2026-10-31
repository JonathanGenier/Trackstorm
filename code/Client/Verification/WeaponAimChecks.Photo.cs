using Godot;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Verification;

public sealed partial class WeaponAimChecks
{
    private async Task CheckPhotoCamera()
    {
        var host = _arenas[0].Driver.Host!;
        Position(new(0, Ground, 35), new(0, Ground, 0), new(6, Ground, 0));
        host.Items.Grant(host.World, Shooter, HeldItem.MachineGun);
        await Frames(120);
        await AimAt(_arenas[1].Bodies[1].VisualPosition + Vector3.Up);
        _captureInput = true;
        void Key(Key key, bool pressed) => Send(new InputEventKey { PhysicalKeycode = key, Pressed = pressed });
        void Toggle() { Key(Godot.Key.C, true); Key(Godot.Key.C, false); }
        Toggle(); await Frames(2);
        Require(_input.Adapter.FreeCameraActive && !_input.Adapter.CameraAimActive && _arenas[1].AssistedCar == 0 && !_arenas[1].Driver.DesiredAim.HasValue,
            "Photo entry clears actual acquired aim and desired network aim.");
        var observer = _arenas[2].GetNode<Camera3D>("ChaseCamera");
        var observerOffset = observer.GlobalPosition - _arenas[2].Bodies[_arenas[2].Driver.LocalVehicleId].VisualPosition;
        var start = Camera.GlobalPosition;
        Key(Godot.Key.W, true); Key(Godot.Key.E, true);
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        await Frames(60);
        Require(Camera.GlobalPosition.DistanceTo(start) > 5 && _input.Adapter.Capture(0).Accelerate == 0 && !_arenas[1].Driver.DesiredAim.HasValue,
            "Photo translation through production UDP capture remains isolated from driving, firing and aiming.");
        Key(Godot.Key.W, false); Key(Godot.Key.E, false);
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        await Frames(20);
        Require(_arenas[2].CameraInput?.FreeCameraActive != true &&
            (observer.GlobalPosition - _arenas[2].Bodies[_arenas[2].Driver.LocalVehicleId].VisualPosition).DistanceTo(observerOffset) < .2f,
            "Observer retains its own chase offset while native suspension settles.");
        await Capture("photo-elevated");
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftY, AxisValue = -.4f });
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = .3f });
        await Frames(45);
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftY, AxisValue = 0 });
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0 });
        Require(!_input.Adapter.CameraAimActive && !_arenas[1].Driver.DesiredAim.HasValue, "Switching to both controller sticks preserves photo isolation.");
        await Capture("photo-controller");
        Toggle(); await Frames(20);
        Require(!_input.Adapter.FreeCameraActive && Camera.Near > .025f, "Photo exit restores chase projection.");
        await Capture("photo-restored");
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        await Frames(2);
        Toggle(); await Frames(2);
        Vector3 detail = _arenas[1].Bodies[Shooter].VisualPosition + new Vector3(0, 1.5f, 0);
        Vector3 direction = (detail - Camera.GlobalPosition).Normalized();
        Vector3 angles = Camera.GlobalBasis.GetEuler();
        float yaw = MathF.Atan2(-direction.X, -direction.Z), pitch = MathF.Asin(direction.Y);
        Send(new InputEventMouseMotion { ScreenRelative = new(-Mathf.AngleDifference(angles.Y, yaw) / .003f, -(pitch - angles.X) / .003f) });
        Key(Godot.Key.W, true); await Frames(20); Key(Godot.Key.W, false); await Frames(15);
        await Capture("photo-full-car");
        Key(Godot.Key.W, true); Key(Godot.Key.Ctrl, true); await Frames(120);
        Key(Godot.Key.W, false); Key(Godot.Key.Ctrl, false); await Frames(15);
        await Capture("photo-detail");
        Key(Godot.Key.S, true); Key(Godot.Key.Shift, true); await Frames(60);
        Key(Godot.Key.S, false); Key(Godot.Key.Shift, false); await Frames(15);
        await Capture("photo-wide");
        Toggle(); await Frames(3);
        _captureInput = false;
    }
}
