using Godot;
using Trackstorm.Core.Input;

namespace Trackstorm.Client.Verification;

public sealed partial class InputIntegrationChecks
{
    private void VerifyDeviceSensitivity()
    {
        var adapter = _player.Adapter;
        void KeyState(Key key, bool held) => Send(new InputEventKey { PhysicalKeycode = key, Pressed = held });
        void Axis(float value) => Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftX, AxisValue = value });
        void Reset() { adapter.Enabled = false; adapter.Capture(1); adapter.Enabled = true; }
        short KeyboardSteer(float sensitivity, float controller, float deadzone)
        {
            Reset(); adapter.KeyboardSteeringSensitivity = sensitivity; adapter.SteeringSensitivity = controller; adapter.DeadZone = deadzone;
            KeyState(Key.D, true); InputFrame frame = default;
            for (ulong tick = 1; tick <= 12; tick++) frame = adapter.Capture(tick);
            KeyState(Key.D, false); return frame.Steering;
        }
        short slow = KeyboardSteer(0.1f, 1, 0.15f), fast = KeyboardSteer(3, 1, 0.15f);
        Check(fast > slow * 20, "keyboard steering sensitivity changes TS-268 ramp response");
        Check(KeyboardSteer(3, 0.1f, 0.95f) == fast, "controller sensitivity/deadzone cannot change keyboard steering");
        Reset(); Axis(0.6f); adapter.DeadZone = 0.15f; adapter.SteeringSensitivity = 1;
        var controllerSteer = adapter.Capture(1).Steering;
        adapter.KeyboardSteeringSensitivity = 0.1f;
        Check(adapter.Capture(2).Steering == controllerSteer, "keyboard steering setting cannot change controller steering");
        Axis(0);
        foreach (var key in new[] { Key.W, Key.A, Key.D })
        {
            KeyState(Key.Shift, key == Key.D);
            KeyState(key, true); adapter.KeyboardAerialSensitivity = 0.1f;
            var low = adapter.Capture(1);
            adapter.KeyboardAerialSensitivity = 1;
            var high = adapter.Capture(2);
            short Value(InputFrame frame) => key == Key.W ? frame.AirPitch : key == Key.A ? frame.AirYaw : frame.AirRoll;
            Check(Math.Abs(Value(high)) > Math.Abs(Value(low)) * 9, "keyboard aerial sensitivity changes each axis");
            adapter.AerialSensitivity = 0.1f; adapter.DeadZone = 0.95f;
            Check(Value(adapter.Capture(3)) == Value(high), "controller aerial setting/deadzone cannot change keys");
            KeyState(key, false);
        }
        KeyState(Key.Shift, false);
        Send(new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.LeftShoulder, Pressed = false });
        adapter.DeadZone = 0.15f; adapter.AerialSensitivity = 1; Axis(0.6f);
        var controllerAir = adapter.Capture(1).AirYaw;
        adapter.KeyboardAerialSensitivity = 0.1f;
        Check(adapter.Capture(2).AirYaw == controllerAir, "keyboard aerial setting cannot change controller yaw");
        Axis(0); Send(new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.LeftShoulder, Pressed = false });
        // Device routing follows the physical binding, including remapped mouse/pad buttons.
        using var mouse = new InputEventMouseButton { ButtonIndex = MouseButton.Left };
        adapter.Bindings.Replace(InputAction.AirPitchUp, mouse);
        KeyState(Key.Shift, true); Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        Check(Math.Abs(adapter.Capture(3).AirPitch / 32767f - 0.1f) < 0.0001f, "mouse binding uses keyboard/mouse aerial sensitivity");
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false }); KeyState(Key.Shift, false);
        adapter.Bindings.RestoreDefaults(); adapter.KeyboardAerialSensitivity = adapter.KeyboardSteeringSensitivity = adapter.AerialSensitivity = adapter.SteeringSensitivity = 1; adapter.DeadZone = 0.15f; Reset();
    }

    private void VerifyAutomaticAirControls()
    {
        var adapter = _player.Adapter;
        adapter.Bindings.RestoreDefaults();
        void KeyState(Key key, bool held) => Send(new InputEventKey { PhysicalKeycode = key, Pressed = held });
        void Button(JoyButton button, bool held) => Send(new InputEventJoypadButton { Device = 0, ButtonIndex = button, Pressed = held });
        void Axis(JoyAxis axis, float value) => Send(new InputEventJoypadMotion { Device = 0, Axis = axis, AxisValue = value });
        Check(!InputMap.HasAction("trackstorm_AirControl"), "retired activation action is absent from native controls");
        foreach (var key in new[] { Key.W, Key.S, Key.A, Key.D, Key.Q, Key.E })
        {
            KeyState(key, true);
            var normal = adapter.Capture(1);
            int pitch = key == Key.W ? 32767 : key == Key.S ? -32767 : 0;
            int horizontal = key == Key.A ? -32767 : key == Key.D ? 32767 : 0;
            Check(normal.AirPitch == pitch && normal.AirYaw == horizontal && normal.AirRoll == 0, "unmodified keyboard pitch/yaw mapping");
            Check((normal.Held & InputButtons.AirControl) == 0, "capture never emits legacy enable bit");
            KeyState(Key.Shift, true);
            var roll = adapter.Capture(2);
            Check(roll.AirPitch == pitch && roll.AirYaw == 0 && roll.AirRoll == horizontal, "Shift changes only horizontal aerial intent to roll");
            if (key == Key.E) { Check((roll.Held & InputButtons.SwitchItem) != 0, "E remains weapon switch with Shift"); }
            KeyState(Key.Shift, false);
            var released = adapter.Capture(3);
            Check(released.AirPitch == pitch && released.AirYaw == horizontal && released.AirRoll == 0, "Shift release restores yaw without disabling pitch");
            KeyState(key, false); adapter.Capture(4);
        }
        foreach (float direction in new[] { -1f, 1f })
        {
            Axis(JoyAxis.LeftX, direction);
            Axis(JoyAxis.TriggerRight, direction > 0 ? 1 : 0);
            Axis(JoyAxis.TriggerLeft, direction < 0 ? 1 : 0);
            var yaw = adapter.Capture(5);
            Check(yaw.AirPitch == direction * 32767 && yaw.AirYaw == direction * 32767 && yaw.AirRoll == 0, "triggers pitch and left stick yaws without enable button");
            Button(JoyButton.LeftShoulder, true);
            var roll = adapter.Capture(6);
            Check(roll.AirPitch == yaw.AirPitch && roll.AirYaw == 0 && roll.AirRoll == yaw.AirYaw, "LB redirects horizontal stick to roll and preserves trigger pitch");
            Button(JoyButton.LeftShoulder, false);
            Check(adapter.Capture(7).AirYaw == yaw.AirYaw && adapter.Capture(8).AirRoll == 0, "LB release restores yaw");
        }
        Axis(JoyAxis.TriggerLeft, 0); Axis(JoyAxis.TriggerRight, 0); Axis(JoyAxis.LeftX, 0);
        Axis(JoyAxis.LeftY, -1); Button(JoyButton.A, true);
        var unused = adapter.Capture(9);
        Check(unused.AirPitch == 0 && unused.AirRoll == 0, "old stick pitch and A roll controls no longer rotate");
        Button(JoyButton.A, false); Axis(JoyAxis.LeftY, 0);
        foreach (float deadzone in new[] { 0f, 0.15f, 0.5f, 0.95f })
        foreach (float sensitivity in new[] { 0.1f, 1f, 3f })
        {
            adapter.DeadZone = deadzone; adapter.AerialSensitivity = sensitivity; adapter.SteeringSensitivity = sensitivity;
            Axis(JoyAxis.LeftX, deadzone); Axis(JoyAxis.TriggerRight, deadzone);
            var neutral = adapter.Capture(12);
            Check(neutral.Steering == 0 && neutral.AirYaw == 0 && neutral.AirPitch == 0, "stick and trigger deadzone reject center");
            float raw = deadzone + (1 - deadzone) * 0.5f;
            Axis(JoyAxis.LeftX, raw); Axis(JoyAxis.TriggerRight, raw);
            var sample = adapter.Capture(13);
            float airExpected = Math.Clamp(0.5f * sensitivity, 0, 1);
            float groundExpected = MathF.Pow(0.5f, 3.5f) * sensitivity;
            Check(Math.Abs(sample.AirYaw / 32767f - airExpected) < 0.0001f && Math.Abs(sample.AirPitch / 32767f - airExpected) < 0.0001f, "aerial sensitivity retains linear analog precision");
            Check(Math.Abs(sample.Steering / 32767f - groundExpected) < 0.0001f, "ground precision curve remains independent");
        }
        adapter.DeadZone = 0.15f; adapter.AerialSensitivity = adapter.SteeringSensitivity = 1;
        Axis(JoyAxis.LeftX, 0); Axis(JoyAxis.TriggerRight, 1); Axis(JoyAxis.TriggerLeft, 1);
        Check(adapter.Capture(14).AirPitch == 0, "equal opposing triggers cancel pitch");
        Axis(JoyAxis.TriggerLeft, 0);
        adapter.GameplaySuppressed = true;
        Check(adapter.Capture(15).AirPitch == 0, "UI suppression clears aerial intent");
        adapter.GameplaySuppressed = false; adapter.Enabled = false;
        Check(adapter.Capture(16).AirPitch == 0, "focus loss clears aerial intent");
        Axis(JoyAxis.TriggerRight, 0); adapter.Enabled = true; adapter.Capture(17);
        VerifyAerialBindingMigration();
        VerifyDeviceSensitivity();
    }

    private void VerifyAerialBindingMigration()
    {
        var adapter = _player.Adapter;
        var old = new Trackstorm.Core.Settings.PlayerSettings { BindingDefaultsVersion = 2 }
            .WithBindings(InputAction.AirRoll, ["button:0:0"])
            .WithBindings(InputAction.AirPitchDown, ["key:87", "axis:0:1:-1"])
            .WithBindings(InputAction.AirPitchUp, ["key:83", "axis:0:1:1"])
            .WithBindings(InputAction.AirYawLeft, ["key:81", "axis:0:0:-1"])
            .WithBindings(InputAction.AirYawRight, ["key:69", "axis:0:0:1"])
            .WithBindings(InputAction.AirRollLeft, ["key:65"])
            .WithBindings(InputAction.AirRollRight, ["key:68"]);
        var defaults = Input.InputBindingPreferences.Capture(adapter, new());
        Input.InputBindingPreferences.Apply(adapter, old);
        var migrated = Input.InputBindingPreferences.Capture(adapter, old);
        foreach (var action in old.Bindings.Keys)
            Check(migrated.Bindings[action].SequenceEqual(defaults.Bindings[action]), "previous aerial default migrates: " + action);
        var custom = old.WithBindings(InputAction.AirPitchUp, ["key:74"]).WithBindings(InputAction.AirRoll, []);
        Input.InputBindingPreferences.Apply(adapter, custom);
        var captured = Input.InputBindingPreferences.Capture(adapter, custom);
        Check(captured.Bindings[InputAction.AirPitchUp].SequenceEqual(new[] { "key:74" }) && captured.Bindings[InputAction.AirRoll].Count == 0, "custom aerial binding and explicit unbind survive migration");
        adapter.Bindings.RestoreDefaults();
        Input.InputBindingPreferences.Apply(adapter, migrated);
        Check(Input.InputBindingPreferences.Capture(adapter, migrated).Bindings[InputAction.AirPitchUp].SequenceEqual(defaults.Bindings[InputAction.AirPitchUp]), "migrated aerial bindings survive restart");
        adapter.Bindings.RestoreDefaults();
    }
}
