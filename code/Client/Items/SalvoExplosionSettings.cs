namespace Trackstorm.Client.Items;

/// <summary>Device-local explosion preview tuning, captured per impact; never changes gameplay or flight trails.</summary>
internal sealed record SalvoExplosionSettings
{
    private static SalvoExplosionSettings _current = new();
    internal static SalvoExplosionSettings Current
    {
        get => _current;
        set
        {
            if (!float.IsFinite(value.FireworkScale) || value.FireworkScale is < 1 or > 20 ||
                !float.IsFinite(value.SmokeScale) || value.SmokeScale is < 1 or > 20 ||
                !float.IsFinite(value.BurstSpacing) || value.BurstSpacing is < .08f or > .3f ||
                !float.IsFinite(value.SmokeLifetime) || value.SmokeLifetime is < 5 or > 30 ||
                !float.IsFinite(value.SmokeOpacity) || value.SmokeOpacity is < .1f or > .8f ||
                !float.IsFinite(value.Brightness) || value.Brightness is < .5f or > 3)
                throw new ArgumentException("Invalid Salvo explosion cosmetics.");
            _current = value;
        }
    }
    internal float FireworkScale { get; init; } = 10;
    internal float SmokeScale { get; init; } = 10;
    internal float BurstSpacing { get; init; } = .16f;
    internal float SmokeLifetime { get; init; } = 20;
    internal float SmokeOpacity { get; init; } = .62f;
    internal float Brightness { get; init; } = 2.2f;
}
