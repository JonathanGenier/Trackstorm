namespace Trackstorm.Client.Items;

/// <summary>Bounded session-local cosmetics; travel captured on equip, repair and VFX on accepted use.</summary>
internal sealed record WrenchRepairSettings
{
    internal float Duration { get; init; } = .96f;
    internal float ContactFraction { get; init; } = .33f;
    internal float SparkIntensity { get; init; } = 1;
    internal float DeploymentDuration { get; init; } = .9f;
    internal float RetractionDuration { get; init; } = .9f;
    internal float ContactSpacing { get; init; } = .50f;
    internal float ContactForward { get; init; }
    internal float HoverHeight { get; init; } = .20f;
    internal float Vibration { get; init; } = .001f;
    internal float VibrationFrequency { get; init; } = 180;
    internal float DrillSpeed { get; init; } = 85;
    internal float SparkLifetime { get; init; } = .16f;
    internal float SparkSize { get; init; } = 1;
    internal float SparkSpeed { get; init; } = 1;
    internal static WrenchRepairSettings Current { get; set; } = new();
}
