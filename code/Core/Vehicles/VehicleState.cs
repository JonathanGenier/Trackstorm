namespace Trackstorm.Core.Vehicles;

/// <summary>Serializable pre-solver snapshot: observed pose, next velocities, and all movement memory and feedback.</summary>
public readonly record struct VehicleState
{
    /// <summary>Constructs a movement snapshot suitable for explicit restoration.</summary>
    /// <param name="tick">Last completed movement decision tick.</param>
    /// <param name="physics">Observed pose and commanded velocities.</param>
    /// <param name="grounded">Whether support was observed this tick.</param>
    /// <param name="drifting">Whether measurable lateral sliding is present.</param>
    /// <param name="steeringAngle">Current front-wheel angle in radians.</param>
    /// <param name="handbrake">Rear handbrake application, zero through one.</param>
    /// <param name="currentSurface">Current support surface, retained during flight.</param>
    /// <param name="frontSlip">Front traction saturation.</param>
    /// <param name="rearSlip">Rear traction saturation.</param>
    /// <param name="longitudinalAcceleration">Longitudinal tire acceleration.</param>
    /// <param name="lateralAcceleration">Lateral tire acceleration.</param>
    /// <param name="landingIntensity">Landing feedback.</param>
    /// <param name="wheels">Per-wheel compression for presentation and replay diagnostics.</param>
    /// <param name="nitro">Complete temporary boost continuation.</param>
    /// <param name="powerSlip">Progressive rear wheelspin grip loss.</param>
    /// <param name="air">Complete airborne control continuation.</param>
    /// <param name="oilTicks">Remaining temporary oil handling duration.</param>
    public VehicleState(ulong tick, VehiclePhysicsState physics, bool grounded, bool drifting, float steeringAngle, float handbrake, SurfaceType currentSurface = SurfaceType.Concrete, float frontSlip = 0, float rearSlip = 0, float longitudinalAcceleration = 0, float lateralAcceleration = 0, float landingIntensity = 0, WheelSupport wheels = default, int oilTicks = 0, NitroState nitro = default, float powerSlip = 0, AirControlState air = default)
    {
        _ = new VehiclePhysicsState(physics.Position, physics.Orientation, physics.LinearVelocity, physics.AngularVelocity);
        if (new[] { steeringAngle, handbrake, frontSlip, rearSlip, longitudinalAcceleration, lateralAcceleration, landingIntensity }.Any(value => !float.IsFinite(value)) || Math.Abs(steeringAngle) > 1 || handbrake is < 0 or > 1 || frontSlip is < 0 or > 1 || rearSlip is < 0 or > 1 || landingIntensity is < 0 or > 1 || Math.Abs(longitudinalAcceleration) > 1000 || Math.Abs(lateralAcceleration) > 1000)
        {
            throw new ArgumentException("Invalid handling state.");
        }

        if (oilTicks is < 0 or > 120) { throw new ArgumentException("Invalid oil duration."); }
        if (!float.IsFinite(powerSlip) || powerSlip is < 0 or > 1) { throw new ArgumentException("Invalid power slip."); }
        air.Validate();
        Air = air;
        PowerSlip = powerSlip;
        OilTicks = oilTicks;
        nitro.Validate();
        Nitro = nitro;
        Wheels = new WheelSupport(wheels.Compression);
        FrontSlip = frontSlip;
        RearSlip = rearSlip;
        LongitudinalAcceleration = longitudinalAcceleration;
        LateralAcceleration = lateralAcceleration;
        LandingIntensity = landingIntensity;
        if (!Enum.IsDefined(currentSurface))
        {
            throw new ArgumentOutOfRangeException(nameof(currentSurface));
        }

        CurrentSurface = currentSurface;
        Tick = tick;
        Physics = physics;
        Grounded = grounded;
        Drifting = drifting;
        SteeringAngle = steeringAngle;
        Handbrake = handbrake;
    }

    /// <summary>Remaining fixed steps of authoritative reduced tire grip.</summary>
    public int OilTicks { get; }
    /// <summary>Progressive rear wheelspin lateral grip loss retained through reconciliation.</summary>
    public float PowerSlip { get; }
    /// <summary>Complete airborne control continuation.</summary>
    public AirControlState Air { get; }
    /// <summary>Authoritative temporary boost; default means inactive.</summary>
    public NitroState Nitro { get; init; }
    /// <summary>Last supported surface; airborne movement applies no surface effects.</summary>
    public SurfaceType CurrentSurface { get; }
    /// <summary>Movement decision tick.</summary>
    public ulong Tick { get; }
    /// <summary>Body state for presentation and reconciliation.</summary>
    public VehiclePhysicsState Physics { get; }
    /// <summary>Ground contact supplied by the physics adapter.</summary>
    public bool Grounded { get; }
    /// <summary>Observed sliding feedback; never a mode that controls physics.</summary>
    public bool Drifting { get; }
    /// <summary>Current front-wheel angle in radians.</summary>
    public float SteeringAngle { get; }
    /// <summary>Rear handbrake application, zero through one.</summary>
    public float Handbrake { get; }
    /// <summary>Front axle traction saturation, zero through one.</summary>
    public float FrontSlip { get; }
    /// <summary>Rear axle traction saturation, zero through one.</summary>
    public float RearSlip { get; }
    /// <summary>Signed tire acceleration along the chassis, in m/s squared.</summary>
    public float LongitudinalAcceleration { get; }
    /// <summary>Signed tire acceleration across the chassis, in m/s squared.</summary>
    public float LateralAcceleration { get; }
    /// <summary>Landing compression impulse intensity; decays after contact.</summary>
    public float LandingIntensity { get; }
    /// <summary>Individual spring compression in metres.</summary>
    public WheelSupport Wheels { get; }
    /// <summary>Total commanded velocity magnitude for physics diagnostics, not player travel telemetry.</summary>
    public float CommandSpeed => Physics.LinearVelocity.Length();
    /// <summary>Revalidates all portable state fields at aggregate boundaries.</summary>
    public void Validate() => _ = new VehicleState(Tick, Physics, Grounded, Drifting, SteeringAngle, Handbrake, CurrentSurface, FrontSlip, RearSlip, LongitudinalAcceleration, LateralAcceleration, LandingIntensity, Wheels, OilTicks, Nitro, PowerSlip, Air);

}
