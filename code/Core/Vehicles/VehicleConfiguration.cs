namespace Trackstorm.Core.Vehicles;

/// <summary>Validated arcade tuning shared by local, replay, and future authority drivers.</summary>
public sealed record VehicleConfiguration
{
    /// <summary>Cast surface, slightly below the unmodified asphalt baseline.</summary>
    public SurfaceModifiers Concrete { get; init; } = new(0.95f, 1, 0.98f);
    /// <summary>Compacted soil retains controllable drive with modest rolling resistance.</summary>
    public SurfaceModifiers Dirt { get; init; } = new(0.85f, 1.15f, 0.95f);
    /// <summary>Vegetation reduces tire purchase and adds rolling resistance.</summary>
    public SurfaceModifiers Grass { get; init; } = new(0.72f, 1.4f, 0.9f);
    /// <summary>Soft ground has less grip/acceleration and greater resistance.</summary>
    public SurfaceModifiers Mud { get; init; } = new(0.6f, 2.5f, 0.85f);
    /// <summary>Saturated soil bogs at speed while retaining usable low-speed drive.</summary>
    public SurfaceModifiers DeepMud { get; init; } = new(0.5f, 5, 0.8f);
    /// <summary>Traversable water is more restrictive than saturated soil.</summary>
    public SurfaceModifiers Water { get; init; } = new(0.45f, 8, 0.65f);
    /// <summary>Immersion measured from nominal tire level at which damage begins, in metres.</summary>
    public float DeepWaterDepth { get; init; } = 1.0f;
    /// <summary>Continuous deep-water damage per second, through ordinary health authority.</summary>
    public float WaterDamagePerSecond { get; init; } = 250;

