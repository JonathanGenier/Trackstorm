namespace Trackstorm.Core.Vehicles;

/// <summary>Complete authoritative vehicle aggregate at a global tick, before commands are applied natively.</summary>
public sealed record VehicleSnapshot
{
    /// <summary>Combines identity, life, solved observations, movement commands/memory and health.</summary>
    /// <param name="vehicleId">Stable vehicle identity.</param>
    /// <param name="lifeId">Explicit life generation; reset increments it without rewinding global time.</param>
    /// <param name="movement">Movement commands and deterministic timers at the global tick.</param>
    /// <param name="damage">Health, attribution and collision memory.</param>
    /// <param name="observedPhysics">Solved native state before this tick's movement/effect commands.</param>
    /// <param name="effects">Accepted one-shot native effects belonging to this command boundary.</param>
    public VehicleSnapshot(ulong vehicleId, ulong lifeId, VehicleState movement, VehicleDamageState damage, VehiclePhysicsState observedPhysics, IEnumerable<VehicleEffectRequest>? effects = null)
    {
        ArgumentNullException.ThrowIfNull(damage);
        _ = new VehicleState(movement.Tick, movement.Physics, movement.Grounded, movement.Drifting, movement.DriftTicks, movement.BoostTicks);
        _ = new VehiclePhysicsState(observedPhysics.Position, observedPhysics.Orientation, observedPhysics.LinearVelocity, observedPhysics.AngularVelocity);
        if (vehicleId == 0 || lifeId == 0 || damage.LastDamage?.Tick > movement.Tick ||
            observedPhysics.Position != movement.Physics.Position || observedPhysics.Orientation != movement.Physics.Orientation ||
            (damage.Destroyed && (movement.Drifting || movement.BoostTicks != 0)))
        {
            throw new ArgumentException("Incoherent authoritative vehicle snapshot.");
        }

        VehicleId = vehicleId;
        LifeId = lifeId;
        Movement = movement;
        Damage = damage;
        ObservedPhysics = observedPhysics;
        VehicleEffectRequest[] copy = effects?.ToArray() ?? [];
        foreach (VehicleEffectRequest effect in copy)
        {
            ArgumentNullException.ThrowIfNull(effect);
        }

        Effects = Array.AsReadOnly(copy);
    }

    /// <summary>Stable identity.</summary>
    public ulong VehicleId { get; }
    /// <summary>Life generation, distinct from the global movement tick.</summary>
    public ulong LifeId { get; }
    /// <summary>Core movement state and commanded velocities.</summary>
    public VehicleState Movement { get; }
    /// <summary>Core health, damage and replay state.</summary>
    public VehicleDamageState Damage { get; }
    /// <summary>Latest accepted solved pose and velocity observation.</summary>
    public VehiclePhysicsState ObservedPhysics { get; }
    /// <summary>Accepted native impulses, applied once with this tick's commands when reconstructing the body.</summary>
    public IReadOnlyList<VehicleEffectRequest> Effects { get; }
    /// <summary>Nonnegative horizontal solved travel speed in m/s; gravity/jump velocity does not inflate the HUD.</summary>
    public float Speed => (float)Math.Sqrt(((double)ObservedPhysics.LinearVelocity.X * ObservedPhysics.LinearVelocity.X) + ((double)ObservedPhysics.LinearVelocity.Z * ObservedPhysics.LinearVelocity.Z));
}
