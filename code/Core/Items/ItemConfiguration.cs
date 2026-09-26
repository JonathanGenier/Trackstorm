namespace Trackstorm.Core.Items;

/// <summary>Host-owned bounded tuning. Linear falloff reaches zero at the edge.</summary>
public sealed record ItemConfiguration
{
    /// <summary>Individually fired shots per pickup.</summary>
    public int SalvoCount { get; init; } = 5;
    /// <summary>Minimum interval between presses accepted as shots, at 60 Hz.</summary>
    public int SalvoIntervalTicks { get; init; } = 30;
    /// <summary>Fixed forward range (m).</summary>
    public float SalvoRange { get; init; } = 65;
    /// <summary>Height above vehicle (m).</summary>
    public float SalvoLaunchHeight { get; init; } = 3;
    /// <summary>Parabola height above chord (m).</summary>
    public float SalvoArcHeight { get; init; } = 12;
    /// <summary>Mean flight speed (m/s).</summary>
    public float SalvoSpeed { get; init; } = 85;
    /// <summary>Blast radius (m).</summary>
    public float SalvoBlastRadius { get; init; } = 7;
    /// <summary>Maximum damage per round.</summary>
    public float SalvoDamage { get; init; } = 65;
    /// <summary>Damage and impulse falloff exponent.</summary>
    public float SalvoFalloff { get; init; } = 1;
    /// <summary>Maximum impulse (N s).</summary>
    public float SalvoImpulse { get; init; } = 3500;
    /// <summary>Marker radius relative to blast.</summary>
    public float SalvoMarkerScale { get; init; } = 1;
    /// <summary>Marker ring width (m).</summary>
    public float SalvoMarkerWidth { get; init; } = 0.45f;
    /// <summary>Marker surface offset (m).</summary>
    public float SalvoMarkerLift { get; init; } = 0.12f;
    /// <summary>Acquired rounds; existing magazines keep their capacity.</summary>
    public int MachineGunCapacity { get; init; } = 800;
    /// <summary>Rounds per second, bounded to two rounds per fixed step.</summary>
    public double MachineGunFireRate { get; init; } = 80;
    /// <summary>Maximum damaging ray length in metres.</summary>
    public float MachineGunRange { get; init; } = 225;
    /// <summary>Near-range HP per round.</summary>
    public float MachineGunDamage { get; init; } = 2.25f;
    /// <summary>Distance where damage begins fading.</summary>
    public float MachineGunFalloffStart { get; init; } = 12;
    /// <summary>Power exponent of the fade to zero at maximum range.</summary>
    public float MachineGunFalloff { get; init; } = 1.5f;
    /// <summary>Half-angle of the uniform spread cone in degrees.</summary>
    public float MachineGunSpread { get; init; } = 6;
    /// <summary>Near-range central impulse per round in Newton seconds.</summary>
    public float MachineGunKnockback { get; init; } = 8;
    /// <summary>Render one tracer per this many rounds, including the first.</summary>
    public int MachineGunTracerEvery { get; init; } = 2;
    /// <summary>Contact damage, applied once through vehicle health authority.</summary>
    public float MineDamage { get; init; } = 100;
    /// <summary>Invisible magnetic field extent in metres.</summary>
    public float MineAttractionRadius { get; init; } = 24;
    /// <summary>Weak field baseline in Newtons, ramped continuously from zero at the boundary.</summary>
    public float MineMinimumForce { get; init; } = 12;
    /// <summary>Close-range magnetic force in Newtons.</summary>
    public float MineMaximumForce { get; init; } = 1000;
    /// <summary>Power exponent controlling the distance-to-force curve.</summary>
    public float MineFalloff { get; init; } = 2.5f;
    /// <summary>Contact impulse in Newton seconds.</summary>
    public float MineKnockback { get; init; } = 24000;
    /// <summary>Percentage points consumed per second of held use.</summary>
    public double NitroConsumptionPerSecond { get; init; } = 20;
    /// <summary>Independent forward thrust in newtons, unaffected by throttle or tire traction.</summary>
    public float NitroForwardThrust { get; init; } = 18000;
    /// <summary>Forward drive cap multiplier, still bounded by vehicle physics safety limits.</summary>
    public float NitroSpeedMultiplier { get; init; } = 1.4f;

    /// <summary>Fraction of rocket thrust available without driveable wheel support; zero disables airborne thrust.</summary>
    public float NitroAirborneThrustScale { get; init; } = 1;

