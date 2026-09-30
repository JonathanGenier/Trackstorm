namespace Trackstorm.Core.Vehicles;

/// <summary>Validated arcade tuning shared by local, replay, and future authority drivers.</summary>
public sealed record VehicleConfiguration
{
    /// <summary>Trunk mechanism speed relative to its original 0.72-second travel; independently tunable.</summary>
    public float TrunkDeploymentSpeed { get; init; } = 3;
    /// <summary>Rack mechanism speed relative to its original 0.88-second travel; independently tunable.</summary>
    public float RackDeploymentSpeed { get; init; } = 3;
    /// <summary>Fraction of lateral tire grip lost on Oil; propulsion and steering remain available.</summary>
    public float OilGripReduction { get; init; } = 0.1f;
    /// <summary>Seconds of progressive lateral-grip recovery after the last supported Oil contact.</summary>
    public float OilRecoverySeconds { get; init; } = 0.5f;
    /// <summary>Asphalt tire capacity; neutral drag/drive retain approved power and coasting.</summary>
    public float AsphaltGrip { get; init; } = 3;
    /// <summary>Cast surface handling profile.</summary>
    public SurfaceModifiers Concrete { get; init; } = new(1.5f, 1, 0.98f);
    /// <summary>Compacted soil retains controllable drive with modest rolling resistance.</summary>
    public SurfaceModifiers Dirt { get; init; } = new(2, 1.15f, 0.95f);
    /// <summary>Vegetation handling profile with added rolling resistance.</summary>
    public SurfaceModifiers Grass { get; init; } = new(1.9f, 1.4f, 0.9f);
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
    /// <summary>Environmental HP/s after leaving the authored arena perimeter.</summary>
    public float OutOfBoundsDamagePerSecond { get; init; } = 100;

