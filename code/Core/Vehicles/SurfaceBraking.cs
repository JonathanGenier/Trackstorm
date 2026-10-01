namespace Trackstorm.Core.Vehicles;

/// <summary>Surface response to deliberate braking, separate from ordinary driving purchase.</summary>
public sealed record SurfaceBraking
{
    /// <summary>Service and rear-lock braking demand and longitudinal traction multiplier.</summary>
    public float Deceleration { get; init; } = 1;
    /// <summary>Rear lateral purchase retained at full service braking; front tires retain its square root.</summary>
    public float BrakeLateralGrip { get; init; } = 1;
    /// <summary>Multiplier on the global rear lateral purchase retained under a moving handbrake.</summary>
    public float HandbrakeLateralGrip { get; init; } = 1;

    /// <summary>Rejects invalid surface response before a configuration can be accepted.</summary>
    public void Validate()
    {
        if (!float.IsFinite(Deceleration) || Deceleration is < 0 or > 2 ||
            !float.IsFinite(BrakeLateralGrip) || BrakeLateralGrip is < 0 or > 1 ||
            !float.IsFinite(HandbrakeLateralGrip) || HandbrakeLateralGrip is < 0 or > 2)
        {
            throw new ArgumentException("Invalid surface braking response.");
        }
    }
}
