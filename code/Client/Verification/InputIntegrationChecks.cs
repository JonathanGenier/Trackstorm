using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Core.Input;

namespace Trackstorm.Client.Verification;

/// <summary>Focused headless Godot checks; loaded only by the explicit verification scene.</summary>
public sealed partial class InputIntegrationChecks : Node
{
    private PlayerInput _player = null!;
    private int _assertions;

    /// <inheritdoc/>
    public override void _Ready()
    {
        CallDeferred(MethodName.Run);
    }

    /// <summary>Runs native input integration checks and exits nonzero on failure.</summary>
    public void Run()
    {
        try
        {
            _player = new PlayerInput();
            AddChild(_player);
            _player.SetPhysicsProcess(false);
            VerifyRequiredDefaults();
            GD.Print($"Connected physical gamepads before synthetic input: {Godot.Input.GetConnectedJoypads().Count}");
            VerifyEveryDefaultBinding();
            VerifyAnalogAndIndependentLeaderboard();
            VerifyRemappingAndMultipleBindings();
            VerifyTapFocusAndTickCapture();
            GD.Print($"Input integration passed: {_assertions} assertions.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private static bool ActionActive(InputFrame frame, InputAction action) => action switch
    {
        InputAction.Accelerate => frame.Accelerate == 65535,
        InputAction.Brake => frame.Brake == 65535,
        InputAction.SteerLeft => frame.Steering == -32767,
        InputAction.SteerRight => frame.Steering == 32767,
        _ => (frame.Held & (InputButtons)(1 << ((int)action - (int)InputAction.Drift))) != 0,
    };

    private static void SetPressed(InputEvent @event, bool pressed)
    {
        switch (@event)
        {
            case InputEventKey key:
                key.Pressed = pressed;
                break;
            case InputEventMouseButton mouse:
                mouse.Pressed = pressed;
                break;
            case InputEventJoypadButton button:
                button.Pressed = pressed;
                break;
            case InputEventJoypadMotion axis when !pressed:
                axis.AxisValue = 0;
                break;
        }
    }

    private static void Send(InputEvent @event)
    {
        using var copy = (InputEvent)@event.Duplicate();
        Godot.Input.ParseInputEvent(copy);
        Godot.Input.FlushBufferedEvents();
    }

    private void VerifyRequiredDefaults()
    {
        Check(InputMap.ActionHasEvent(PlayerInputBindings.Name(InputAction.Drift), new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.B }), "B defaults to physical handbrake");
        Check(InputMap.ActionHasEvent(PlayerInputBindings.Name(InputAction.UseItem), new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.A }), "A defaults to item use");
        Check(InputMap.ActionHasEvent(PlayerInputBindings.Name(InputAction.UseItem), new InputEventMouseButton { ButtonIndex = MouseButton.Left }), "LMB defaults to item use");
        Check(_player.Adapter.Bindings.FindConflicts(new InputEventMouseButton { ButtonIndex = MouseButton.Right }).Length == 0, "RMB remains reserved");
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        _player.Adapter.GameplaySuppressed = true;
        Check((_player.Adapter.Capture(0).Pressed & InputButtons.UseItem) == 0, "opening UI cannot leak a pending mouse item press");
        _player.Adapter.GameplaySuppressed = false;
        Check((_player.Adapter.Capture(0).Pressed & InputButtons.UseItem) == 0, "closing UI while item input remains held waits for release");
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        _player.Adapter.GameplaySuppressed = false;
        var saved = InputBindingPreferences.Capture(_player.Adapter, new Trackstorm.Core.Settings.PlayerSettings());
        _player.Adapter.Bindings.Replace(InputAction.UseItem);
        InputBindingPreferences.Apply(_player.Adapter, Trackstorm.Core.Settings.PlayerSettingsJson.Deserialize(Trackstorm.Core.Settings.PlayerSettingsJson.Serialize(saved)));
        Check(InputMap.ActionHasEvent(PlayerInputBindings.Name(InputAction.UseItem), new InputEventMouseButton { ButtonIndex = MouseButton.Left }), "mouse remapping survives preference serialization");
    }

    private void VerifyEveryDefaultBinding()
    {
        foreach (InputAction action in Enum.GetValues<InputAction>())
        {
            var bindings = InputMap.ActionGetEvents(PlayerInputBindings.Name(action));
            Check(bindings.Count == (action >= InputAction.CameraLeft ? 1 : 2), $"{action} has keyboard and gamepad defaults");
            foreach (InputEvent binding in bindings)
            {
                using var pressed = (InputEvent)binding.Duplicate();
                SetPressed(pressed, true);
                Send(pressed);
                Check(_player.Adapter.Bindings.Strength(action, 0.15f) == 1, $"{action} resolves {binding.GetType().Name}");
                InputFrame first = _player.Adapter.Capture(1);
                if (binding is InputEventKey && action <= InputAction.SteerRight)
                {
                    Check(!ActionActive(first, action), "Digital input ramps before full strength");
                }

                InputFrame frame = first;
                for (ulong tick = 2; tick <= 31; tick++)
                {
                    frame = _player.Adapter.Capture(tick);
                }

                Check(action >= InputAction.CameraLeft ? _player.Adapter.CameraIntent.Length() > 0 : ActionActive(frame, action), $"{action} reaches logical frame");
                SetPressed(pressed, false);
                Send(pressed);
                Check(_player.Adapter.Bindings.Strength(action, 0.15f) == 0, $"{action} releases");
                for (ulong tick = 32; tick <= 61; tick++)
                {
                    _player.Adapter.Capture(tick);
                }
            }
        }
    }

    private void VerifyAnalogAndIndependentLeaderboard()
    {
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftX, AxisValue = 0.575f });
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.TriggerRight, AxisValue = 0.575f });
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.TriggerLeft, AxisValue = 1 });
        InputFrame vehicle = _player.Adapter.Capture(3);
        Check(Math.Abs(vehicle.Steering - 16384) <= 1 && Math.Abs(vehicle.Accelerate - 32768) <= 1 && vehicle.Brake == 65535, "Analog axes normalize and quantize");
        Send(new InputEventKey { PhysicalKeycode = Key.Tab, Pressed = true });
        InputFrame press = _player.Adapter.Capture(4);
        InputFrame held = _player.Adapter.Capture(5);
        Send(new InputEventKey { PhysicalKeycode = Key.Tab, Pressed = false });
        InputFrame release = _player.Adapter.Capture(6);
        Check(press.Pressed == InputButtons.Leaderboard && held.Held == InputButtons.Leaderboard && held.Pressed == 0 && release.Released == InputButtons.Leaderboard, "Leaderboard press/hold/release");
        foreach (InputFrame frame in new[] { press, held, release })
        {
            Check(frame.Steering == vehicle.Steering && frame.Accelerate == vehicle.Accelerate && frame.Brake == vehicle.Brake, "Leaderboard does not change vehicle axes");
        }

        _player.Adapter.InvertSteering = true;
        Check(_player.Adapter.Capture(7).Steering == -vehicle.Steering, "Inversion precedes frame publication");
        _player.Adapter.InvertSteering = false;
        foreach (JoyAxis axis in new[] { JoyAxis.LeftX, JoyAxis.TriggerRight, JoyAxis.TriggerLeft })
        {
            Send(new InputEventJoypadMotion { Device = 0, Axis = axis, AxisValue = 0 });
        }

        Send(new InputEventKey { PhysicalKeycode = Key.A, Pressed = true });
        Send(new InputEventKey { PhysicalKeycode = Key.D, Pressed = true });
        Check(_player.Adapter.Capture(8).Steering == 0, "Opposite steering cancels");
        Send(new InputEventKey { PhysicalKeycode = Key.A, Pressed = false });
        Send(new InputEventKey { PhysicalKeycode = Key.D, Pressed = false });
        Send(new InputEventJoypadMotion { Device = 1, Axis = JoyAxis.TriggerRight, AxisValue = 1 });
        Check(_player.Adapter.Capture(9).Accelerate == 0, "Unassigned gamepad cannot control player");
        Send(new InputEventJoypadMotion { Device = 1, Axis = JoyAxis.TriggerRight, AxisValue = 0 });
    }

    private void VerifyRemappingAndMultipleBindings()
    {
        var bindings = _player.Adapter.Bindings;
        using var key = new InputEventKey { PhysicalKeycode = Key.Q };
        bindings.Replace(InputAction.UseItem, key, new InputEventKey { PhysicalKeycode = Key.R });
        key.PhysicalKeycode = Key.Z;
        Check(bindings.FindConflicts(new InputEventKey { PhysicalKeycode = Key.Q }).Contains(InputAction.UseItem), "Binding is copied and conflict is discoverable");
        Send(new InputEventKey { PhysicalKeycode = Key.E, Pressed = true });
        Check((_player.Adapter.Capture(10).Held & InputButtons.UseItem) == 0, "Old binding stops resolving");
        Send(new InputEventKey { PhysicalKeycode = Key.E, Pressed = false });
        Send(new InputEventKey { PhysicalKeycode = Key.Q, Pressed = true });
        Send(new InputEventKey { PhysicalKeycode = Key.R, Pressed = true });
        _player.Adapter.Capture(11);
        Send(new InputEventKey { PhysicalKeycode = Key.Q, Pressed = false });
        InputFrame held = _player.Adapter.Capture(12);
        Check(held.Held == InputButtons.UseItem && held.Released == 0, "Releasing one binding preserves the other");
        bindings.Replace(InputAction.UseItem);
        Check(_player.Adapter.Capture(13).Released == InputButtons.UseItem, "Unbinding a held action releases at next capture");
        Send(new InputEventKey { PhysicalKeycode = Key.R, Pressed = false });
        bindings.Replace(InputAction.UseItem, new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.Y });
        Send(new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.Y, Pressed = true });
        Check(_player.Adapter.Capture(14).Held == InputButtons.UseItem, "Gamepad button remapping");
        Send(new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.Y, Pressed = false });
        bindings.Replace(InputAction.SteerLeft, new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 1 });
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 1 });
        Check(_player.Adapter.Capture(15).Steering == -32767, "Signed axis remapping");
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0 });
        bool rejected = false;
        try
        {
            bindings.Replace(InputAction.Accelerate, new InputEventMouseButton());
        }
        catch (ArgumentException)
        {
            rejected = true;
        }

        Check(rejected && bindings.FindConflicts(new InputEventKey { PhysicalKeycode = Key.W }).Contains(InputAction.Accelerate), "Invalid remap preserves previous bindings");
    }

    private void VerifyTapFocusAndTickCapture()
    {
        _player.Adapter.Capture(16);
        Send(new InputEventKey { PhysicalKeycode = Key.Tab, Pressed = true });
        Send(new InputEventKey { PhysicalKeycode = Key.Tab, Pressed = false });
        InputFrame tap = _player.Adapter.Capture(17);
        Check(tap.Held == 0 && tap.Pressed == InputButtons.Leaderboard && tap.Released == InputButtons.Leaderboard, "Native short tap survives to tick");
        Send(new InputEventKey { PhysicalKeycode = Key.W, Pressed = true });
        Send(new InputEventKey { PhysicalKeycode = Key.Tab, Pressed = true });
        _player.Adapter.Capture(18);
        _player._Notification((int)NotificationApplicationFocusOut);
        InputFrame unfocused = _player.Adapter.Capture(19);
        Check(unfocused.Accelerate == 0 && unfocused.Held == 0 && unfocused.Released == InputButtons.Leaderboard, "Focus loss neutralizes controls");
        Send(new InputEventKey { PhysicalKeycode = Key.W, Pressed = false });
        Send(new InputEventKey { PhysicalKeycode = Key.Tab, Pressed = false });
        _player._Notification((int)NotificationApplicationFocusIn);
        int frames = 0;
        _player.FrameCaptured += _ => frames++;
        _player._PhysicsProcess(1.0 / 60);
        _player._PhysicsProcess(1.0 / 60);
        Check(frames == 2 && _player.LatestFrame.Tick == 2, "One publication for each fixed callback");
    }

    private void Check(bool condition, string description)
    {
        _assertions++;
        if (!condition)
        {
            throw new InvalidOperationException(description);
        }
    }
}
