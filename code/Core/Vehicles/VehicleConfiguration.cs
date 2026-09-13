namespace Trackstorm.Core.Vehicles;

/// <summary>Validated arcade tuning shared by local, replay, and future authority drivers.</summary>
public sealed record VehicleConfiguration
{
    /// <summary>Hard-surface baseline; identity multipliers preserve ordinary driving.</summary>
    public SurfaceModifiers Concrete { get; init; } = new(1, 1, 1);
    /// <summary>Soft ground has less grip/acceleration and greater resistance.</summary>
    public SurfaceModifiers Mud { get; init; } = new(0.55f, 3, 0.6f);

    /// <summary>Fixed frequency; independent of rendering.</summary>
    public int TicksPerSecond { get; init; } = 60;
    /// <summary>Body mass in kilograms.</summary>
    public float Mass { get; init; } = 900;
    /// <summary>Forward acceleration in metres per second squared.</summary>
    public float Acceleration { get; init; } = 14;
    /// <summary>Braking deceleration.</summary>
    public float Braking { get; init; } = 25;
    /// <summary>Reverse acceleration.</summary>
    public float ReverseAcceleration { get; init; } = 8;
    /// <summary>Normal forward drive limit in metres per second.</summary>
    public float ForwardSpeed { get; init; } = 28;
    /// <summary>Reverse drive limit.</summary>
    public float ReverseSpeed { get; init; } = 11;
    /// <summary>Lateral grip response per second.</summary>
    public float Grip { get; init; } = 9;
    /// <summary>Reduced lateral grip while drifting.</summary>
    public float DriftGrip { get; init; } = 1.8f;
    /// <summary>Maximum steering angular speed in radians per second.</summary>
    public float SteeringRate { get; init; } = 1.9f;
    /// <summary>Minimum speed required to initiate/maintain drift.</summary>
    public float DriftMinimumSpeed { get; init; } = 7;
    /// <summary>Required continuous valid drift duration.</summary>
    public float DriftChargeSeconds { get; init; } = 0.65f;
    /// <summary>Boost duration after a charged release.</summary>
    public float BoostSeconds { get; init; } = 0.9f;
    /// <summary>Additional forward speed permitted during boost.</summary>
    public float BoostSpeed { get; init; } = 12;
    /// <summary>Gravity acceleration.</summary>
    public float Gravity { get; init; } = 24;
    /// <summary>Safety bound on total velocity, including external impulses.</summary>
    public float MaximumPhysicsSpeed { get; init; } = 65;
    /// <summary>Safety bound on angular velocity.</summary>
    public float MaximumAngularSpeed { get; init; } = 8;

    /// <summary>Resolves explicit surface tuning without engine or mutable state.</summary>
    /// <param name="surface">Supported surface identifier.</param>
    /// <returns>Configured handling multipliers.</returns>
    public SurfaceModifiers ResolveSurface(SurfaceType surface) => surface switch
    {
        SurfaceType.Concrete => Concrete,
        SurfaceType.Mud => Mud,
        _ => throw new ArgumentOutOfRangeException(nameof(surface)),
    };

    /// <summary>Rejects unsafe tuning before any state or native body is created.</summary>
    public void Validate()
    {
        if (TicksPerSecond is < 30 or > 240 || new[] { Mass, Acceleration, Braking, ReverseAcceleration, ForwardSpeed, ReverseSpeed, Grip, DriftGrip, SteeringRate, DriftMinimumSpeed, DriftChargeSeconds, BoostSeconds, BoostSpeed, Gravity, MaximumPhysicsSpeed, MaximumAngularSpeed }.Any(value => !float.IsFinite(value) || value <= 0 || value > 10000) || DriftGrip > Grip || ForwardSpeed + BoostSpeed > MaximumPhysicsSpeed || ReverseSpeed > ForwardSpeed)
        {
            throw new ArgumentException("Vehicle tuning requires finite positive values and consistent speed/grip limits.");
        }
    }
}
