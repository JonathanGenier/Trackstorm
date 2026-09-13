using System.Numerics;

namespace Trackstorm.Core.Vehicles;

/// <summary>Generic damage and physical impulse request; native application belongs to Client.</summary>
public readonly record struct DamageEffect
{
    /// <summary>Creates an effect without depending on an item or engine body.</summary>
    /// <param name="damage">Nonnegative HP request.</param>
    /// <param name="impulse">World-space linear impulse in newton seconds.</param>
    /// <param name="offset">World-space application offset from the body origin.</param>
    public DamageEffect(float damage, Vector3 impulse, Vector3 offset)
    {
        if (!float.IsFinite(damage) || damage < 0 || !VehiclePhysicsState.IsFinite(impulse) || !VehiclePhysicsState.IsFinite(offset) ||
            impulse.LengthSquared() > 1e12f || offset.LengthSquared() > 1e6f)
        {
            throw new ArgumentException("Invalid damage effect.");
        }

        Damage = damage;
        Impulse = impulse;
        Offset = offset;
    }

    /// <summary>Requested HP loss.</summary>
    public float Damage { get; }
    /// <summary>Linear impulse applied by the engine adapter.</summary>
    public Vector3 Impulse { get; }
    /// <summary>Application offset also produces torque through the native inertia tensor.</summary>
    public Vector3 Offset { get; }
}
