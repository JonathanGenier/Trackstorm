namespace Trackstorm.Core.Vehicles;

/// <summary>Central authority-owned HP mutation and destruction flow; no engine dependencies or wall-clock time.</summary>
public sealed class VehicleHealth
{
    private readonly DamageConfiguration _configuration;

    /// <summary>Creates a fresh life with configured health.</summary>
    /// <param name="configuration">Host-owned damage tuning.</param>
    public VehicleHealth(DamageConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        _configuration = configuration;
        State = new VehicleDamageState(configuration.MaxHP, configuration.MaxHP, null, null);
    }

    /// <summary>Read-only health and replay memory.</summary>
    public VehicleDamageState State { get; private set; }

    /// <summary>Applies positive damage once; zero/negative requests never heal or replace attribution.</summary>
    /// <param name="amount">Requested HP loss.</param>
    /// <param name="attribution">Source and instigator metadata.</param>
    /// <param name="tick">Monotonic authority tick within the life.</param>
    /// <returns>New outcome, or null when no HP was removed.</returns>
    public DamageEvent? ApplyDamage(float amount, DamageContext attribution, ulong tick)
    {
        ArgumentNullException.ThrowIfNull(attribution);
        if (!float.IsFinite(amount) || (State.LastDamage is not null && tick < State.LastDamage.Tick))
        {
            throw new ArgumentException("Damage must be finite and ordered.");
        }

        if (amount <= 0 || State.Destroyed)
        {
            return null;
        }

        float hp = Math.Max(0, State.CurrentHP - amount);
        float loss = State.CurrentHP - hp;
        if (loss == 0)
        {
            return null;
        }

        var outcome = new DamageEvent(checked((State.LastDamage?.Sequence ?? 0) + 1), tick, loss, attribution, hp == 0);
        State = new VehicleDamageState(State.MaxHP, hp, outcome, State.LastCollisionTick);
        return outcome;
    }

    /// <summary>Gates damaging contacts to avoid duplicate manifold points and sustained-contact damage storms.</summary>
    /// <param name="severity">Core-computed impact severity.</param>
    /// <param name="attribution">Other vehicle or world metadata.</param>
    /// <param name="tick">Current authority fixed tick.</param>
    /// <returns>New outcome if this contact removes HP.</returns>
    public DamageEvent? ApplyCollision(float severity, DamageContext attribution, ulong tick)
    {
        float amount = VehicleDamageMath.CollisionDamage(severity, _configuration);
        if (State.LastCollisionTick is ulong previous && (tick < previous || tick - previous < _configuration.CollisionCooldownTicks))
        {
            return null;
        }

        DamageEvent? outcome = ApplyDamage(amount, attribution, tick);
        if (outcome is not null)
        {
            State = new VehicleDamageState(State.MaxHP, State.CurrentHP, outcome, tick);
        }

        return outcome;
    }

    /// <summary>Repairs a living vehicle without exceeding capacity; does not revive a destroyed life.</summary>
    /// <param name="amount">Requested repair; negative values are ignored.</param>
    public void Repair(float amount)
    {
        if (!float.IsFinite(amount))
        {
            throw new ArgumentException("Repair must be finite.", nameof(amount));
        }

        if (!State.Destroyed)
        {
            State = new VehicleDamageState(State.MaxHP, (float)Math.Min(State.MaxHP, (double)State.CurrentHP + Math.Max(0, amount)), State.LastDamage, State.LastCollisionTick);
        }
    }

    /// <summary>Explicitly starts a new life and resets damage attribution/cooldowns.</summary>
    public void Reset() => State = new VehicleDamageState(_configuration.MaxHP, _configuration.MaxHP, null, null);

    /// <summary>Restores a complete validated authority snapshot without emitting a death event.</summary>
    /// <param name="state">Health and all replay memory for the configured capacity.</param>
    public void Restore(VehicleDamageState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.MaxHP != _configuration.MaxHP)
        {
            throw new ArgumentException("Snapshot health capacity differs from configuration.", nameof(state));
        }

        State = state;
    }
}
