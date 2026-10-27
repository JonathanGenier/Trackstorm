namespace Trackstorm.Client.Items;

/// <summary>Bounded device-local cosmetics. Never changes attachment deadlines or damage queries.</summary>
internal sealed record EmpClawVfxSettings
{
    private static EmpClawVfxSettings _current = new();
    internal static EmpClawVfxSettings Current
    {
        get => _current;
        set
        {
            if (!float.IsFinite(value.ShockwaveScale) || value.ShockwaveScale is < .25f or > 2 ||
                !float.IsFinite(value.TrailLength) || value.TrailLength is < .5f or > 5 ||
                !float.IsFinite(value.ElectricIntensity) || value.ElectricIntensity is < 0 or > 2 ||
                !float.IsFinite(value.ImpactIntensity) || value.ImpactIntensity is < 0 or > 2 ||
                !float.IsFinite(value.DeflationSeconds) || value.DeflationSeconds is < .1f or > .8f ||
                !float.IsFinite(value.CleanupSeconds) || value.CleanupSeconds is < .8f or > 2)
                throw new ArgumentException("Invalid local EMP cosmetics.");
            _current = value;
        }
    }
    internal float ShockwaveScale { get; init; } = 1;
    internal float TrailLength { get; init; } = 2.8f;
    internal float ElectricIntensity { get; init; } = 1;
    internal float ImpactIntensity { get; init; } = 1;
    internal float DeflationSeconds { get; init; } = .38f;
    internal float CleanupSeconds { get; init; } = 1.3f;
}
