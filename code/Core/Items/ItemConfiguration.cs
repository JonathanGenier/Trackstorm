namespace Trackstorm.Core.Items;

/// <summary>Host-owned bounded tuning. Linear falloff reaches zero at the edge.</summary>
public sealed record ItemConfiguration
{
    /// <summary>Guided EMP projectile parameters.</summary>
    public EmpClawConfiguration EmpClaw { get; init; } = new();
    /// <summary>Complete EMP presentation deployment (seconds).</summary>
    public float EmpClawDeploySeconds { get; init; } = 1.2f;
    /// <summary>Complete EMP presentation return (seconds).</summary>
    public float EmpClawStowSeconds { get; init; } = .82f;
    /// <summary>Accepted presentation-shot spacing; default three shots within 1.67 seconds.</summary>
    public int EmpClawIntervalTicks { get; init; } = 50;
    /// <summary>Expanded wall width, captured at deployment (m).</summary>
    public float ShieldWidth { get; init; } = 6.6f;
    /// <summary>Expanded wall height (m).</summary>
    public float ShieldHeight { get; init; } = 2.5f;
    /// <summary>Expanded wall depth (m).</summary>
    public float ShieldDepth { get; init; } = 0.6f;
    /// <summary>Deployed rigid-body mass (kg).</summary>
    public float ShieldMass { get; init; } = 250;
    /// <summary>Clearance behind the chassis before ground queries (m).</summary>
    public float ShieldClearance { get; init; } = 1;
    /// <summary>Lifetime captured on successful deployment, in simulation seconds.</summary>
    public float ShieldLifetimeSeconds { get; init; } = 120;
    /// <summary>Impact impulse per wall mass that releases tipping, in metres per second.</summary>
    public float ShieldTipSpeed { get; init; } = 20;
    /// <summary>Shared direct-fire aiming rules.</summary>
    public WeaponAimConfiguration Aim { get; init; } = new();
    /// <summary>Fixed physical five-round magazine.</summary>
    public int SalvoCount { get; init; } = 5;
    /// <summary>Automatic launch interval at 60 Hz; default 1.25 seconds.</summary>
    public int SalvoIntervalTicks { get; init; } = 75;
    /// <summary>Maximum camera aim range (m).</summary>
    public float SalvoRange { get; init; } = 65;
    /// <summary>Height above vehicle (m).</summary>
    public float SalvoLaunchHeight { get; init; } = 3;
    /// <summary>Parabola height above chord (m).</summary>
    public float SalvoArcHeight { get; init; } = 12;
    /// <summary>Mean flight speed (m/s).</summary>
    public float SalvoSpeed { get; init; } = 150;
    /// <summary>Blast radius (m).</summary>
    public float SalvoBlastRadius { get; init; } = .9144f;
    /// <summary>Maximum damage per round.</summary>
    public float SalvoDamage { get; init; } = 250;
    /// <summary>Damage and impulse falloff exponent.</summary>
    public float SalvoFalloff { get; init; } = 1;
    /// <summary>Maximum impulse (N s).</summary>
    public float SalvoImpulse { get; init; } = 3500;
    /// <summary>Marker radius relative to blast.</summary>
    public float SalvoMarkerScale { get; init; } = 0.5f;
    /// <summary>Marker ring width (m).</summary>
    public float SalvoMarkerWidth { get; init; } = 0.5f;
    /// <summary>Marker surface offset (m).</summary>
    public float SalvoMarkerLift { get; init; } = 0.05f;
    /// <summary>Acquired rounds; existing magazines keep their capacity.</summary>
    public int MachineGunCapacity { get; init; } = 800;
    /// <summary>Rounds per second, bounded to two rounds per fixed step.</summary>
    public double MachineGunFireRate { get; init; } = 80;
    /// <summary>Resource units consumed per actual round; final partial cost exhausts the item.</summary>
    public int MachineGunAmmoPerRound { get; init; } = 1;
    /// <summary>Maximum damaging ray length in metres.</summary>
    public float MachineGunRange { get; init; } = 225;
    /// <summary>Near-range HP per round.</summary>
    public float MachineGunDamage { get; init; } = 2.25f;
    /// <summary>Distance where damage begins fading.</summary>
    public float MachineGunFalloffStart { get; init; } = 12;
    /// <summary>Power exponent of the fade to zero at maximum range.</summary>
    public float MachineGunFalloff { get; init; } = 1.5f;
    /// <summary>Half-angle of the uniform spread cone in degrees.</summary>
    public float MachineGunSpread { get; init; } = .42f;
    /// <summary>Near-range central impulse per round in Newton seconds.</summary>
    public float MachineGunKnockback { get; init; } = 8;
    /// <summary>Render one tracer per this many rounds, including the first.</summary>
    public int MachineGunTracerEvery { get; init; } = 2;
    /// <summary>Contact damage, applied once through vehicle health authority.</summary>
    public float MineDamage { get; init; } = 250;
    /// <summary>Invisible magnetic field extent in metres.</summary>
    public float MineAttractionRadius { get; init; } = 30;
    /// <summary>Weak field baseline in Newtons, ramped continuously from zero at the boundary.</summary>
    public float MineMinimumForce { get; init; } = 1000;
    /// <summary>Close-range magnetic force in Newtons.</summary>
    public float MineMaximumForce { get; init; } = 2000;
    /// <summary>Power exponent controlling the distance-to-force curve.</summary>
    public float MineFalloff { get; init; } = 2.5f;
    /// <summary>Contact impulse in Newton seconds.</summary>
    public float MineKnockback { get; init; } = 18000;
    /// <summary>Percentage points consumed per second of held use.</summary>
    public double NitroConsumptionPerSecond { get; init; } = 20;
    /// <summary>Independent forward thrust in newtons, unaffected by throttle or tire traction.</summary>
    public float NitroForwardThrust { get; init; } = 18000;
    /// <summary>Forward drive cap multiplier, still bounded by vehicle physics safety limits.</summary>
    public float NitroSpeedMultiplier { get; init; } = 1.4f;

