namespace Trackstorm.Core.Input;

/// <summary>Accumulates logical button transitions between ticks, preserving short taps and independent controls.</summary>
public sealed class InputFrameCapture
{
    private InputButtons _held;
    private InputButtons _pressed;
    private InputButtons _released;

    /// <summary>Observes the aggregate logical button state, never an individual physical binding.</summary>
    /// <param name="held">Current combined logical held state.</param>
    public void Observe(InputButtons held)
    {
        _ = new InputFrame(0, 0, 0, 0, held, InputButtons.None, InputButtons.None);
        _pressed |= held & ~_held;
        _released |= _held & ~held;
        _held = held;
    }

    /// <summary>Captures one tick and consumes pending edges once. The caller owns tick scheduling.</summary>
    /// <param name="tick">Simulation tick supplied by the caller.</param>
    /// <param name="steering">Quantized signed steering.</param>
    /// <param name="accelerate">Quantized throttle.</param>
    /// <param name="brake">Quantized brake.</param>
    /// <returns>The frame with all pending edges.</returns>
    public InputFrame Capture(ulong tick, short steering, ushort accelerate, ushort brake)
    {
        var frame = new InputFrame(tick, steering, accelerate, brake, _held, _pressed, _released);
        _pressed = InputButtons.None;
        _released = InputButtons.None;
        return frame;
    }
}