    /// <summary>Fixed frequency; independent of rendering.</summary>
    public int TicksPerSecond { get; init; } = 60;
    /// <summary>Body mass in kilograms.</summary>
    public float Mass { get; init; } = 3000;
    /// <summary>Engine force expressed as acceleration at reference mass; calibrated with body mass to retain approved pacing.</summary>
    public float Acceleration { get; init; } = 24 * (3000f / 1400);
    /// <summary>Seconds for engine demand to close 63 percent of an increasing pedal change.</summary>
    public float ThrottleRiseTime { get; init; } = 0.65f;
    /// <summary>Seconds for engine demand to close 63 percent of a decreasing pedal change.</summary>
    public float ThrottleFallTime { get; init; } = 0.12f;
    /// <summary>Braking deceleration.</summary>
    public float Braking { get; init; } = 45 * (3000f / 1400);
    /// <summary>Engine demand sent to the front axle; the rear handbrake interrupts all engine drive.</summary>
    public float FrontDriveShare { get; init; } = 0;
    /// <summary>Driven rear tire longitudinal capacity multiplier; leaves lateral grip independent.</summary>
    public float RearDriveGrip { get; init; } = 1.6f;
    /// <summary>Service brake tire capacity multiplier at full pedal.</summary>
    public float BrakeGrip { get; init; } = 2;
    /// <summary>Rear lateral grip loss from excess torque and steering commitment.</summary>
    public float PowerOversteer { get; init; } = 0.8f;
    /// <summary>Speed through which deliberate power breakaway retains full demand, in m/s.</summary>
    public float PowerSlipFullSpeed { get; init; } = 8;
    /// <summary>Speed where torque-induced rear grip loss fades to zero, in m/s; handbrake is independent.</summary>
    public float PowerSlipFadeSpeed { get; init; } = 16.667f;
    /// <summary>Forward creep accepted as effectively stopped on a new reverse press, in m/s.</summary>
    public float ReverseEngagementSpeed { get; init; } = 0.35f;
    /// <summary>Longitudinal traction loss at full wheelspin.</summary>
    public float SpinDriveLoss { get; init; } = 0.7f;
    /// <summary>Residual opposing speed snapped to rest before reversing, in m/s.</summary>
    public float StopSpeed { get; init; } = 0.05f;
    /// <summary>Reverse acceleration.</summary>
    public float ReverseAcceleration { get; init; } = 8 * (3000f / 1400);
    /// <summary>Normal forward drive limit in metres per second.</summary>
    public float ForwardSpeed { get; init; } = 44.44f;
    /// <summary>Reverse drive limit.</summary>
    public float ReverseSpeed { get; init; } = 11;
    /// <summary>Lateral grip response per second.</summary>
    public float Grip { get; init; } = 26 * (3000f / 1400);
    /// <summary>Maximum wheel angle in radians at every speed.</summary>
    public float SteeringAngle { get; init; } = 0.9f;
    /// <summary>Wheel angle transition rate in radians per second.</summary>
    public float SteeringResponse { get; init; } = 0.95f;
    /// <summary>Time constant for progressive wheel corrections, independent of speed.</summary>
    public float SteeringSmoothing { get; init; } = 0.3f;
    /// <summary>Low/medium dirt corner authority multiplier; fades out by 28 m/s.</summary>
    public float DirtCornering { get; init; } = 1;
    /// <summary>Speed through which the extra dirt tire budget is fully available, m/s.</summary>
    public float DirtCornerFullSpeed { get; init; } = 8;
    /// <summary>Speed where extra dirt tire budget finishes fading; never limits wheel angle, m/s.</summary>
    public float DirtCornerFadeSpeed { get; init; } = 28;
    /// <summary>Extra tire capacity at full dirt corner commitment.</summary>
    public float DirtCornerGrip { get; init; } = 1.2f;
    /// <summary>Extra power-slip demand at full dirt corner commitment.</summary>
    public float DirtCornerPowerSlip { get; init; } = 1.5f;
    /// <summary>Fraction of ordinary service braking assigned to the front axle.</summary>
    public float FrontBrakeShare { get; init; } = 0.65f;
    /// <summary>Maximum dirt rear lateral grip loss under sustained power.</summary>
    public float DirtPowerSlip { get; init; } = 0.22f;
    /// <summary>Front dirt tire budget reserved for wheel direction during saturated slides.</summary>
    public float DirtSteeringReserve { get; init; } = 0.95f;
    /// <summary>Dirt slide yaw recovery response per second; zero disables the arcade assist.</summary>
    public float DirtRecovery { get; init; } = 4;
    /// <summary>Wheelspin buildup rate per second.</summary>
    public float PowerSlipResponse { get; init; } = 2;
    /// <summary>Wheelspin recovery rate per second on throttle reduction.</summary>
    public float PowerSlipRecovery { get; init; } = 2.5f;
    /// <summary>Distance between axle centers in metres.</summary>
    public float Wheelbase { get; init; } = VehicleDimensions.Wheelbase;
    /// <summary>Tire friction coefficient; combined demands share this budget.</summary>
    public float TireFriction { get; init; } = 1.9f;
    /// <summary>Rear traction share available to propulsion despite lateral saturation; zero disables allocation.</summary>
    public float DriveTractionReserve { get; init; } = 0.55f;
    /// <summary>Effective center-of-mass height for longitudinal/lateral load transfer.</summary>
    public float LoadHeight { get; init; } = 0.45f * VehicleDimensions.Scale;
    /// <summary>Rear braking deceleration at reference mass.</summary>
    public float HandbrakeBraking { get; init; } = 30 * (3000f / 1400);
    /// <summary>Rear lateral grip fraction with the handbrake fully engaged.</summary>
    public float HandbrakeGrip { get; init; } = 0.25f;
    /// <summary>Handbrake application response per second.</summary>
    public float HandbrakeResponse { get; init; } = 2;
    /// <summary>Handbrake release response per second, permitting gradual traction recovery.</summary>
    public float TractionRecovery { get; init; } = 3;
    /// <summary>Horizontal overspeed recovery in m/s² above the effective drive limit.</summary>
    public float OverspeedDeceleration { get; init; } = 3;
    /// <summary>Rolling resistance per second.</summary>
    public float CoastDrag { get; init; } = 0.28f;
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
    public float SuspensionLength { get; init; } = VehicleDimensions.RideHeight + (11f / 22);
    /// <summary>Vertical spring stiffness per unit sprung mass.</summary>
    public float WheelSpring { get; init; } = 22;
    /// <summary>Compression damping per unit sprung mass; acts on chassis point velocity.</summary>
    public float WheelDamping { get; init; } = 12;
    /// <summary>Extension damping per unit sprung mass, controlling recovery without pulling tires down.</summary>
    public float WheelReboundDamping { get; init; } = 16;
    /// <summary>Compression where progressive bump resistance begins, in metres.</summary>
    public float WheelBumpStart { get; init; } = 0.55f;
    /// <summary>Additional acceleration per squared metre beyond bump engagement.</summary>
    public float WheelBumpSpring { get; init; } = 3500;
    /// <summary>Additional squared deep-travel damping multiplier.</summary>
    public float WheelDeepDamping { get; init; } = 8;
    /// <summary>Clean-landing rebound envelope decay per second.</summary>
    public float LandingReboundDecay { get; init; } = 3;
    /// <summary>Gravity acceleration.</summary>
    public float Gravity { get; init; } = 11;
    /// <summary>Safety bound on total velocity, including external impulses.</summary>
    public float MaximumPhysicsSpeed { get; init; } = 65;
    /// <summary>Safety bound on angular velocity.</summary>
    public float MaximumAngularSpeed { get; init; } = 8;

