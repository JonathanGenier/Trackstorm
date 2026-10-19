namespace Trackstorm.Core.Items;

/// <summary>Authoritative EMP flight tuning; ammunition and attached EMP effects have separate owners.</summary>
public sealed record EmpClawConfiguration
{
    /// <summary>Initial speed in metres per second, always above 200 km/h.</summary>
    public float LaunchSpeed { get; init; } = 65;
    /// <summary>Sustained speed in metres per second; default is 100 km/h.</summary>
    public float CruiseSpeed { get; init; } = 100f / 3.6f;
    /// <summary>Launch deceleration in metres per second squared.</summary>
    public float Deceleration { get; init; } = 75;
    /// <summary>Maximum homing direction change in degrees per second.</summary>
    public float TurnRate { get; init; } = 100;
    /// <summary>First-order homing response per second.</summary>
    public float Response { get; init; } = 5;
    /// <summary>Forward acquisition cone half angle in degrees.</summary>
    public float AcquisitionDegrees { get; init; } = 18;
    /// <summary>Maximum unlocked-flight acquisition distance in metres; firing locks use the shared 300 m aim bound.</summary>
    public float AcquisitionRange { get; init; } = 120;
    /// <summary>Captured flight budget, never more than sixty seconds.</summary>
    public float LifetimeSeconds { get; init; } = 60;
    /// <summary>Independent EMP tuning using the established missile terrain response.</summary>
    public MissileTerrainConfiguration Terrain { get; init; } = new() { LookAhead = 12, TurnRate = 150, Response = 16 };

    /// <summary>Rejects invalid or contradictory phase and sensing bounds.</summary>
    public void Validate()
    {
        if (!float.IsFinite(LaunchSpeed) || LaunchSpeed is <= (200f / 3.6f) or > 150 ||
            !float.IsFinite(CruiseSpeed) || CruiseSpeed is < 10 or > 50 || CruiseSpeed >= LaunchSpeed ||
            !float.IsFinite(Deceleration) || Deceleration is < 10 or > 200 ||
            !float.IsFinite(TurnRate) || TurnRate is < 1 or > 180 ||
            !float.IsFinite(Response) || Response is < .1f or > 20 ||
            !float.IsFinite(AcquisitionDegrees) || AcquisitionDegrees is < 1 or > 60 ||
            !float.IsFinite(AcquisitionRange) || AcquisitionRange is < 5 or > 300 ||
            !float.IsFinite(LifetimeSeconds) || LifetimeSeconds is < .1f or > 60)
        { throw new ArgumentException("Invalid EMP flight configuration."); }
        ArgumentNullException.ThrowIfNull(Terrain);
        Terrain.Validate();
    }
}
