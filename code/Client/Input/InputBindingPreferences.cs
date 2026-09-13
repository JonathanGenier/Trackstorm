using System.Globalization;
using Godot;
using Trackstorm.Core.Input;
using Trackstorm.Core.Settings;

namespace Trackstorm.Client.Input;

/// <summary>Input-system-owned stable tokens for supported native bindings; malformed overrides retain defaults.</summary>
internal static class InputBindingPreferences
{
    /// <summary>Compact binding names for the settings editor, including the assigned gamepad.</summary>
    /// <param name="binding">Supported native binding.</param>
    /// <returns>A readable physical control label.</returns>
    public static string Describe(InputEvent binding) => binding switch
    {
        InputEventKey key => OS.GetKeycodeString(key.PhysicalKeycode),
        InputEventJoypadButton button => $"Pad {button.Device + 1}: {button.ButtonIndex}",
        InputEventJoypadMotion axis => $"Pad {axis.Device + 1}: {axis.Axis} {(axis.AxisValue < 0 ? "−" : "+")}",
        _ => binding.AsText(),
    };

    /// <summary>Captures owned input configuration for local persistence.</summary>
    /// <param name="adapter">Existing input owner.</param>
    /// <param name="settings">Other preferences to preserve.</param>
    /// <returns>Snapshot containing current input preferences.</returns>
    public static PlayerSettings Capture(PlayerInputAdapter adapter, PlayerSettings settings)
    {
        settings = settings with { InvertSteering = adapter.InvertSteering, DeadZone = adapter.DeadZone };
        foreach (InputAction action in Enum.GetValues<InputAction>())
        {
            InputEvent[] bindings = adapter.Bindings.CopyBindings(action);
            try
            {
                settings = settings.WithBindings(action, bindings.Select(Encode));
            }
            finally
            {
                foreach (InputEvent binding in bindings)
                {
                    binding.Dispose();
                }
            }
        }

        return settings;
    }

    /// <summary>Applies valid saved overrides to freshly installed defaults.</summary>
    /// <param name="adapter">Existing input owner.</param>
    /// <param name="settings">Saved preferences.</param>
    public static void Apply(PlayerInputAdapter adapter, PlayerSettings settings)
    {
        adapter.InvertSteering = settings.InvertSteering;
        adapter.DeadZone = (float)settings.DeadZone;
        foreach ((InputAction action, IReadOnlyList<string> tokens) in settings.Bindings)
        {
            var events = new List<InputEvent>();
            try
            {
                foreach (string token in tokens)
                {
                    InputEvent? binding = Decode(token);
                    if (binding is null)
                    {
                        break;
                    }

                    events.Add(binding);
                }

                if (events.Count == tokens.Count)
                {
                    adapter.Bindings.Replace(action, events.ToArray());
                }
            }
            finally
            {
                foreach (InputEvent binding in events)
                {
                    binding.Dispose();
                }
            }
        }
    }

    private static string Encode(InputEvent binding) => binding switch
    {
        InputEventKey key => FormattableString.Invariant($"key:{(long)key.PhysicalKeycode}"),
        InputEventJoypadButton button => FormattableString.Invariant($"button:{button.Device}:{(int)button.ButtonIndex}"),
        InputEventJoypadMotion axis => FormattableString.Invariant($"axis:{axis.Device}:{(int)axis.Axis}:{(int)axis.AxisValue}"),
        _ => throw new ArgumentException("Unsupported input binding.", nameof(binding)),
    };

    private static InputEvent? Decode(string token)
    {
        string[] parts = token.Split(':');
        long[] values = new long[parts.Length - 1];
        for (int index = 1; index < parts.Length; index++)
        {
            if (!long.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[index - 1]))
            {
                return null;
            }
        }

        if (parts is ["key", _] && values[0] > 0 && Enum.IsDefined((Key)values[0]))
        {
            return new InputEventKey { PhysicalKeycode = (Key)values[0] };
        }

        if (values.Length >= 2 && values[0] is >= 0 and <= int.MaxValue)
        {
            if (parts is ["button", _, _] && values[1] >= 0 && values[1] < (int)JoyButton.Max)
            {
                return new InputEventJoypadButton { Device = (int)values[0], ButtonIndex = (JoyButton)values[1] };
            }

            if (parts is ["axis", _, _, _] && values[1] >= 0 && values[1] < (int)JoyAxis.Max && values[2] is -1 or 1)
            {
                return new InputEventJoypadMotion { Device = (int)values[0], Axis = (JoyAxis)values[1], AxisValue = values[2] };
            }
        }

        return null;
    }
}
