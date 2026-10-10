namespace Trackstorm.Client.Items;

/// <summary>Device-local cosmetic tuning captured by each confirmed explosion; never enters authority.</summary>
internal sealed record MissileExplosionSettings
{
    internal float Intensity { get; init; } = 1;
    internal float FireScale { get; init; } = 1;
    internal float ReachCarLengths { get; init; } = 2.25f;
    internal float Density { get; init; } = 1;
    internal float Duration { get; init; } = 1.6f;
    internal static MissileExplosionSettings Current { get; set; } = new();

    internal MissileExplosionSettings Bounded() => this with
    {
        Intensity = Bound(Intensity, .2f, 2, 1),
        FireScale = Bound(FireScale, .5f, 1.5f, 1),
        ReachCarLengths = Bound(ReachCarLengths, 1, 3, 2.25f),
        Density = Bound(Density, 0, 2, 1),
        Duration = Bound(Duration, .8f, 2.4f, 1.6f),
    };

    private static float Bound(float value, float minimum, float maximum, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}
