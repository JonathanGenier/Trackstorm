namespace Trackstorm.Core.Items;

/// <summary>Host-owned bounded tuning. Linear falloff reaches zero at the edge.</summary>
public sealed record ItemConfiguration
{
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
        if (!float.IsFinite(WrenchHeal) || WrenchHeal < 0 || WrenchHeal > 10000 ||
            !float.IsFinite(MissileSpeed) || MissileSpeed <= 0 || MissileSpeed > 300 ||
            MissileLifetimeTicks is < 1 or > 3600 || !float.IsFinite(ExplosionRadius) || ExplosionRadius <= 0 || ExplosionRadius > 100 ||
            !float.IsFinite(MaximumDamage) || MaximumDamage < 0 || MaximumDamage > 10000 ||
            !float.IsFinite(MaximumImpulse) || MaximumImpulse < 0 || MaximumImpulse > 1000000)
        {
            throw new ArgumentException("Invalid item tuning.");
        }
    }
}