    /// <summary>Activation delay (s).</summary>
    public float AirDelay { get; init; } = 0.5f;
    /// <summary>Pitch rate (rad/s).</summary>
    public float AirPitchRate { get; init; } = 2.52f;
    /// <summary>Yaw rate (rad/s).</summary>
    public float AirYawRate { get; init; } = 2.16f;
    /// <summary>Roll rate (rad/s).</summary>
    public float AirRollRate { get; init; } = 3.24f;
    /// <summary>Pitch acceleration (rad/s2).</summary>
    public float AirPitchAcceleration { get; init; } = 16;
    /// <summary>Yaw acceleration (rad/s2).</summary>
    public float AirYawAcceleration { get; init; } = 14;
    /// <summary>Roll acceleration (rad/s2).</summary>
    public float AirRollAcceleration { get; init; } = 20;
    /// <summary>Residual rotation damping (1/s).</summary>
    public float AirStabilization { get; init; } = 8;
    /// <summary>Stabilization ramp (s).</summary>
    public float AirStabilizationResponse { get; init; } = 0.08f;
    /// <summary>Input smoothing (s).</summary>
    public float AirInputResponse { get; init; } = 0.06f;
    /// <summary>Air input dead zone.</summary>
    public float AirDeadZone { get; init; } = 0.08f;
    /// <summary>Minimum ground normal Y.</summary>
    public float SupportNormalMinimum { get; init; } = 0.55f;

    /// <summary>Body-supported crash seconds before bounded rolling assistance starts.</summary>
    public float CrashRecoveryDelay { get; init; } = 1.25f;
    /// <summary>Minimum assisted rolling rate in rad/s; zero disables assistance.</summary>
    public float CrashRecoveryRate { get; init; } = 1.2f;
    /// <summary>Seconds for the delayed rolling-rate floor to reach full strength.</summary>
    public float CrashRecoveryRamp { get; init; } = 0.75f;
    /// <summary>Body-supported crash tangential damping per second.</summary>
    public float CrashSlideDamping { get; init; } = 2;
    /// <summary>Body-supported crash angular damping per second.</summary>
    public float CrashRollDamping { get; init; } = 0.65f;

    /// <summary>Static-obstacle tangential resistance per second.</summary>
    public float WallDrag { get; init; } = 0.18f;
    /// <summary>Fraction of residual lateral motion removed by a direct crash.</summary>
    public float CrashDissipation { get; init; } = 0.95f;
    /// <summary>Eccentric obstacle/vehicle impact angular response multiplier.</summary>
    public float CrashRotation { get; init; } = 0.08f;
    /// <summary>Maximum angular velocity change from one obstacle/vehicle manifold, rad/s.</summary>
    public float CrashAngularLimit { get; init; } = 1.2f;

    /// <summary>Resolves explicit surface tuning without engine or mutable state.</summary>
    /// <param name="surface">Supported surface identifier.</param>
    /// <returns>Configured handling multipliers.</returns>
    public SurfaceModifiers ResolveSurface(SurfaceType surface) => surface switch
    {
        SurfaceType.Concrete => Concrete,
        SurfaceType.Mud => Mud,
        SurfaceType.Asphalt => new(AsphaltGrip, 1, 1),
        SurfaceType.Dirt => Dirt,
        SurfaceType.Grass => Grass,
        SurfaceType.DeepMud => DeepMud,
        SurfaceType.Water => Water,
        _ => throw new ArgumentOutOfRangeException(nameof(surface)),
    };