    /// <summary>Fraction of rocket thrust available without driveable wheel support; zero disables airborne thrust.</summary>
    public float NitroAirborneThrustScale { get; init; } = 1;

    /// <summary>Maximum lifetime of new patches in seconds at the authoritative 60 Hz clock.</summary>
    public float OilLifetimeSeconds { get; init; } = 30;

    /// <summary>Oil FireSeconds tuning, captured by an activation where applicable.</summary>
    public float OilFireSeconds { get; init; } = 1f;
    /// <summary>Oil Speed tuning, captured by an activation where applicable.</summary>
    public float OilSpeed { get; init; } = 18f;
    /// <summary>Oil Gravity tuning, captured by an activation where applicable.</summary>
    public float OilGravity { get; init; } = 9.81f;
    /// <summary>Oil Radius tuning, captured by an activation where applicable.</summary>
    public float OilRadius { get; init; } = 1.3f;
    /// <summary>Oil SpringStiffness tuning, captured by an activation where applicable.</summary>
    public float OilSpringStiffness { get; init; } = 12f;
    /// <summary>Oil SpringDamping tuning, captured by an activation where applicable.</summary>
    public float OilSpringDamping { get; init; } = 1.8f;
    /// <summary>Oil SpringReaction tuning, captured by an activation where applicable.</summary>
    public float OilSpringReaction { get; init; } = 0.44f;
    /// <summary>Captured head recoil velocity at the start of Oil expulsion.</summary>
    public float OilKickback { get; init; } = 1.6f;
    /// <summary>Oil Roughness tuning, captured by an activation where applicable.</summary>
    public float OilRoughness { get; init; } = 0.16f;
    /// <summary>Cosmetic liquid diameter at the mouth in metres; does not add projectiles.</summary>
    public float OilStreamWidth { get; init; } = .24f;
    /// <summary>Bounded fixed-step parcel emission cadence.</summary>
    public float OilIntervalTicks { get; init; } = 1.5f;

    /// <summary>HP restored to a living vehicle.</summary>
    public float WrenchHeal { get; init; } = 500;
    /// <summary>Metres per second along the launch forward direction.</summary>
    public float MissileSpeed { get; init; } = 120;
    /// <summary>Fixed 60 Hz steps before removal without an explosion.</summary>
    public int MissileLifetimeTicks { get; init; } = 300;
    /// <summary>Independent maximum lifetime in seconds; the smaller of this and the tick budget wins at launch.</summary>
    public float MissileLifetimeSeconds { get; init; } = 60;
    /// <summary>Absolute world-coordinate safety bound; leaving it retires a missile without an impact.</summary>
    public float MissileWorldLimit { get; init; } = 4096;
    /// <summary>Seconds for trunk, rack, mount, extension and fins; captured when selected.</summary>
    public float MissileDeploySeconds { get; init; } = 1.6f;
    /// <summary>Seconds for empty mount, rack and trunk return; captured when fired or deselected.</summary>
    public float MissileStowSeconds { get; init; } = .9f;
    /// <summary>Terrain-only guidance; authoritative gameplay tuning.</summary>
    public MissileTerrainConfiguration MissileTerrain { get; init; } = new();
    /// <summary>Damaging radial reach in metres; one production-car collision length by default.</summary>
    public float ExplosionRadius { get; init; } = Vehicles.VehicleDimensions.Length;
    /// <summary>Damage at the exact center.</summary>
    public float MaximumDamage { get; init; } = 55;
    /// <summary>Outward impulse in Newton seconds at the center.</summary>
    public float MaximumImpulse { get; init; } = 15000;

