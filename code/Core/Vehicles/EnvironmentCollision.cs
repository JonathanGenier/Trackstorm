using System.Numerics;

namespace Trackstorm.Core.Vehicles;

/// <summary>Dissipative static-obstacle response shared by native practice and replay sweeps.</summary>
public static class EnvironmentCollision
{
    /// <summary>Removes the support-normal part of a side face so bevels cannot turn road speed into lift.</summary>
    public static Vector3 ResponseNormal(Vector3 normal, Vector3 support)
    {
        Vector3 up = support.Y >= 0.55f ? support : Vector3.UnitY;
        Vector3 side = normal - up * Vector3.Dot(normal, up);
        return side.LengthSquared() > 0.01f ? Vector3.Normalize(side) : normal;
    }

    /// <summary>Continuous incidence weighting; shallow rubbing has no damaging component.</summary>
    public static float Severity(Vector3 velocity, Vector3 normal)
    {
        float closing = Math.Max(0, -Vector3.Dot(velocity, normal));
        float incidence = closing / Math.Max(0.001f, velocity.Length());
        float blend = Math.Clamp((incidence - 0.2f) / 0.6f, 0, 1);
        return closing * blend * blend * (3 - 2 * blend);
    }

    /// <summary>Resolves a whole static manifold once: no restitution, one drag application and one bounded torque.</summary>
    public static VehiclePhysicsState Resolve(VehiclePhysicsState incoming, Vector3 support, IReadOnlyList<VehicleContact> contacts, VehicleConfiguration configuration)
    {
        Vector3 velocity = incoming.LinearVelocity;
        Vector3 torque = Vector3.Zero;
        float strongest = 0;
        bool touching = false;
        foreach (VehicleContact contact in contacts)
        {
            if (!contact.StaticObstacle) { continue; }
            touching = true;
            Vector3 normal = ResponseNormal(contact.Normal, support);
            float closing = Math.Max(0, -Vector3.Dot(velocity, normal));
            velocity += normal * closing;
            float severity = Severity(incoming.LinearVelocity, normal);
            if (severity > strongest)
            {
                strongest = severity;
                Vector3 lever = Vector3.Transform(contact.LocalPosition, incoming.Orientation);
                // A severe eccentric crash retains geometry-driven rotation. A scrape cannot build spin.
                torque = Vector3.Cross(lever, normal * severity) * (configuration.CrashRotation / (configuration.Wheelbase * configuration.Wheelbase / 3));
            }
        }

        if (!touching) { return incoming; }
        Vector3 up = support.Y >= 0.55f ? support : Vector3.UnitY;
        Vector3 vertical = up * Vector3.Dot(velocity, up);
        float directness = strongest / Math.Max(0.001f, incoming.LinearVelocity.Length());
        // A direct face in a multi-face manifold must also dissipate lateral motion introduced by a bevel.
        float crashRetention = 1 - configuration.CrashDissipation * directness * directness;
        // Time-based resistance is applied once, independent of manifold/slide count or Nitro state.
        velocity = vertical + (velocity - vertical) * crashRetention * MathF.Exp(-configuration.WallDrag / configuration.TicksPerSecond);
        Vector3 angular = incoming.AngularVelocity + VehicleMovement.Limit(torque, configuration.CrashAngularLimit);
        return new(incoming.Position, incoming.Orientation, velocity, VehicleMovement.Limit(angular, configuration.MaximumAngularSpeed));
    }
}
