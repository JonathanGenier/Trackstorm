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
            if (!Within(value.Speed, 300, 1000) || !Within(value.BulletLength, .4f, 2) ||
                !Within(value.TracerLength, 1, 6) || !Within(value.Width, .5f, 2) ||
                !Within(value.BulletBrightness, .25f, 2.5f) || !Within(value.TracerBrightness, 1, 4) || !Within(value.FlashScale, 0, 1.5f) ||
                !Within(value.SmokeIntensity, 0, 2))
                throw new ArgumentException("Invalid Machine Gun cosmetics.");
            _current = value;
        }
    }
    internal float Speed { get; init; } = 600;
    internal float BulletLength { get; init; } = 1.1f;
    internal float TracerLength { get; init; } = 4.2f;
    internal float Width { get; init; } = 1;
    internal float BulletBrightness { get; init; } = 1.35f;
    internal float TracerBrightness { get; init; } = 2.5f;
    internal float FlashScale { get; init; } = 1;
    internal float SmokeIntensity { get; init; } = 1;

    private static bool Within(float value, float min, float max) => float.IsFinite(value) && value >= min && value <= max;
}
