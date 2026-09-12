namespace Trackstorm.Core.Input;

/// <summary>Pure axis conditioning at the logical-input boundary, before integer quantization.</summary>
public static class InputAxis
{
    /// <summary>Clamps to [-1,1], removes and rescales a dead zone, then optionally negates. Non-finite samples are neutral.</summary>
    /// <param name="value">Signed logical sample.</param>
    /// <param name="deadZone">Neutral magnitude in [0,1).</param>
    /// <param name="inverted">Whether to reverse the signed result.</param>
    /// <returns>Conditioned signed strength.</returns>
    public static float Normalize(float value, float deadZone = 0, bool inverted = false)
    {
        if (!float.IsFinite(deadZone) || deadZone < 0 || deadZone >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(deadZone));
        }

        if (!float.IsFinite(value))
        {
            return 0;
        }

        value = Math.Clamp(value, -1, 1);
        float magnitude = Math.Abs(value);
        if (magnitude <= deadZone)
        {
            return 0;
        }

        float result = MathF.CopySign((magnitude - deadZone) / (1 - deadZone), value);
        return inverted ? -result : result;
    }

    /// <summary>Quantizes signed steering symmetrically to [-32767,32767], midpoint away from zero.</summary>
    /// <param name="value">Signed normalized strength.</param>
    /// <returns>Canonical integer steering.</returns>
    public static short QuantizeSteering(float value) => (short)MathF.Round(Normalize(value) * short.MaxValue, MidpointRounding.AwayFromZero);

    /// <summary>Quantizes a nonnegative pedal to [0,65535], midpoint away from zero.</summary>
    /// <param name="value">Normalized pedal strength.</param>
    /// <returns>Canonical integer pedal.</returns>
    public static ushort QuantizePedal(float value) => (ushort)MathF.Round(Math.Max(0, Normalize(value)) * ushort.MaxValue, MidpointRounding.AwayFromZero);
}
