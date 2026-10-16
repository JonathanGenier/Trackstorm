namespace Trackstorm.Client.Items;

/// <summary>Session-local cosmetic controls, captured when a confirmed repair begins.</summary>
internal sealed record WrenchRepairSettings
{
    internal float Duration { get; init; } = .96f;
    internal float ContactFraction { get; init; } = .33f;
    internal float SparkIntensity { get; init; } = 1;
    internal static WrenchRepairSettings Current { get; set; } = new();
}
