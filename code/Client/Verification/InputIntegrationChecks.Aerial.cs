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
        Axis(0); KeyState(Key.Shift, true);
        foreach (var key in new[] { Key.W, Key.E, Key.D })
        {
            KeyState(key, true); adapter.KeyboardAerialSensitivity = 0.1f;
            var low = adapter.Capture(1);
            adapter.KeyboardAerialSensitivity = 1;
            var high = adapter.Capture(2);
            short Value(InputFrame frame) => key == Key.W ? frame.AirPitch : key == Key.E ? frame.AirYaw : frame.AirRoll;
            Check(Math.Abs(Value(high)) > Math.Abs(Value(low)) * 9, "keyboard aerial sensitivity changes each axis");
            adapter.AerialSensitivity = 0.1f; adapter.DeadZone = 0.95f;
            Check(Value(adapter.Capture(3)) == Value(high), "controller aerial setting/deadzone cannot change keys");
            KeyState(key, false);
        }
        KeyState(Key.Shift, false);
        Send(new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.LeftShoulder, Pressed = true });
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

    private void VerifyDeliberateAirControl()
    {
        var adapter = _player.Adapter;
        adapter.Bindings.RestoreDefaults();
        void KeyState(Key key, bool held) => Send(new InputEventKey { PhysicalKeycode = key, Pressed = held });
        void Button(JoyButton button, bool held) => Send(new InputEventJoypadButton { Device = 0, ButtonIndex = button, Pressed = held });
        void Axis(JoyAxis axis, float value) => Send(new InputEventJoypadMotion { Device = 0, Axis = axis, AxisValue = value });
        foreach (var key in new[] { Key.W, Key.S, Key.A, Key.D, Key.Q, Key.E })
        {
            KeyState(key, true);
            var idle = adapter.Capture(1);
            Check(idle.AirPitch == 0 && idle.AirYaw == 0 && idle.AirRoll == 0, "keys never supply aerial axes without modifier");
            KeyState(Key.Shift, true);
            var active = adapter.Capture(2);
            Check((active.Held & InputButtons.AirControl) != 0, "Shift explicitly enables air control");
            Check(active.AirPitch == (key == Key.W ? -32767 : key == Key.S ? 32767 : 0), "keyboard pitch mapping");
            Check(active.AirYaw == (key == Key.Q ? -32767 : key == Key.E ? 32767 : 0), "keyboard yaw mapping");
            Check(active.AirRoll == (key == Key.A ? -32767 : key == Key.D ? 32767 : 0), "keyboard roll mapping");
            if (key == Key.E) { Check(((active.Held | active.Pressed) & InputButtons.SwitchItem) == 0, "aerial E does not switch weapon"); }
            KeyState(Key.Shift, false);
            var released = adapter.Capture(3);
            Check(released.AirPitch == 0 && released.AirYaw == 0 && released.AirRoll == 0 && (released.Held & InputButtons.AirControl) == 0, "modifier release immediately clears all aerial axes");
            KeyState(key, false); adapter.Capture(4);
        }
        KeyState(Key.E, true); KeyState(Key.Shift, true);
        Check((adapter.Capture(4).Pressed & InputButtons.SwitchItem) == 0, "E before Shift within one tick cannot leak a weapon-switch edge");
        KeyState(Key.Shift, false);
        Check((adapter.Capture(4).Pressed & InputButtons.SwitchItem) == 0, "held yaw E requires release after Shift");
        KeyState(Key.E, false); adapter.Capture(4);
        KeyState(Key.E, true); KeyState(Key.Shift, true); KeyState(Key.E, false);
        Check((adapter.Capture(4).Pressed & InputButtons.SwitchItem) == 0, "quick E release retains pending contextual edge suppression");
        KeyState(Key.Shift, false); adapter.Capture(4);
        KeyState(Key.E, true);
        Check((adapter.Capture(4).Pressed & InputButtons.SwitchItem) != 0, "fresh E after contextual capture switches normally");
        KeyState(Key.E, false); adapter.Capture(4);
        Button(JoyButton.LeftShoulder, true);
        Axis(JoyAxis.LeftX, 1); Axis(JoyAxis.LeftY, -1);
        var yaw = adapter.Capture(5);
        Check(yaw.AirPitch == -32767 && yaw.AirYaw == 32767 && yaw.AirRoll == 0, "LB stick commands pitch and yaw");
        Button(JoyButton.A, true);
        var roll = adapter.Capture(6);
        Check(roll.AirPitch == -32767 && roll.AirYaw == 0 && roll.AirRoll == 32767 && (roll.Held & InputButtons.UseItem) == 0, "LB+A switches horizontal stick to roll without firing");
        Button(JoyButton.A, false);
        Check(adapter.Capture(7).AirYaw == 32767 && adapter.Capture(8).AirRoll == 0, "A release immediately restores yaw");
        Axis(JoyAxis.LeftX, -1); Axis(JoyAxis.LeftY, 1);
        Check(adapter.Capture(9).AirPitch == 32767 && adapter.Capture(10).AirYaw == -32767, "opposite stick pitch and yaw signs");
        Button(JoyButton.A, true);
        Check(adapter.Capture(11).AirRoll == -32767, "controller roll left");
        Button(JoyButton.A, false);
        foreach (float deadzone in new[] { 0f, 0.15f, 0.5f, 0.95f })
        foreach (float sensitivity in new[] { 0.1f, 1f, 3f })
        {
            adapter.DeadZone = deadzone; adapter.AerialSensitivity = sensitivity; adapter.SteeringSensitivity = sensitivity;
            Axis(JoyAxis.LeftX, deadzone); Axis(JoyAxis.LeftY, -deadzone);
            var neutral = adapter.Capture(12);
            Check(neutral.Steering == 0 && neutral.AirYaw == 0 && neutral.AirPitch == 0, "shared stick deadzone rejects center in both modes");
            float raw = deadzone + (1 - deadzone) * 0.5f;
            Axis(JoyAxis.LeftX, raw); Axis(JoyAxis.LeftY, -raw);
            var sample = adapter.Capture(13);
            float airExpected = Math.Clamp(0.5f * sensitivity, 0, 1);
            float groundExpected = MathF.Pow(0.5f, 3.5f) * sensitivity;
            Check(Math.Abs(sample.AirYaw / 32767f - airExpected) < 0.0001f && Math.Abs(sample.AirPitch / 32767f + airExpected) < 0.0001f, "aerial sensitivity scales linear recognized stick input");
            Check(Math.Abs(sample.Steering / 32767f - groundExpected) < 0.0001f, "ground sensitivity retains TS-268 precision curve even while LB held");
            adapter.AerialSensitivity = 3; adapter.SteeringSensitivity = 0.1f;
            var independent = adapter.Capture(14);
            Check(independent.AirYaw == 32767 && independent.Steering < sample.Steering + 1, "ground and aerial sensitivity are independent");
        }
        adapter.DeadZone = 0.15f; adapter.AerialSensitivity = adapter.SteeringSensitivity = 1;
        Button(JoyButton.X, true);
        Check((adapter.Capture(14).Pressed & InputButtons.SwitchItem) != 0, "controller X remains available while LB is held");
        Button(JoyButton.X, false);
        Button(JoyButton.LeftShoulder, false);
        Check(adapter.Capture(15).AirYaw == 0, "LB release exits air control while stick remains deflected");
        Axis(JoyAxis.LeftX, 0); Axis(JoyAxis.LeftY, 0);
        Button(JoyButton.Y, true); Check((adapter.Capture(16).Pressed & InputButtons.UseItem) != 0, "Y fires"); Button(JoyButton.Y, false);
        Button(JoyButton.X, true); Check((adapter.Capture(17).Pressed & InputButtons.SwitchItem) != 0, "X switches weapon"); Button(JoyButton.X, false);
        Button(JoyButton.LeftShoulder, true); Axis(JoyAxis.LeftY, 1);
        adapter.GameplaySuppressed = true;
        Check(adapter.Capture(18).AirPitch == 0, "UI suppression clears aerial intent");
        adapter.GameplaySuppressed = false; adapter.Enabled = false;
        Check(adapter.Capture(19).AirPitch == 0, "focus loss clears aerial intent");
        Button(JoyButton.LeftShoulder, false); Axis(JoyAxis.LeftY, 0); adapter.Enabled = true; adapter.Capture(20);
        VerifyDeviceSensitivity();
    }
}
