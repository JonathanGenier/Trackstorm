namespace Trackstorm.Core.Items;

/// <summary>Bounded terrain sensing and pitch-only guidance for standard missiles.</summary>
public sealed record MissileTerrainConfiguration
{
    /// <summary>Desired vertical clearance above drivable ground, in metres.</summary>
    public float Clearance { get; init; } = 1;
    /// <summary>Distance along committed flight used to sample the upcoming surface, in metres.</summary>
    public float LookAhead { get; init; } = 30;
    /// <summary>Vertical extent on either side of the forward sample, in metres.</summary>
    public float DetectionRange { get; init; } = 10;
    /// <summary>Maximum height above current supporting ground at which correction can engage.</summary>
    public float ReacquisitionHeight { get; init; } = 3;
    /// <summary>Maximum pitch magnitude requested by terrain correction, in degrees.</summary>
    public float PitchLimit { get; init; } = 35;
    /// <summary>Maximum pitch change per second, in degrees. Zero disables correction.</summary>
    public float TurnRate { get; init; } = 45;
    /// <summary>First-order pitch response per second; the rate cap still applies.</summary>
    public float Response { get; init; } = 8;
    /// <summary>Maximum suitable surface inclination from horizontal, in degrees.</summary>
    public float SlopeLimit { get; init; } = 40;
    /// <summary>Maximum downward discontinuity beyond either sampled tangent, in metres.</summary>
    public float DropTolerance { get; init; } = 1;

    /// <summary>Rejects nonfinite, excessive or contradictory terrain envelopes.</summary>
    public void Validate()
    {
        if (!float.IsFinite(Clearance) || Clearance is < 0.25f or > 3 ||
            !float.IsFinite(LookAhead) || LookAhead is < 2 or > 60 ||
            !float.IsFinite(DetectionRange) || DetectionRange is < 1 or > 20 ||
            !float.IsFinite(ReacquisitionHeight) || ReacquisitionHeight <= Clearance || ReacquisitionHeight > DetectionRange ||
            !float.IsFinite(PitchLimit) || PitchLimit is < 1 or > 60 ||
            !float.IsFinite(TurnRate) || TurnRate is < 0 or > 90 ||
            !float.IsFinite(Response) || Response is < 0.1f or > 12 ||
            !float.IsFinite(SlopeLimit) || SlopeLimit is < 1 or > 60 ||
            !float.IsFinite(DropTolerance) || DropTolerance is < 0 or > 3)
        { throw new ArgumentException("Invalid Missile terrain envelope."); }
    }
}