    /// <summary>Rejects unsafe tuning before any state or native body is created.</summary>
    public void Validate()
    {
        if (!float.IsFinite(AsphaltGrip) || AsphaltGrip is < 0 or > 100) { throw new ArgumentException("Invalid asphalt grip."); }
        if (!float.IsFinite(PowerSlipFullSpeed) || !float.IsFinite(PowerSlipFadeSpeed) || PowerSlipFullSpeed < 0 || PowerSlipFadeSpeed <= PowerSlipFullSpeed || PowerSlipFadeSpeed > MaximumPhysicsSpeed || !float.IsFinite(ReverseEngagementSpeed) || ReverseEngagementSpeed is < 0 or > 1) { throw new ArgumentException("Invalid power-slip speed or reverse engagement tuning."); }
        if (!float.IsFinite(PowerOversteer) || PowerOversteer is < 0 or > .9f || !float.IsFinite(SpinDriveLoss) || SpinDriveLoss is < 0 or > .9f) { throw new ArgumentException("Invalid wheelspin tuning."); }
        if (!float.IsFinite(RearDriveGrip) || RearDriveGrip is < 0.1f or > 4 || !float.IsFinite(BrakeGrip) || BrakeGrip is < 0.1f or > 4) { throw new ArgumentException("Invalid longitudinal tire grip."); }
        if (!float.IsFinite(FrontDriveShare) || FrontDriveShare is < 0 or > 1)
        {
            throw new ArgumentException("Front drive share must be a finite fraction.");
        }
        if (!float.IsFinite(TrunkDeploymentSpeed) || TrunkDeploymentSpeed is < 0.1f or > 10 ||
            !float.IsFinite(RackDeploymentSpeed) || RackDeploymentSpeed is < 0.1f or > 10)
        {
            throw new ArgumentException("Trunk and rack deployment speeds must each be between 0.1 and 10.");
        }
        if (!float.IsFinite(OilGripReduction) || OilGripReduction is < 0 or > 0.8f ||
            !float.IsFinite(OilRecoverySeconds) || OilRecoverySeconds is < 0.1f or > 10)
        {
            throw new ArgumentException("Oil requires grip reduction 0–0.8 and recovery 0.1–10 seconds.");
        }
        if (!float.IsFinite(WallDrag) || WallDrag is < 0 or > 5 ||
            !float.IsFinite(CrashDissipation) || CrashDissipation is < 0 or > 1 ||
            !float.IsFinite(CrashRotation) || CrashRotation is < 0 or > 1 ||
            !float.IsFinite(CrashAngularLimit) || CrashAngularLimit is < 0 or > 3)
        {
            throw new ArgumentException("Collision tuning requires wall drag 0–5/s, dissipation/rotation 0–1 and angular change 0–3 rad/s.");
        }
        if (!float.IsFinite(AirDelay) || AirDelay is < 0 or > 2 ||
            new[] { AirPitchRate, AirYawRate, AirRollRate }.Any(v => !float.IsFinite(v) || v is < 0 or > 8) ||
            new[] { AirPitchAcceleration, AirYawAcceleration, AirRollAcceleration }.Any(v => !float.IsFinite(v) || v is < 0.1f or > 60) ||
            new[] { AirInputResponse, AirStabilizationResponse }.Any(v => !float.IsFinite(v) || v is < 0.01f or > 1) ||
            !float.IsFinite(AirStabilization) || AirStabilization is < 0 or > 30 ||
            !float.IsFinite(AirDeadZone) || AirDeadZone is < 0 or > 0.5f ||
            !float.IsFinite(SupportNormalMinimum) || SupportNormalMinimum is < 0.55f or > 1)
        {
            throw new ArgumentException("Invalid air-control tuning.");
        }
        if (new[] { ThrottleRiseTime, ThrottleFallTime }.Any(v => !float.IsFinite(v) || v is < 0.01f or > 3) ||
            !float.IsFinite(SteeringSmoothing) || SteeringSmoothing is < 0.01f or > 1 ||
            !float.IsFinite(DirtSteeringReserve) || DirtSteeringReserve is < 0 or > 1 ||
            !float.IsFinite(DirtRecovery) || DirtRecovery is < 0 or > 10 ||
            !float.IsFinite(DirtPowerSlip) || DirtPowerSlip is < 0 or > 0.8f ||
            !float.IsFinite(PowerSlipResponse) || PowerSlipResponse is < 0.1f or > 20 ||
            !float.IsFinite(PowerSlipRecovery) || PowerSlipRecovery is < 0.1f or > 20)
        {
            throw new ArgumentException("Invalid progressive handling tuning.");
        }
        if (!float.IsFinite(OutOfBoundsDamagePerSecond) || OutOfBoundsDamagePerSecond is < 0 or > 10000)
        { throw new ArgumentException("Out-of-bounds damage must be 0–10000 HP/s."); }
        if (!float.IsFinite(DeepWaterDepth) || DeepWaterDepth is < 0.01f or > 100 ||
            !float.IsFinite(WaterDamagePerSecond) || WaterDamagePerSecond is < 0 or > 10000)
        {
            throw new ArgumentException("Water depth must be 0.01–100 metres and damage 0–10000 HP/s.");
        }
        if (!float.IsFinite(DriveTractionReserve) || DriveTractionReserve < 0 || DriveTractionReserve > 1)
        {
            throw new ArgumentException("Drive traction reserve must be a finite fraction.");
        }

        static bool Within(float value, float minimum, float maximum) => float.IsFinite(value) && value >= minimum && value <= maximum;
        if (!Within(DirtCornerFullSpeed, 0, 99) || !Within(DirtCornerFadeSpeed, DirtCornerFullSpeed + 0.1f, 100) ||
            !Within(DirtCornerGrip, 0, 4) || !Within(DirtCornerPowerSlip, 0, 4) || !Within(FrontBrakeShare, 0, 1) ||
            !Within(WheelDeepDamping, 0, 30) || !Within(LandingReboundDecay, 0.1f, 20) ||
            !Within(CrashRecoveryRamp, 0.05f, 5) || !Within(CrashSlideDamping, 0, 10) || !Within(CrashRollDamping, 0, 10))
        { throw new ArgumentException("Invalid trophy-truck force/recovery tuning."); }
        if (!float.IsFinite(DirtCornering) || DirtCornering is < 0 or > 2) { throw new ArgumentException("Invalid dirt corner tuning."); }
        if (!float.IsFinite(CrashRecoveryDelay) || CrashRecoveryDelay is < 0.5f or > 10 || !float.IsFinite(CrashRecoveryRate) || CrashRecoveryRate is < 0 or > 2) { throw new ArgumentException("Invalid crash recovery tuning."); }
        if (TicksPerSecond is < 30 or > 240 || new[] { StopSpeed, SuspensionLength, WheelSpring, WheelDamping, WheelReboundDamping, WheelBumpStart, WheelBumpSpring, Mass, Acceleration, Braking, ReverseAcceleration, ForwardSpeed, ReverseSpeed, OverspeedDeceleration, Grip, SteeringAngle, SteeringResponse, Wheelbase, TireFriction, LoadHeight, HandbrakeBraking, HandbrakeGrip, HandbrakeResponse, TractionRecovery, CoastDrag, ReferenceMass, SuspensionSpring, SuspensionDamping, ChassisCompliance, MaximumChassisTilt, Gravity, MaximumPhysicsSpeed, MaximumAngularSpeed }.Any(value => !float.IsFinite(value) || value <= 0 || value > 10000) || SuspensionLength > 2 || WheelBumpStart >= 1 || HandbrakeGrip > 1 || SteeringAngle > 1 || MaximumChassisTilt > 0.5f || !float.IsFinite(StabilityDamping) || StabilityDamping < 0 || StabilityDamping > 1 || ForwardSpeed > MaximumPhysicsSpeed || ReverseSpeed > ForwardSpeed)
        {
            throw new ArgumentException("Vehicle tuning requires finite positive values and consistent speed/grip limits.");
        }
    }
}