    /// <summary>Maximum persistent patches; lowering this cap never deletes existing hazards.</summary>
    public int MaximumOilPatches { get; init; } = 16;

    /// <summary>HP restored to a living vehicle.</summary>
    public float WrenchHeal { get; init; } = 35;
    /// <summary>Metres per second along the launch forward direction.</summary>
    public float MissileSpeed { get; init; } = 45;
    /// <summary>Fixed 60 Hz steps before removal without an explosion.</summary>
    public int MissileLifetimeTicks { get; init; } = 300;
    /// <summary>Explosion radius in metres.</summary>
    public float ExplosionRadius { get; init; } = 8;
    /// <summary>Damage at the exact center.</summary>
    public float MaximumDamage { get; init; } = 55;
    /// <summary>Outward impulse in Newton seconds at the center.</summary>
    public float MaximumImpulse { get; init; } = 15000;

    /// <summary>Rejects nonfinite or excessive host configuration before simulation.</summary>
    public void Validate()
    {
        if (!double.IsFinite(SalvoCount) || SalvoCount < 1 || SalvoCount > 16) { throw new ArgumentException("Invalid salvo count."); }
        if (!double.IsFinite(SalvoIntervalTicks) || SalvoIntervalTicks < 1 || SalvoIntervalTicks > 60) { throw new ArgumentException("Invalid salvo interval_ticks."); }
        if (!double.IsFinite(SalvoRange) || SalvoRange < 25 || SalvoRange > 250) { throw new ArgumentException("Invalid salvo range."); }
        if (!double.IsFinite(SalvoLaunchHeight) || SalvoLaunchHeight < 1 || SalvoLaunchHeight > 10) { throw new ArgumentException("Invalid salvo launch_height."); }
        if (!double.IsFinite(SalvoArcHeight) || SalvoArcHeight < 1 || SalvoArcHeight > 60) { throw new ArgumentException("Invalid salvo arc_height."); }
        if (!double.IsFinite(SalvoSpeed) || SalvoSpeed < 20 || SalvoSpeed > 200) { throw new ArgumentException("Invalid salvo speed."); }
        if (!double.IsFinite(SalvoBlastRadius) || SalvoBlastRadius < 1 || SalvoBlastRadius > 30) { throw new ArgumentException("Invalid salvo blast_radius."); }
        if (!double.IsFinite(SalvoDamage) || SalvoDamage < 0 || SalvoDamage > 1000) { throw new ArgumentException("Invalid salvo damage."); }
        if (!double.IsFinite(SalvoFalloff) || SalvoFalloff < 0.25 || SalvoFalloff > 4) { throw new ArgumentException("Invalid salvo falloff."); }
        if (!double.IsFinite(SalvoImpulse) || SalvoImpulse < 0 || SalvoImpulse > 50000) { throw new ArgumentException("Invalid salvo impulse."); }
        if (!double.IsFinite(SalvoMarkerScale) || SalvoMarkerScale < 0.5 || SalvoMarkerScale > 2) { throw new ArgumentException("Invalid salvo marker_scale."); }
        if (!double.IsFinite(SalvoMarkerWidth) || SalvoMarkerWidth < 0.1 || SalvoMarkerWidth > 2) { throw new ArgumentException("Invalid salvo marker_width."); }
        if (!double.IsFinite(SalvoMarkerLift) || SalvoMarkerLift < 0.02 || SalvoMarkerLift > 0.5) { throw new ArgumentException("Invalid salvo marker_lift."); }
        if (MachineGunCapacity is < 1 or > 10000 || !double.IsFinite(MachineGunFireRate) || MachineGunFireRate is < 1 or > 120 ||
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
        if (MaximumOilPatches is < 1 or > ItemAuthority.MaximumPatches || !float.IsFinite(WrenchHeal) || WrenchHeal < 0 || WrenchHeal > 10000 ||
            !float.IsFinite(MissileSpeed) || MissileSpeed <= 0 || MissileSpeed > 300 ||
            MissileLifetimeTicks is < 1 or > 3600 || !float.IsFinite(ExplosionRadius) || ExplosionRadius <= 0 || ExplosionRadius > 100 ||
            !float.IsFinite(MaximumDamage) || MaximumDamage < 0 || MaximumDamage > 10000 ||
            !float.IsFinite(MaximumImpulse) || MaximumImpulse < 0 || MaximumImpulse > 1000000)
        {
            throw new ArgumentException("Invalid item tuning.");
        }
    }
}
