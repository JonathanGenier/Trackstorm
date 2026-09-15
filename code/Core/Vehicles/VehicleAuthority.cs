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
    /// <param name="respawn">Host respawn policy, or null in isolated fixtures.</param>
    /// <param name="arena">Validated spawn contract.</param>
    /// <param name="vehicles">Authoritative roster with earlier candidate respawns reserved.</param>
    internal VehicleStepResult Prepare(VehicleStepRequest request, RespawnConfiguration? respawn, Arenas.ArenaConfiguration arena, IReadOnlyList<VehicleSnapshot> vehicles)
    {
        VehicleSnapshot previous = Snapshot;
        if (!request.Reset.HasValue && !previous.CanInteract)
        {
            VehiclePhysicsState? spawn = respawn is not null && previous.RespawnAtTick is ulong deadline && request.Input.Tick >= deadline
                ? arena.SelectRespawn(previous.VehicleId, checked(previous.LifeId + 1), vehicles) : null;
            bool ready = spawn.HasValue;
            ulong life = ready ? checked(previous.LifeId + 1) : previous.LifeId;
            VehiclePhysicsState pose = ready ? spawn!.Value
                : new VehiclePhysicsState(previous.Movement.Physics.Position, previous.Movement.Physics.Orientation, Vector3.Zero, Vector3.Zero);
            var state = new VehicleSnapshot(previous.VehicleId, life, new VehicleState(request.Input.Tick, pose, false, false, 0, 0), ready ? new VehicleHealth(_damageConfiguration).State : previous.Damage, pose, lifecycle: ready ? VehicleLifecycle.Alive : VehicleLifecycle.Respawning, respawnAtTick: ready ? null : previous.RespawnAtTick);
            return new VehicleStepResult(state, Array.Empty<VehicleEffectRequest>(), new List<DamageEvent>(), ready);
        }

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
        IReadOnlyList<VehicleEffectRequest> acceptedEffects = health.State.Destroyed ? Array.Empty<VehicleEffectRequest>() : request.Effects;
        if (health.State.Destroyed)
        {
            var stationary = new VehiclePhysicsState(observed.Physics.Position, observed.Physics.Orientation, Vector3.Zero, Vector3.Zero);
            next = new VehicleState(request.Input.Tick, stationary, false, false, 0, 0);
        }

        var snapshot = new VehicleSnapshot(previous.VehicleId, request.Reset.HasValue ? checked(previous.LifeId + 1) : previous.LifeId, next, health.State, observed.Physics, acceptedEffects, respawnAtTick: health.State.Destroyed && respawn is not null ? checked(request.Input.Tick + respawn.DelayTicks) : null);
        return new VehicleStepResult(snapshot, acceptedEffects, events, request.Reset.HasValue);
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
