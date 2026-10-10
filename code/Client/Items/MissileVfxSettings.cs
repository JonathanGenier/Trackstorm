namespace Trackstorm.Client.Items;

/// <summary>Bounded device-local live preview controls; no gameplay or wire ownership.</summary>
internal sealed record MissileVfxSettings
{
    internal float FlameLength { get; init; } = 1.15f;
    internal float FlameWidth { get; init; } = .22f;
    internal float SmokeLifetime { get; init; } = .65f;
    internal float SmokeSize { get; init; } = .65f;
    internal float SmokeOpacity { get; init; } = .42f;
    internal float Density { get; init; } = 1;
    internal float ConfettiLifetime { get; init; } = .38f;
    internal float ConfettiSize { get; init; } = .085f;
    internal float EmberLifetime { get; init; } = .45f;
    internal static MissileVfxSettings Current { get; set; } = new();
}
