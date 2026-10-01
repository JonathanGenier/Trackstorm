using System.Numerics;

namespace Trackstorm.Core.Vehicles;

/// <summary>Contact classification shared by crash movement and authoritative impact damage.</summary>
internal static class VehicleCrash
{
    internal static int WheelCount(WheelSupport? wheels) => wheels is { } support
        ? (support.Compression.X > 0 ? 1 : 0) + (support.Compression.Y > 0 ? 1 : 0) +
          (support.Compression.Z > 0 ? 1 : 0) + (support.Compression.W > 0 ? 1 : 0) : 0;

    internal static float Severity(VehicleContact contact, Vector3 support, float mass) => contact.StaticObstacle
        ? EnvironmentCollision.Severity(contact.RelativeVelocity, EnvironmentCollision.ResponseNormal(contact.Normal, support))
        : VehicleDamageMath.CollisionSeverity(contact.RelativeVelocity, contact.Normal, contact.Impulse, mass);

    // Opposite faces are distinct impacts even when a rolling manifold does not fully
    // separate. Persistent points on the same face are one contact, regardless of count.
    internal static byte Face(Quaternion orientation, VehicleContact contact)
    {
        Vector3 normal = Vector3.Transform(contact.Normal, Quaternion.Conjugate(orientation));
        Vector3 magnitude = Vector3.Abs(normal);
        int axis = magnitude.Y >= magnitude.X && magnitude.Y >= magnitude.Z ? 2 : magnitude.X >= magnitude.Z ? 0 : 4;
        float component = axis == 0 ? normal.X : axis == 2 ? normal.Y : normal.Z;
        return (byte)(1 << (axis + (component < 0 ? 1 : 0)));
    }
}
