using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Verification;

public sealed partial class CameraIntegrationChecks
{
    private static void VerifyPhotoCamera(VehicleChaseCamera camera, PlayerInput input, VehicleSnapshot state)
    {
        input.GameplayAvailable = () => true;
        input.Adapter.Enabled = true;
        input.Adapter.GameplaySuppressed = false;
        input._Process(0);
        camera.InputSource = input.Adapter;
        camera.SettingsSource = null;
        camera.ResetFollow();
        camera.Follow(Transform3D.Identity, state, 1f / 60);
        Transform3D baseline = camera.GlobalTransform;
        float near = camera.Near;
        ulong tick = 10000;
        void Send(InputEvent value)
        {
            using (value) { Godot.Input.ParseInputEvent(value); Godot.Input.FlushBufferedEvents(); input.Adapter.Observe(); }
        }
        void KeyEvent(Key key, bool down) => Send(new InputEventKey { PhysicalKeycode = key, Pressed = down });
        void Toggle(bool controller)
        {
            if (controller)
            {
                Send(new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.DpadUp, Pressed = true });
                Send(new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.DpadUp, Pressed = false });
            }
            else { KeyEvent(Key.C, true); KeyEvent(Key.C, false); }
        }
        foreach (int fps in new[] { 30, 60, 144 })
        foreach (bool controller in new[] { false, true })
        {
            Toggle(controller);
            Require(input.Adapter.FreeCameraActive, "Keyboard/controller photo toggle enters once.");
            KeyEvent(Key.W, true); KeyEvent(Key.E, true);
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
            for (int i = 0; i < fps; i++)
            {
                var frame = input.Adapter.Capture(++tick);
                Require(frame.Accelerate == 0 && frame.Steering == 0 && frame.AirPitch == 0 && !frame.Held.HasFlag(InputButtons.UseItem) && !frame.Pressed.HasFlag(InputButtons.UseItem), "Photo controls cannot drive or fire.");
                camera.Follow(Transform3D.Identity, state, 1f / fps);
            }
            Require(camera.GlobalPosition.DistanceTo(baseline.Origin) > 4 && camera.GlobalPosition.Y > baseline.Origin.Y + 2, "Photo camera translates and rises independently.");
            KeyEvent(Key.W, false); KeyEvent(Key.E, false);
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            Send(new InputEventMouseMotion { ScreenRelative = new Vector2(100, -30) });
            camera.Follow(Transform3D.Identity, state, 1f / fps);
            Require(!camera.GlobalBasis.IsEqualApprox(baseline.Basis) && !input.Adapter.CameraAimActive, "Mouse photo look needs no RMB and creates no aiming engagement.");
            KeyEvent(Key.Tab, true);
            Require(input.Adapter.Capture(++tick).Held.HasFlag(InputButtons.Leaderboard), "Tab leaderboard remains available in photo mode.");
            KeyEvent(Key.Tab, false);
            Send(new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.Back, Pressed = true });
            Require(input.Adapter.Capture(++tick).Held.HasFlag(InputButtons.Leaderboard), "View leaderboard remains available in photo mode.");
            Send(new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.Back, Pressed = false });
            Toggle(controller);
            camera.Follow(Transform3D.Identity, state, 1f / fps);
            Require(!input.Adapter.FreeCameraActive && camera.GlobalTransform.IsEqualApprox(baseline) && camera.Near == near, "Exit restores configured chase and near plane.");
        }
        foreach (int boundary in new[] { 0, 1, 2 })
        {
            Toggle(false);
            camera.Follow(Transform3D.Identity, state, 1f / 60);
            if (boundary == 0) input.Adapter.Enabled = false;
            if (boundary == 1) input.Adapter.GameplaySuppressed = true;
            if (boundary == 2) camera.ResetFollow();
            camera.Follow(Transform3D.Identity, state, 1f / 60);
            Require(!input.Adapter.FreeCameraActive && camera.GlobalTransform.IsEqualApprox(baseline), "Focus, menu and recovery restore chase.");
            Toggle(true);
            Require(!input.Adapter.FreeCameraActive || boundary == 2, "Menu/focus cannot toggle photo mode.");
            input.Adapter.EndFreeCamera();
            input.Adapter.Enabled = true;
            input.Adapter.GameplaySuppressed = false;
            input._Process(0);
        }
        Toggle(false);
        camera.Follow(Transform3D.Identity, state, 1f / 60);
        var newLife = new VehicleSnapshot(state.VehicleId, state.LifeId + 1, state.Movement, state.Damage, state.ObservedPhysics);
        camera.Follow(Transform3D.Identity, newLife, 1f / 60);
        Require(!input.Adapter.FreeCameraActive, "A new authoritative life exits photo mode.");
        camera.ResetFollow();
        camera.Follow(Transform3D.Identity, state, 1f / 60);
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        Toggle(false); Toggle(false);
        Require(!input.Adapter.Capture(++tick).Pressed.HasFlag(InputButtons.UseItem), "Rapid entry/exit cannot replay a pending weapon press.");
        GD.Print("Photo camera checks passed: keyboard/D-pad toggles, 30/60/144 FPS translation, ascent, mouse look, aim/fire/drive isolation, both leaderboard bindings, restoration, focus/menu/recovery.");
    }
}
