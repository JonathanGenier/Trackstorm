using System.Numerics;

namespace Trackstorm.Core.Vehicles;

/// <summary>Pure collision and explosion calculations shared by authority, replay and tests.</summary>
public static class VehicleDamageMath
{
    /// <summary>Uses closing speed or solver impulse per victim mass, whichever captures the impact better.</summary>
    /// <param name="relativeVelocity">Victim contact velocity minus other contact velocity, in world space.</param>
    /// <param name="normal">Unit normal pointing away from the other body.</param>
    /// <param name="impulse">Nonnegative native contact impulse magnitude.</param>
    /// <param name="mass">Victim mass; each vehicle evaluates its own received impact.</param>
    /// <returns>Nonnegative impact severity.</returns>
    public static float CollisionSeverity(Vector3 relativeVelocity, Vector3 normal, float impulse, float mass)
    {
        if (!VehiclePhysicsState.IsFinite(relativeVelocity) || !VehiclePhysicsState.IsFinite(normal) || Math.Abs(normal.LengthSquared() - 1) > 0.001f ||
            !float.IsFinite(impulse) || impulse < 0 || !float.IsFinite(mass) || mass <= 0)
        {
            throw new ArgumentException("Invalid contact observation.");
        }

        return Math.Max(Math.Max(0, -Vector3.Dot(relativeVelocity, normal)), impulse / mass);
    }

    /// <summary>Linear damage above the harmless threshold, capped per contact.</summary>
    /// <param name="severity">Nonnegative relative impact severity.</param>
    /// <param name="configuration">Host tuning.</param>
    /// <returns>Nonnegative requested HP loss.</returns>
    public static float CollisionDamage(float severity, DamageConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        if (!float.IsFinite(severity) || severity < 0)
        {
            throw new ArgumentException("Invalid collision severity.", nameof(severity));
        }

        return (float)Math.Min(configuration.MaximumCollisionDamage, Math.Max(0, (double)severity - configuration.CollisionThreshold) * configuration.CollisionScale);
    }

    /// <summary>Produces linearly fading damage and outward/upward impulse; exact center uses up as a stable direction.</summary>
    /// <param name="center">World-space explosion center.</param>
    /// <param name="position">Victim position.</param>
    /// <param name="radius">Positive area radius.</param>
    /// <param name="maximumDamage">Center HP loss.</param>
    /// <param name="maximumImpulse">Center impulse magnitude.</param>
    /// <param name="offset">World-space application offset, allowing a rotational response.</param>
    /// <returns>Generic effect; zero outside the radius.</returns>
    public static DamageEffect Explosion(Vector3 center, Vector3 position, float radius, float maximumDamage, float maximumImpulse, Vector3 offset)
    {
        if (!VehiclePhysicsState.IsFinite(center) || !VehiclePhysicsState.IsFinite(position) || !float.IsFinite(radius) || radius <= 0 ||
            !float.IsFinite(maximumDamage) || maximumDamage < 0 || !float.IsFinite(maximumImpulse) || maximumImpulse < 0 || maximumImpulse > 1_000_000)
        {
            throw new ArgumentException("Invalid explosion input.");
        }

        Vector3 radial = position - center;
        float distance = radial.Length();
        float falloff = Math.Clamp(1 - (distance / radius), 0, 1);
        Vector3 direction = distance > 0.0001f ? Vector3.Normalize((radial / distance) + (Vector3.UnitY * 0.35f)) : Vector3.UnitY;
        return new DamageEffect(maximumDamage * falloff, direction * (maximumImpulse * falloff), offset);
    }
}