    /// <summary>Fixed frequency; independent of rendering.</summary>
    public int TicksPerSecond { get; init; } = 60;
    /// <summary>Body mass in kilograms.</summary>
    public float Mass { get; init; } = 900;
    /// <summary>Forward acceleration in metres per second squared.</summary>
    public float Acceleration { get; init; } = 11;
    /// <summary>Braking deceleration.</summary>
    public float Braking { get; init; } = 14;
    /// <summary>Residual opposing speed snapped to rest before reversing, in m/s.</summary>
    public float StopSpeed { get; init; } = 0.05f;
    /// <summary>Reverse acceleration.</summary>
    public float ReverseAcceleration { get; init; } = 8;
    /// <summary>Normal forward drive limit in metres per second.</summary>
    public float ForwardSpeed { get; init; } = 44.44f;
    /// <summary>Reverse drive limit.</summary>
    public float ReverseSpeed { get; init; } = 11;
    /// <summary>Lateral grip response per second.</summary>
    public float Grip { get; init; } = 12;
    /// <summary>Maximum low-speed wheel angle in radians.</summary>
    public float SteeringAngle { get; init; } = 0.6f;
    /// <summary>Speed in m/s at which wheel authority starts calming substantially.</summary>
    public float SteeringSpeed { get; init; } = 11;
    /// <summary>Wheel angle transition rate in radians per second.</summary>
    public float SteeringResponse { get; init; } = 2.4f;
    /// <summary>Distance between axle centers in metres.</summary>
    public float Wheelbase { get; init; } = VehicleDimensions.Wheelbase;
    /// <summary>Tire friction coefficient; combined demands share this budget.</summary>
    public float TireFriction { get; init; } = 1.65f;
    /// <summary>Rear traction share available to propulsion despite lateral saturation; zero disables allocation.</summary>
    public float DriveTractionReserve { get; init; } = 0.55f;
    /// <summary>Effective center-of-mass height for longitudinal/lateral load transfer.</summary>
    public float LoadHeight { get; init; } = 0.45f * VehicleDimensions.Scale;
    /// <summary>Rear braking deceleration at reference mass.</summary>
    public float HandbrakeBraking { get; init; } = 12;
    /// <summary>Rear lateral grip fraction with the handbrake fully engaged.</summary>
    public float HandbrakeGrip { get; init; } = 0.6f;
    /// <summary>Handbrake application response per second.</summary>
    public float HandbrakeResponse { get; init; } = 4;
    /// <summary>Handbrake release response per second, permitting gradual traction recovery.</summary>
    public float TractionRecovery { get; init; } = 3;
    /// <summary>Horizontal overspeed recovery in m/s² above the effective drive limit.</summary>
    public float OverspeedDeceleration { get; init; } = 3;
    /// <summary>Rolling resistance per second.</summary>
    public float CoastDrag { get; init; } = 0.5f;
    /// <summary>Reference mass for engine and brake forces, so heavier tuning retains inertia.</summary>
    public float ReferenceMass { get; init; } = 900;
    /// <summary>Pitch/roll spring stiffness per second squared.</summary>
    public float SuspensionSpring { get; init; } = 32;
    /// <summary>Pitch/roll damper rate per second.</summary>
    public float SuspensionDamping { get; init; } = 8;
    /// <summary>Chassis pitch/roll target radians per m/s squared of tire acceleration.</summary>
    public float ChassisCompliance { get; init; } = 0.004f;
    /// <summary>Maximum load-induced chassis tilt in radians.</summary>
    public float MaximumChassisTilt { get; init; } = 0.16f;
    /// <summary>Weak yaw damping; never targets a commanded yaw or drift angle.</summary>
    public float StabilityDamping { get; init; } = 0.65f;
    /// <summary>Fully extended suspension ray length in metres.</summary>
    public float SuspensionLength { get; init; } = VehicleDimensions.RideHeight + (9.81f / 100);
    /// <summary>Vertical spring stiffness per unit sprung mass.</summary>
    public float WheelSpring { get; init; } = 100;
    /// <summary>Vertical wheel damping per unit sprung mass.</summary>
    public float WheelDamping { get; init; } = 20;
    /// <summary>Gravity acceleration.</summary>
    public float Gravity { get; init; } = 9.81f;
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
        SurfaceType.Asphalt => new(1, 1, 1),
        SurfaceType.Dirt => Dirt,
        SurfaceType.Grass => Grass,
        SurfaceType.DeepMud => DeepMud,
        SurfaceType.Water => Water,
        _ => throw new ArgumentOutOfRangeException(nameof(surface)),
    };

    /// <summary>Rejects unsafe tuning before any state or native body is created.</summary>
    public void Validate()
    {
        if (!float.IsFinite(DeepWaterDepth) || DeepWaterDepth is < 0.01f or > 100 ||
            !float.IsFinite(WaterDamagePerSecond) || WaterDamagePerSecond is < 0 or > 10000)
        {
            throw new ArgumentException("Water depth must be 0.01–100 metres and damage 0–10000 HP/s.");
        }
        if (!float.IsFinite(DriveTractionReserve) || DriveTractionReserve < 0 || DriveTractionReserve > 1)
        {
            throw new ArgumentException("Drive traction reserve must be a finite fraction.");
        }

        if (TicksPerSecond is < 30 or > 240 || new[] { OverspeedDeceleration, StopSpeed, SuspensionLength, WheelSpring, WheelDamping, Mass, Acceleration, Braking, ReverseAcceleration, ForwardSpeed, ReverseSpeed, Grip, SteeringAngle, SteeringSpeed, SteeringResponse, Wheelbase, TireFriction, LoadHeight, HandbrakeBraking, HandbrakeGrip, HandbrakeResponse, TractionRecovery, CoastDrag, ReferenceMass, SuspensionSpring, SuspensionDamping, ChassisCompliance, MaximumChassisTilt, Gravity, MaximumPhysicsSpeed, MaximumAngularSpeed }.Any(value => !float.IsFinite(value) || value <= 0 || value > 10000) || SuspensionLength > 1 || HandbrakeGrip > 1 || SteeringAngle > 1 || MaximumChassisTilt > 0.5f || !float.IsFinite(StabilityDamping) || StabilityDamping < 0 || StabilityDamping > 1 || ForwardSpeed > MaximumPhysicsSpeed || ReverseSpeed > ForwardSpeed)
        {
            throw new ArgumentException("Vehicle tuning requires finite positive values and consistent speed/grip limits.");
        }
    }
}
