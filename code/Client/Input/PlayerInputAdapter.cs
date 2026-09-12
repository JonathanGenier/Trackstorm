using Trackstorm.Core.Input;

namespace Trackstorm.Client.Input;

/// <summary>Converts physical binding strengths to conditioned Core frames. Call Observe after events and Capture once per tick.</summary>
internal sealed class PlayerInputAdapter
{
    private static readonly (InputAction Action, InputButtons Button)[] DigitalActions =
    [
        (InputAction.Drift, InputButtons.Drift),
        (InputAction.UseItem, InputButtons.UseItem),
        (InputAction.Leaderboard, InputButtons.Leaderboard),
        (InputAction.MenuUp, InputButtons.MenuUp),
        (InputAction.MenuDown, InputButtons.MenuDown),
        (InputAction.MenuLeft, InputButtons.MenuLeft),
        (InputAction.MenuRight, InputButtons.MenuRight),
        (InputAction.MenuAccept, InputButtons.MenuAccept),
        (InputAction.MenuCancel, InputButtons.MenuCancel),
        (InputAction.Pause, InputButtons.Pause),
    ];

    private readonly InputFrameCapture _capture = new();
    private float _deadZone = 0.15f;

    /// <summary>Creates an adapter around the Client-owned mapping.</summary>
    /// <param name="bindings">The single local player's bindings.</param>
    public PlayerInputAdapter(PlayerInputBindings bindings)
    {
        Bindings = bindings;
    }

    /// <summary>Runtime remapping entry point.</summary>
    public PlayerInputBindings Bindings { get; }

    /// <summary>Inverts signed steering after resolving bindings and before quantization.</summary>
    public bool InvertSteering { get; set; }

    /// <summary>Analog dead zone; digital keys/buttons remain full strength.</summary>
    public float DeadZone
    {
        get => _deadZone;
        set
        {
            _ = InputAxis.Normalize(0, value);
            _deadZone = value;
        }
    }

    /// <summary>Suppresses capture while focus is lost; observing suppression releases held controls.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Samples aggregate digital state; preserves press/release transitions until capture.</summary>
    public void Observe()
    {
        InputButtons held = InputButtons.None;
        if (Enabled)
        {
            foreach ((InputAction action, InputButtons button) in DigitalActions)
            {
                if (Bindings.Strength(action, DeadZone) > 0.5f)
                {
                    held |= button;
                }
            }
        }

        _capture.Observe(held);
    }

    /// <summary>Produces one logical frame with no device data. Opposite steering bindings cancel.</summary>
    /// <param name="tick">Caller-owned simulation tick.</param>
    /// <returns>Conditioned and quantized logical input.</returns>
    public InputFrame Capture(ulong tick)
    {
        Observe();
        float steering = Enabled ? Bindings.Strength(InputAction.SteerRight, DeadZone) - Bindings.Strength(InputAction.SteerLeft, DeadZone) : 0;
        return _capture.Capture(
            tick,
            InputAxis.QuantizeSteering(InputAxis.Normalize(steering, inverted: InvertSteering)),
            InputAxis.QuantizePedal(Enabled ? Bindings.Strength(InputAction.Accelerate, DeadZone) : 0),
            InputAxis.QuantizePedal(Enabled ? Bindings.Strength(InputAction.Brake, DeadZone) : 0));
    }
}
