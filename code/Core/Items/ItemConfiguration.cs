namespace Trackstorm.Core.Items;

/// <summary>Host-owned bounded tuning. Linear falloff reaches zero at the edge.</summary>
public sealed record ItemConfiguration
{
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
