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

        Set(InputAction.Accelerate, Key.W, Axis(JoyAxis.TriggerRight, 1, gamepadDevice));
        Set(InputAction.Brake, Key.S, Axis(JoyAxis.TriggerLeft, 1, gamepadDevice));
        Set(InputAction.SteerLeft, Key.A, Axis(JoyAxis.LeftX, -1, gamepadDevice));
        Set(InputAction.SteerRight, Key.D, Axis(JoyAxis.LeftX, 1, gamepadDevice));
        Set(InputAction.Drift, Key.Space, Button(JoyButton.A, gamepadDevice));
        Set(InputAction.UseItem, Key.E, Button(JoyButton.X, gamepadDevice));
        Set(InputAction.Leaderboard, Key.Tab, Button(JoyButton.Back, gamepadDevice));
        Set(InputAction.MenuUp, Key.Up, Button(JoyButton.DpadUp, gamepadDevice));
        Set(InputAction.MenuDown, Key.Down, Button(JoyButton.DpadDown, gamepadDevice));
        Set(InputAction.MenuLeft, Key.Left, Button(JoyButton.DpadLeft, gamepadDevice));
        Set(InputAction.MenuRight, Key.Right, Button(JoyButton.DpadRight, gamepadDevice));
        Set(InputAction.MenuAccept, Key.Enter, Button(JoyButton.A, gamepadDevice));
        Set(InputAction.MenuCancel, Key.Escape, Button(JoyButton.B, gamepadDevice));
        Set(InputAction.Pause, Key.P, Button(JoyButton.Start, gamepadDevice));
    }

    /// <summary>Stable names avoid overwriting Godot's built-in UI actions.</summary>
    /// <param name="action">Logical action.</param>
    /// <returns>The namespaced Godot action name.</returns>
    public static StringName Name(InputAction action) => $"trackstorm_{action}";

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
    /// <returns>The strongest binding's normalized nonnegative value.</returns>
    public float Strength(InputAction action, float deadZone)
    {
        float strength = 0;
        foreach (InputEvent binding in _bindings[action])
        {
            float value = binding switch
            {
                InputEventKey key => Godot.Input.IsPhysicalKeyPressed(key.PhysicalKeycode) ? 1 : 0,
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

    private void Set(InputAction action, Key key, InputEvent gamepad) => Replace(action, new InputEventKey { PhysicalKeycode = key }, gamepad);
}
