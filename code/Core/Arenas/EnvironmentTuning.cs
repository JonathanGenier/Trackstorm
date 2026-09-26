namespace Trackstorm.Core.Arenas;

/// <summary>Live destruction balance within the fixed piece, displacement and wire safety bounds.</summary>
public sealed record EnvironmentTuning
{
    /// <summary>Multiplier on authored stage health; applies to the next accepted impact.</summary>
    public float HealthScale { get; init; } = 1;
    /// <summary>Harmless normal approach speed in m/s.</summary>
    public float ImpactThreshold { get; init; } = 3;
    /// <summary>Vehicle damage coefficient above the threshold.</summary>
    public float ImpactScale { get; init; } = 10;
    /// <summary>Broken-piece speed limit in m/s, bounded by the existing codec limit.</summary>
    public float PieceSpeed { get; init; } = 6;
    /// <summary>Fraction of impact speed transferred to a broken piece.</summary>
    public float PushScale { get; init; } = 0.35f;
    /// <summary>Velocity retained each fixed 60 Hz tick.</summary>
    public float VelocityRetention { get; init; } = 0.9f;

    /// <summary>Rejects unsafe live tuning before an authoritative transaction commits.</summary>
    public void Validate()
    {
        if (!float.IsFinite(HealthScale) || HealthScale is < 0.1f or > 10 ||
            !float.IsFinite(ImpactThreshold) || ImpactThreshold is < 0 or > 20 ||
            !float.IsFinite(ImpactScale) || ImpactScale is < 0 or > 100 ||
            !float.IsFinite(PieceSpeed) || PieceSpeed is < 0 or > 6 ||
            !float.IsFinite(PushScale) || PushScale is < 0 or > 1 ||
            !float.IsFinite(VelocityRetention) || VelocityRetention is < 0 or > 0.99f)
        {
            throw new ArgumentException("Invalid environment health, impact or bounded piece-motion tuning.");
        }
    }
}
