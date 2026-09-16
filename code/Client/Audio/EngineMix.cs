namespace Trackstorm.Client.Audio;

/// <summary>Continuous equal-power idle/low/high gains driven by observed road speed.</summary>
internal readonly record struct EngineMix(float Idle, float Low, float High, float Pitch)
{
    /// <summary>Returns continuous gains across the observed speed range.</summary>
    /// <param name="speed">Observed horizontal road speed in metres per second.</param>
    /// <returns>The selected presentation value.</returns>
    internal static EngineMix Select(float speed)
    {
        float demand = float.IsFinite(speed) ? Math.Clamp(Math.Abs(speed) / 28, 0, 1) : 0;
        float blend = demand < 0.4f ? demand / 0.4f : (demand - 0.4f) / 0.6f;
        float lower = MathF.Cos(blend * MathF.PI / 2);
        float upper = MathF.Sin(blend * MathF.PI / 2);
        return demand < 0.4f
            ? new EngineMix(lower, upper, 0, 0.9f + (demand * 0.3f))
            : new EngineMix(0, lower, upper, 0.9f + (demand * 0.3f));
    }
}
