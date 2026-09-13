namespace Trackstorm.Core.Vehicles;

/// <summary>Complete serializable health and collision-gate state for HUD and authority restoration.</summary>
public sealed record VehicleDamageState
{
    /// <summary>Creates validated gameplay state, including all damage replay memory.</summary>
    /// <param name="maxHP">Configured positive health capacity.</param>
    /// <param name="currentHP">Health in the closed range zero to capacity.</param>
    /// <param name="lastDamage">Last positive damage event, or null on a fresh life.</param>
    /// <param name="lastCollisionTick">Last damaging collision tick, or null.</param>
    public VehicleDamageState(float maxHP, float currentHP, DamageEvent? lastDamage, ulong? lastCollisionTick)
    {
        if (!float.IsFinite(maxHP) || maxHP <= 0 || maxHP > 1_000_000 || !float.IsFinite(currentHP) || currentHP < 0 || currentHP > maxHP ||
            (lastDamage is not null && (lastDamage.Amount > maxHP || lastDamage.DestroyedTransition != (currentHP == 0))) ||
            (lastCollisionTick.HasValue && (lastDamage is null || lastCollisionTick.Value > lastDamage.Tick)))
        {
            throw new ArgumentException("Invalid vehicle health snapshot.");
        }

        MaxHP = maxHP;
        CurrentHP = currentHP;
        LastDamage = lastDamage;
        LastCollisionTick = lastCollisionTick;
    }

    /// <summary>Maximum health.</summary>
    public float MaxHP { get; }
    /// <summary>Current authoritative health.</summary>
    public float CurrentHP { get; }
    /// <summary>Wrecks stay destroyed until an explicit reset.</summary>
    public bool Destroyed => CurrentHP == 0;
    /// <summary>Latest outcome and kill-candidate metadata.</summary>
    public DamageEvent? LastDamage { get; }
    /// <summary>Deterministic collision cooldown memory.</summary>
    public ulong? LastCollisionTick { get; }
}
