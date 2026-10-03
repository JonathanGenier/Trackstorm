using System.Numerics;

namespace Trackstorm.Core.Vehicles;

/// <summary>Body-impact impulse response for the synchronous terrain sweep adapter.</summary>
public static class TerrainCollision
{
    /// <summary>Whether terrain or an intact rock top needs the body's inelastic lever-arm response.</summary>
    public static bool IsBodyImpact(Quaternion orientation, VehicleContact contact, WheelSupport? wheels = null) =>
        contact.OtherVehicleId == 0 && ((contact.Terrain && !VehicleLanding.SafeContact(orientation, contact, wheels)) ||
            (contact.EnvironmentRock != 0 && !contact.StaticObstacle && contact.Normal.Y >= 0.55f));

    /// <summary>Retains lever-arm rotation and bounded contact friction without adding restitution or changing pose.</summary>
    public static VehiclePhysicsState Resolve(VehiclePhysicsState incoming, VehicleContact contact, VehicleConfiguration configuration)
    {
        if (!IsBodyImpact(incoming.Orientation, contact)) { return incoming; }
        Vector3 lever = Vector3.Transform(contact.LocalPosition, incoming.Orientation);
        Vector3 relative = incoming.LinearVelocity + Vector3.Cross(incoming.AngularVelocity, lever);
        float closing = Math.Max(0, -Vector3.Dot(relative, contact.Normal));
        float inertia = configuration.Wheelbase * configuration.Wheelbase / 3;
        float impulse = closing / (1 + Vector3.Cross(lever, contact.Normal).LengthSquared() / inertia);
        Vector3 delta = contact.Normal * impulse;
        // Solve friction after the normal impulse so coupled lever arms cannot
        // remove the same contact energy twice and accidentally create rotation.
        Vector3 afterNormal = relative + delta + Vector3.Cross(Vector3.Cross(lever, delta) / inertia, lever);
        Vector3 tangent = afterNormal - contact.Normal * Vector3.Dot(afterNormal, contact.Normal);
        if (tangent.LengthSquared() > 0.0001f)
        {
            Vector3 direction = Vector3.Normalize(tangent);
            // A rock under the chassis is not tire purchase. Match the native
            // chassis friction so a high-centred car can slide off a sloped top.
            float friction = Math.Min(impulse * (contact.EnvironmentRock != 0 ? 0.15f : 0.65f), tangent.Length() / (1 + Vector3.Cross(lever, direction).LengthSquared() / inertia));
            delta -= direction * friction;
        }
        return new(incoming.Position, incoming.Orientation, incoming.LinearVelocity + delta,
            VehicleMovement.Limit(incoming.AngularVelocity + Vector3.Cross(lever, delta) / inertia, configuration.MaximumAngularSpeed));
    }
}
