namespace Trackstorm.Core.Items;

/// <summary>Shared direct-fire articulation and local input-friction tuning, hosted with item configuration.</summary>
public sealed record WeaponAimConfiguration
{
    /// <summary>Maximum yaw/pitch travel in degrees per second.</summary>
    public float TurnRate { get; init; } = 540;
    /// <summary>Maximum upward elevation relative to the chassis.</summary>
    public float UpDegrees { get; init; } = 70;
    /// <summary>Maximum downward elevation, further restricted by chassis clearance.</summary>
    public float DownDegrees { get; init; } = 40;
    /// <summary>Near-target assistance cone in degrees; zero disables assistance.</summary>
    public float AssistDegrees { get; init; } = 5;
    /// <summary>Maximum mouse slowdown; fast deliberate motion bypasses it.</summary>
    public float MouseFriction { get; init; } = .08f;
    /// <summary>Maximum stick slowdown; full-stick turns bypass it.</summary>
    public float StickFriction { get; init; } = .28f;

    /// <summary>Rejects nonfinite and unsafe shared tuning.</summary>
    public void Validate()
    {
        if (!float.IsFinite(TurnRate) || TurnRate is < 60 or > 1440 ||
            !float.IsFinite(UpDegrees) || UpDegrees is < 0 or > 80 ||
            !float.IsFinite(DownDegrees) || DownDegrees is < 0 or > 60 ||
            !float.IsFinite(AssistDegrees) || AssistDegrees is < 0 or > 8 ||
            !float.IsFinite(MouseFriction) || MouseFriction is < 0 or > .2f ||
            !float.IsFinite(StickFriction) || StickFriction is < 0 or > .5f)
        { throw new ArgumentException("Invalid weapon aiming tuning."); }
    }
}
