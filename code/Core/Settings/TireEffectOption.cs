namespace Trackstorm.Core.Settings;

/// <summary>A local graphics control with explicit units, defaults and safe runtime bounds.</summary>
public sealed record TireEffectOption(string Key, string Group, string Label, float Default, float Minimum, float Maximum, bool Integral = false)
{
    /// <summary>Whether input is finite, within bounds, and integral where required.</summary>
    public bool Accepts(double value) => double.IsFinite(value) && (float)value >= Minimum && (float)value <= Maximum && (!Integral || value == Math.Truncate(value));
}
