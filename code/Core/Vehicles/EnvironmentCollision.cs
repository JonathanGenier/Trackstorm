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

    /// <summary>Constrains fresh drive against the whole rock manifold without projecting one face into another.</summary>
    internal static Vector3 ConstrainRockDrive(Vector3 desired, Vector3 incoming, Vector3 support, IReadOnlyList<VehicleContact>? contacts)
    {
        if (contacts is null) { return desired; }
        var planes = contacts.Where(c => c.EnvironmentRock != 0 && c.StaticObstacle).Select(c =>
        {
            Vector3 normal = ResponseNormal(c.Normal, support);
            return (Normal: normal, Limit: Math.Min(0, Vector3.Dot(incoming, normal)));
        }).ToArray();
        bool Allowed(Vector3 value) => planes.All(p => Vector3.Dot(value, p.Normal) >= p.Limit - 0.000001f);
        if (Allowed(desired)) { return desired; }
        Vector3 best = incoming;
        float distance = Vector3.DistanceSquared(desired, best);
        void Consider(Vector3 value)
        {
            float change = Vector3.DistanceSquared(desired, value);
            if (change < distance && Allowed(value)) { best = value; distance = change; }
        }
        // Side normals lie in the support plane. Its nearest feasible velocity
        // is free, on one face, or at the intersection of two faces. Solving the
        // set together avoids order-dependent outward kicks in a rock crevice.
        for (int i = 0; i < planes.Length; i++)
        {
            var first = planes[i];
            float a = first.Limit - Vector3.Dot(desired, first.Normal);
            Consider(desired + first.Normal * a);
            for (int j = 0; j < i; j++)
            {
                var second = planes[j];
                float dot = Vector3.Dot(first.Normal, second.Normal);
                float denominator = 1 - dot * dot;
                if (denominator < 0.000001f) { continue; }
                float b = second.Limit - Vector3.Dot(desired, second.Normal);
                Consider(desired + first.Normal * ((a - dot * b) / denominator) + second.Normal * ((b - dot * a) / denominator));
            }
        }
        return best;
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
        Vector3 angular = incoming.AngularVelocity + (configuration.CrashAngularLimit == 0 ? Vector3.Zero : VehicleMovement.Limit(torque, configuration.CrashAngularLimit));
        return new(incoming.Position, incoming.Orientation, velocity, VehicleMovement.Limit(angular, configuration.MaximumAngularSpeed));
    }
}
