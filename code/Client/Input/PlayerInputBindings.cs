using Godot;
using Trackstorm.Core.Input;

namespace Trackstorm.Client.Input;

/// <summary>Owns namespaced Godot InputMap actions and runtime replacement of their physical bindings.</summary>
internal sealed class PlayerInputBindings : IDisposable
{
    private readonly Dictionary<InputAction, InputEvent[]> _bindings = new();

    /// <summary>Installs defaults for one local keyboard and gamepad.</summary>
    /// <param name="gamepadDevice">The assigned nonnegative Godot gamepad ID.</param>
    public PlayerInputBindings(int gamepadDevice = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gamepadDevice);
        foreach (InputAction action in Enum.GetValues<InputAction>())
        {
            if (!InputMap.HasAction(Name(action)))
            {
                InputMap.AddAction(Name(action), 0);
            }

            _bindings[action] = [];
        }

        RestoreDefaults(gamepadDevice);
    }

    /// <summary>Stable names avoid overwriting Godot's built-in UI actions.</summary>
    /// <param name="action">Logical action.</param>
    /// <returns>The namespaced Godot action name.</returns>
    public static StringName Name(InputAction action) => $"trackstorm_{action}";

    /// <summary>Restores the input system's keyboard/gamepad defaults without adding another InputMap owner.</summary>
    /// <param name="gamepadDevice">Assigned nonnegative gamepad ID.</param>
    public void RestoreDefaults(int gamepadDevice = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gamepadDevice);
        Set(InputAction.Accelerate, Key.W, Axis(JoyAxis.TriggerRight, 1, gamepadDevice));
        Set(InputAction.Brake, Key.S, Axis(JoyAxis.TriggerLeft, 1, gamepadDevice));
        Set(InputAction.SteerLeft, Key.A, Axis(JoyAxis.LeftX, -1, gamepadDevice));
        Set(InputAction.SteerRight, Key.D, Axis(JoyAxis.LeftX, 1, gamepadDevice));
        Set(InputAction.Drift, Key.Space, Button(JoyButton.B, gamepadDevice));
        using var mouse = new InputEventMouseButton { ButtonIndex = MouseButton.Left };
        using var itemButton = Button(JoyButton.A, gamepadDevice);
        Replace(InputAction.UseItem, mouse, itemButton);
        Set(InputAction.Leaderboard, Key.Tab, Button(JoyButton.Back, gamepadDevice));
        Set(InputAction.MenuUp, Key.Up, Button(JoyButton.DpadUp, gamepadDevice));
        Set(InputAction.MenuDown, Key.Down, Button(JoyButton.DpadDown, gamepadDevice));
        Set(InputAction.MenuLeft, Key.Left, Button(JoyButton.DpadLeft, gamepadDevice));
        Set(InputAction.MenuRight, Key.Right, Button(JoyButton.DpadRight, gamepadDevice));
        Set(InputAction.MenuAccept, Key.Enter, Button(JoyButton.A, gamepadDevice));
        Set(InputAction.MenuCancel, Key.Escape, Button(JoyButton.B, gamepadDevice));
        Set(InputAction.Pause, Key.P, Button(JoyButton.Start, gamepadDevice));
        using var left = Axis(JoyAxis.RightX, -1, gamepadDevice);
        using var right = Axis(JoyAxis.RightX, 1, gamepadDevice);
        using var up = Axis(JoyAxis.RightY, -1, gamepadDevice);
        using var down = Axis(JoyAxis.RightY, 1, gamepadDevice);
        Replace(InputAction.CameraLeft, left);
        Replace(InputAction.CameraRight, right);
        Replace(InputAction.CameraUp, up);
        Replace(InputAction.CameraDown, down);
    }

    /// <summary>Returns caller-owned copies for settings capture and presentation.</summary>
    /// <param name="action">Logical action to inspect.</param>
    /// <returns>Native events that the caller must dispose.</returns>
    public InputEvent[] CopyBindings(InputAction action) => _bindings[action].Select(binding => (InputEvent)binding.Duplicate()).ToArray();

    /// <summary>Atomically validates and replaces an action's bindings; empty unbinds. Shared bindings are intentional and permitted.</summary>
    /// <param name="action">Action to remap.</param>
    /// <param name="bindings">Replacement physical bindings, copied to prevent caller mutation.</param>
    public void Replace(InputAction action, params InputEvent[] bindings)
    {
        if (!_bindings.ContainsKey(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }

        ArgumentNullException.ThrowIfNull(bindings);
        foreach (InputEvent binding in bindings)
        {
            bool valid = binding switch
            {
                InputEventKey key => key.PhysicalKeycode != Key.None && key.Keycode == Key.None && !key.CtrlPressed && !key.AltPressed && !key.ShiftPressed && !key.MetaPressed,
                InputEventMouseButton mouse => mouse.ButtonIndex is >= MouseButton.Left and <= MouseButton.Xbutton2 && !mouse.CtrlPressed && !mouse.AltPressed && !mouse.ShiftPressed && !mouse.MetaPressed,
                InputEventJoypadButton button => button.Device >= 0 && button.ButtonIndex >= 0 && button.ButtonIndex < JoyButton.Max,
                InputEventJoypadMotion axis => axis.Device >= 0 && axis.Axis >= 0 && axis.Axis < JoyAxis.Max && Math.Abs(axis.AxisValue) == 1,
                _ => false,
            };
            if (!valid)
            {
                throw new ArgumentException("Use an unmodified physical key, device-specific gamepad button, or signed gamepad axis.", nameof(bindings));
            }
        }

        InputEvent[] copies = bindings.Select(binding => (InputEvent)binding.Duplicate()).ToArray();
        InputMap.ActionEraseEvents(Name(action));
        foreach (InputEvent binding in copies)
        {
            InputMap.ActionAddEvent(Name(action), binding);
        }

        foreach (InputEvent previous in _bindings[action])
        {
            previous.Dispose();
        }

        _bindings[action] = copies;
    }

    /// <summary>Reports shared bindings so a future settings UI can explain conflicts before replacement.</summary>
    /// <param name="binding">Candidate physical binding.</param>
    /// <returns>Actions currently sharing the binding.</returns>
    public InputAction[] FindConflicts(InputEvent binding) => _bindings.Keys
        .Where(action => InputMap.ActionHasEvent(Name(action), binding)).ToArray();

    /// <summary>Resolves every physical binding individually so releasing one cannot cancel another held binding.</summary>
    /// <param name="action">Logical action to resolve.</param>
    /// <param name="deadZone">Neutral magnitude applied only to analog bindings.</param>
    /// <param name="analog">Optional analog/digital filter before intent shaping.</param>
    /// <param name="ignoreTextKeys">Keeps printable keys in a focused search editor; other input consumers retain normal bindings.</param>
    /// <returns>The strongest binding's normalized nonnegative value.</returns>
    public float Strength(InputAction action, float deadZone, bool? analog = null, bool ignoreTextKeys = false)
    {
        float strength = 0;
        foreach (InputEvent binding in _bindings[action])
        {
            if (ignoreTextKeys && binding is InputEventKey { PhysicalKeycode: >= Key.Space and < Key.Escape })
            {
                continue;
            }

            if (analog.HasValue && (binding is InputEventJoypadMotion) != analog.Value)
            {
                continue;
            }

            float value = binding switch
            {
                InputEventKey key => Godot.Input.IsPhysicalKeyPressed(key.PhysicalKeycode) ? 1 : 0,
                InputEventMouseButton mouse => Godot.Input.IsMouseButtonPressed(mouse.ButtonIndex) ? 1 : 0,
                InputEventJoypadButton button => Godot.Input.IsJoyButtonPressed(button.Device, button.ButtonIndex) ? 1 : 0,
                InputEventJoypadMotion axis => Math.Max(0, InputAxis.Normalize(Godot.Input.GetJoyAxis(axis.Device, axis.Axis), deadZone) * axis.AxisValue),
                _ => 0,
            };
            strength = Math.Max(strength, value);
        }

        return strength;
    }

    /// <summary>Removes owned mappings and releases native binding resources when the local input owner exits.</summary>
    public void Dispose()
    {
        foreach ((InputAction action, InputEvent[] bindings) in _bindings)
        {
            InputMap.EraseAction(Name(action));
            foreach (InputEvent binding in bindings)
            {
                binding.Dispose();
            }
        }

        _bindings.Clear();
    }

    private static InputEventJoypadMotion Axis(JoyAxis axis, float direction, int device) => new() { Axis = axis, AxisValue = direction, Device = device };

    private static InputEventJoypadButton Button(JoyButton button, int device) => new() { ButtonIndex = button, Device = device };

    private void Set(InputAction action, Key key, InputEvent gamepad)
    {
        using (gamepad)
        {
            using var keyboard = new InputEventKey { PhysicalKeycode = key };
            Replace(action, keyboard, gamepad);
        }
    }
}