    /// <summary>Rejects nonfinite or excessive host configuration before simulation.</summary>
    public void Validate()
    {
        if (!float.IsFinite(EmpClawDeploySeconds) || EmpClawDeploySeconds is < .8f or > 4 ||
            !float.IsFinite(EmpClawStowSeconds) || EmpClawStowSeconds is < .5f or > 3 || EmpClawIntervalTicks is < 15 or > 60)
        { throw new ArgumentException("Invalid EMP Claw presentation timing."); }
        if (!float.IsFinite(ShieldWidth) || ShieldWidth is < 3 or > 12 ||
            !float.IsFinite(ShieldHeight) || ShieldHeight is < 2 or > 6 ||
            !float.IsFinite(ShieldDepth) || ShieldDepth is < 0.3f or > 2 ||
            !float.IsFinite(ShieldMass) || ShieldMass is < 50 or > 2000 ||
            !float.IsFinite(ShieldClearance) || ShieldClearance is < 0.5f or > 5 ||
            !float.IsFinite(ShieldLifetimeSeconds) || ShieldLifetimeSeconds is < 1 or > 600 ||
            !float.IsFinite(ShieldTipSpeed) || ShieldTipSpeed is < 1 or > 100)
        { throw new ArgumentException("Invalid Shield wall tuning."); }
        ArgumentNullException.ThrowIfNull(Aim);
        Aim.Validate();
        if (!float.IsFinite(MissileDeploySeconds) || MissileDeploySeconds is < 1 or > 4 ||
            !float.IsFinite(MissileStowSeconds) || MissileStowSeconds is < .5f or > 3)
        { throw new ArgumentException("Invalid Missile deployment timing."); }
        ArgumentNullException.ThrowIfNull(MissileTerrain);
        ArgumentNullException.ThrowIfNull(EmpClaw);
        EmpClaw.Validate();
        MissileTerrain.Validate();
        if (!float.IsFinite(MissileLifetimeSeconds) || MissileLifetimeSeconds is < 0.05f or > 60 ||
            !float.IsFinite(MissileWorldLimit) || MissileWorldLimit is < 256 or > 16384)
        { throw new ArgumentException("Invalid Missile cleanup bounds."); }
        if (SalvoCount != ItemSlot.SalvoMagazineSize) { throw new ArgumentException("Salvo magazines contain exactly five rounds."); }
        if (SalvoIntervalTicks < 30 || SalvoIntervalTicks > 180) { throw new ArgumentException("Salvo interval must allow a complete reload (30–180 ticks)."); }
        if (!double.IsFinite(SalvoRange) || SalvoRange < 25 || SalvoRange > 250) { throw new ArgumentException("Invalid salvo range."); }
        if (!double.IsFinite(SalvoLaunchHeight) || SalvoLaunchHeight < 1 || SalvoLaunchHeight > 10) { throw new ArgumentException("Invalid salvo launch_height."); }
        if (!double.IsFinite(SalvoArcHeight) || SalvoArcHeight < 1 || SalvoArcHeight > 60) { throw new ArgumentException("Invalid salvo arc_height."); }
        if (!double.IsFinite(SalvoSpeed) || SalvoSpeed < 20 || SalvoSpeed > 200) { throw new ArgumentException("Invalid salvo speed."); }
        if (!double.IsFinite(SalvoBlastRadius) || SalvoBlastRadius < .1f || SalvoBlastRadius > 30) { throw new ArgumentException("Invalid salvo blast_radius."); }
        if (!double.IsFinite(SalvoDamage) || SalvoDamage < 0 || SalvoDamage > 1000) { throw new ArgumentException("Invalid salvo damage."); }
        if (!double.IsFinite(SalvoFalloff) || SalvoFalloff < 0.25 || SalvoFalloff > 4) { throw new ArgumentException("Invalid salvo falloff."); }
        if (!double.IsFinite(SalvoImpulse) || SalvoImpulse < 0 || SalvoImpulse > 50000) { throw new ArgumentException("Invalid salvo impulse."); }
        if (!double.IsFinite(SalvoMarkerScale) || SalvoMarkerScale < 0.5 || SalvoMarkerScale > 2) { throw new ArgumentException("Invalid salvo marker_scale."); }
        if (!double.IsFinite(SalvoMarkerWidth) || SalvoMarkerWidth < 0.1 || SalvoMarkerWidth > 2) { throw new ArgumentException("Invalid salvo marker_width."); }
        if (!double.IsFinite(SalvoMarkerLift) || SalvoMarkerLift < 0.02 || SalvoMarkerLift > 0.5) { throw new ArgumentException("Invalid salvo marker_lift."); }
        if (MachineGunCapacity is < 1 or > 10000 || MachineGunAmmoPerRound is < 1 or > 10 || !double.IsFinite(MachineGunFireRate) || MachineGunFireRate is < 1 or > 120 ||
            !float.IsFinite(MachineGunRange) || MachineGunRange is < 1 or > 300 ||
            !float.IsFinite(MachineGunDamage) || MachineGunDamage is < 0 or > 1000 ||
            !float.IsFinite(MachineGunFalloffStart) || MachineGunFalloffStart < 0 || MachineGunFalloffStart >= MachineGunRange ||
            !float.IsFinite(MachineGunFalloff) || MachineGunFalloff is < 0.1f or > 8 ||
            !float.IsFinite(MachineGunSpread) || MachineGunSpread is < 0 or > 30 ||
            !float.IsFinite(MachineGunKnockback) || MachineGunKnockback is < 0 or > 1000 || MachineGunTracerEvery is < 1 or > 25)
        { throw new ArgumentException("Invalid machine gun tuning."); }
        if (!float.IsFinite(MineDamage) || MineDamage is < 0 or > 10000 ||
            !float.IsFinite(MineAttractionRadius) || MineAttractionRadius is < 1 or > 100 ||
            !float.IsFinite(MineMinimumForce) || MineMinimumForce is < 0 or > 10000 ||
            !float.IsFinite(MineMaximumForce) || MineMaximumForce < MineMinimumForce || MineMaximumForce > 10000 ||
            !float.IsFinite(MineFalloff) || MineFalloff is < 1 or > 8 ||
            !float.IsFinite(MineKnockback) || MineKnockback is < 0 or > 1000000)
        { throw new ArgumentException("Invalid Proxy Mine tuning."); }
        new Vehicles.NitroState(1, NitroForwardThrust, NitroSpeedMultiplier, NitroAirborneThrustScale).Validate();
        if (!double.IsFinite(NitroConsumptionPerSecond) || NitroConsumptionPerSecond is < 2 or > 6000) { throw new ArgumentException("Nitro consumption must be 2–6000 percentage points per second."); }
        if (!float.IsFinite(OilFireSeconds) || OilFireSeconds is < .5f or > 4 || !float.IsFinite(OilIntervalTicks) || OilIntervalTicks is < 1 or > 30 ||
            MathF.Ceiling(MathF.Ceiling(OilFireSeconds * 60) / OilIntervalTicks) > 48 ||
            !float.IsFinite(OilSpeed) || OilSpeed is < 1 or > 30 || !float.IsFinite(OilGravity) || OilGravity is < 1 or > 40 ||
            !float.IsFinite(OilRadius) || OilRadius is < .1f or > 1.5f ||
            !float.IsFinite(OilSpringStiffness) || OilSpringStiffness is < 5 or > 100 ||
            !float.IsFinite(OilSpringDamping) || OilSpringDamping is < 1 or > 20 ||
            !float.IsFinite(OilSpringReaction) || OilSpringReaction is < 0 or > .5f ||
            !float.IsFinite(OilKickback) || OilKickback is < 0 or > 3 ||
            !float.IsFinite(OilStreamWidth) || OilStreamWidth is < .08f or > .8f ||
            !float.IsFinite(OilRoughness) || OilRoughness is < .05f or > .6f)
        { throw new ArgumentException("Invalid Oil physics/presentation tuning."); }
        if (!float.IsFinite(OilLifetimeSeconds) || OilLifetimeSeconds is < 1 or > 600 ||
            !float.IsFinite(WrenchHeal) || WrenchHeal < 0 || WrenchHeal > 10000 ||
            !float.IsFinite(MissileSpeed) || MissileSpeed <= 0 || MissileSpeed > 300 ||
            MissileLifetimeTicks is < 1 or > 3600 || !float.IsFinite(ExplosionRadius) || ExplosionRadius <= 0 || ExplosionRadius > 100 ||
            !float.IsFinite(MaximumDamage) || MaximumDamage < 0 || MaximumDamage > 10000 ||
            !float.IsFinite(MaximumImpulse) || MaximumImpulse < 0 || MaximumImpulse > 1000000)
        {
            throw new ArgumentException("Invalid item tuning.");
        }
    }
}
