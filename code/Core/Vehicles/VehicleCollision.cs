using System.Numerics;

namespace Trackstorm.Core.Vehicles;

/// <summary>Inelastic vehicle contact response; raw impact observations remain available to damage authority.</summary>
public static class VehicleCollision
{
    /// <summary>Exchanges equal and opposite momentum once at a contact, without restitution or an upright target.</summary>
    public static (VehiclePhysicsState First, VehiclePhysicsState Second) ResolvePair(
        VehiclePhysicsState first, VehicleConfiguration firstTuning,
        VehiclePhysicsState second, VehicleConfiguration secondTuning, Vector3 normal, Vector3 point)
    {
        if (!VehiclePhysicsState.IsFinite(normal) || Math.Abs(normal.LengthSquared() - 1) > 0.001f || !VehiclePhysicsState.IsFinite(point))
        {
            throw new ArgumentException("Collision requires a unit normal and finite contact point.");
        }
        firstTuning.Validate();
        secondTuning.Validate();
        Vector3 firstLever = point - first.Position;
        Vector3 secondLever = point - second.Position;
        Vector3 relative = first.LinearVelocity + Vector3.Cross(first.AngularVelocity, firstLever) -
            second.LinearVelocity - Vector3.Cross(second.AngularVelocity, secondLever);
        float closing = Math.Max(0, -Vector3.Dot(relative, normal));
        if (closing == 0) { return (first, second); }
        float firstInverseInertia = firstTuning.CrashRotation * 3 / (firstTuning.Mass * firstTuning.Wheelbase * firstTuning.Wheelbase);
        float secondInverseInertia = secondTuning.CrashRotation * 3 / (secondTuning.Mass * secondTuning.Wheelbase * secondTuning.Wheelbase);
        float effectiveInverseMass = 1 / firstTuning.Mass + 1 / secondTuning.Mass +
            Vector3.Cross(firstLever, normal).LengthSquared() * firstInverseInertia +
            Vector3.Cross(secondLever, normal).LengthSquared() * secondInverseInertia;
        Vector3 impulse = normal * (closing / effectiveInverseMass);
        return (Apply(first, firstTuning, firstLever, impulse, firstInverseInertia),
            Apply(second, secondTuning, secondLever, -impulse, secondInverseInertia));
    }

    private static VehiclePhysicsState Apply(VehiclePhysicsState body, VehicleConfiguration tuning, Vector3 lever, Vector3 impulse, float inverseInertia)
    {
        Vector3 rotation = Vector3.Cross(lever, impulse) * inverseInertia;
        rotation = tuning.CrashAngularLimit == 0 ? Vector3.Zero : VehicleMovement.Limit(rotation, tuning.CrashAngularLimit);
        return new(body.Position, body.Orientation, body.LinearVelocity + impulse / tuning.Mass,
            VehicleMovement.Limit(body.AngularVelocity + rotation, tuning.MaximumAngularSpeed));
    }
}
