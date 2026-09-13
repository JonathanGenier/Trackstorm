using System.Numerics;

namespace Trackstorm.Core.Vehicles;

/// <summary>Simulation-owned vehicle aggregate. Prepares complete candidate states before atomic world publication.</summary>
internal sealed class VehicleAuthority
{
    private readonly VehicleConfiguration _movementConfiguration;
    private readonly DamageConfiguration _damageConfiguration;

    /// <summary>Constructs a fresh aggregate with the simulation's registered tuning.</summary>
    /// <param name="id">Stable vehicle identity.</param>
    /// <param name="movement">Movement tuning.</param>
    /// <param name="damage">Health tuning.</param>
    /// <param name="initial">Initial plain physics state.</param>
    internal VehicleAuthority(ulong id, VehicleConfiguration movement, DamageConfiguration damage, VehiclePhysicsState initial)
    {
        movement.Validate();
        damage.Validate();
        _movementConfiguration = movement;
        _damageConfiguration = damage;
        Snapshot = new VehicleSnapshot(id, 1, new VehicleState(0, initial, false, false, 0, 0), new VehicleHealth(damage).State, initial);
    }

    /// <summary>Last committed aggregate.</summary>
    internal VehicleSnapshot Snapshot { get; private set; }

    /// <summary>Evaluates a candidate without mutating the published aggregate.</summary>
    /// <param name="request">This global tick's inputs and observations.</param>
    /// <returns>Complete candidate state and commands.</returns>
    internal VehicleStepResult Prepare(VehicleStepRequest request)
    {
        VehicleSnapshot previous = Snapshot;
        VehicleObservation observed = request.Reset is VehiclePhysicsState reset ? new VehicleObservation(reset, Vector3.Zero) : request.Observation;
        var movement = new VehicleMovement(_movementConfiguration, observed.Physics);
        var health = new VehicleHealth(_damageConfiguration);
        if (request.Reset.HasValue)
        {
            movement.Restore(new VehicleState(previous.Movement.Tick, observed.Physics, false, false, 0, 0));
        }
        else
        {
            movement.Restore(previous.Movement);
            health.Restore(previous.Damage);
        }

        var events = new List<DamageEvent>();
        foreach (VehicleEffectRequest effect in request.Effects)
        {
            if (health.ApplyDamage(effect.Effect.Damage, effect.Attribution, request.Input.Tick) is DamageEvent outcome)
            {
                events.Add(outcome);
            }
        }

        float severity = 0;
        VehicleContact strongest = default;
        foreach (VehicleContact contact in observed.Contacts)
        {
            float candidate = VehicleDamageMath.CollisionSeverity(contact.RelativeVelocity, contact.Normal, contact.Impulse, _movementConfiguration.Mass);
            if (candidate > severity)
            {
                severity = candidate;
                strongest = contact;
            }
        }

        if (severity > 0 && health.ApplyCollision(severity, new DamageContext("collision", strongest.OtherVehicleId, strongest.OtherVehicleId == 0 ? "world-or-prop" : "vehicle"), request.Input.Tick) is DamageEvent collision)
        {
            events.Add(collision);
        }

        health.Repair(request.Repair);
        VehicleState next = movement.Step(request.Input, observed.Physics, observed.Support, !health.State.Destroyed, observed.Surface);
        var snapshot = new VehicleSnapshot(previous.VehicleId, request.Reset.HasValue ? checked(previous.LifeId + 1) : previous.LifeId, next, health.State, observed.Physics, request.Effects);
        return new VehicleStepResult(snapshot, request.Effects, events, request.Reset.HasValue);
    }

    /// <summary>Checks restoration against the registered tuning without changing state.</summary>
    /// <param name="snapshot">Proposed aggregate.</param>
    /// <param name="tick">Global restored tick.</param>
    internal void ValidateRestore(VehicleSnapshot snapshot, ulong tick)
    {
        if (snapshot.VehicleId != Snapshot.VehicleId || snapshot.Movement.Tick != tick)
        {
            throw new ArgumentException("Vehicle identity/tick differs from the simulation snapshot.");
        }

        var movement = new VehicleMovement(_movementConfiguration, snapshot.ObservedPhysics);
        movement.Restore(snapshot.Movement);
        new VehicleHealth(_damageConfiguration).Restore(snapshot.Damage);
    }

    /// <summary>Publishes a previously validated candidate after the whole batch succeeds.</summary>
    /// <param name="snapshot">Validated candidate.</param>
    internal void Commit(VehicleSnapshot snapshot) => Snapshot = snapshot;
}
