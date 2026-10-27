namespace Trackstorm.Client.Items;

/// <summary>Session-local cosmetics with fixed allocation/lifetime ceilings; no gameplay or ammunition authority.</summary>
internal sealed record MachineGunVfxSettings
{
    private static MachineGunVfxSettings _current = new();
    internal static MachineGunVfxSettings Current
    {
        get => _current;
        set
        {
            if (!Within(value.Speed, 300, 1000) || !Within(value.BulletLength, .4f, 5) ||
                !Within(value.TracerLength, 1, 8) || !Within(value.Width, .5f, 2) ||
                !Within(value.BulletBrightness, .25f, 6) || !Within(value.TracerBrightness, 1, 10) ||
                !Within(value.MinimumFlightSeconds, .04f, .12f) || !Within(value.FlashScale, 0, 1.5f) ||
                !Within(value.SmokeIntensity, 0, 2))
                throw new ArgumentException("Invalid Machine Gun cosmetics.");
            _current = value;
        }
    }
    internal float Speed { get; init; } = 600;
    internal float BulletLength { get; init; } = 3.2f;
    internal float TracerLength { get; init; } = 6;
    internal float Width { get; init; } = 1;
    internal float BulletBrightness { get; init; } = 2.8f;
    internal float TracerBrightness { get; init; } = 5;
    internal float MinimumFlightSeconds { get; init; } = .09f;
    internal float FlashScale { get; init; } = 1;
    internal float SmokeIntensity { get; init; } = 1;

    private static bool Within(float value, float min, float max) => float.IsFinite(value) && value >= min && value <= max;
}
