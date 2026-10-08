using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Shared engine-independent rack articulation, clearance and firing contract.</summary>
public static class WeaponAim
{
    /// <summary>Production Car's fully deployed payload pivot; rack remains fixed.</summary>
    public static Vector3 Pivot => new(0, 1.81f, 1.845f);
    /// <summary>Only direct-fire identities participate; Salvo retains its separate mechanics.</summary>
    public static bool Supports(HeldItem item) => item is HeldItem.MachineGun or HeldItem.Missile;
    /// <summary>Requests must already be finite unit directions, never arbitrary magnitudes.</summary>
    public static bool IsDirection(Vector3 direction) => VehiclePhysicsState.IsFinite(direction) && Math.Abs(direction.LengthSquared() - 1) <= .002f;

    /// <summary>Solves on the host pose, enforcing rate, angular and conservative production-chassis clearance.</summary>
    public static WeaponAimSolution Solve(VehicleSnapshot vehicle, ulong token, Vector3 desired, WeaponAimSolution? previous, WeaponAimConfiguration tuning, ulong tick, VehiclePhysicsState? observedPose = null)
    {
        if (!IsDirection(desired)) { throw new ArgumentException("Aim requires a unit direction."); }
        var pose = observedPose ?? vehicle.ObservedPhysics;
        Vector3 local = Vector3.Transform(desired, Quaternion.Conjugate(pose.Orientation));
        float yaw = MathF.Atan2(-local.X, -local.Z);
        float pitch = Math.Clamp(MathF.Asin(Math.Clamp(local.Y, -1, 1)), -tuning.DownDegrees * MathF.PI / 180, tuning.UpDegrees * MathF.PI / 180);
        float step = tuning.TurnRate * MathF.PI / 10800;
        float oldYaw = previous?.Yaw ?? 0;
        float oldPitch = previous?.Pitch ?? 0;
        yaw = MathF.IEEERemainder(oldYaw + Math.Clamp(MathF.IEEERemainder(yaw - oldYaw, MathF.Tau), -step, step), MathF.Tau);
        pitch = oldPitch + Math.Clamp(pitch - oldPitch, -step, step);
        // New physical/tuning limits take precedence over retained interpolation memory.
        pitch = Math.Clamp(pitch, -tuning.DownDegrees * MathF.PI / 180, tuning.UpDegrees * MathF.PI / 180);
        // A conservative body envelope includes armor and roof. The pivot-to-muzzle segment
        // is included, so offsetting a future muzzle can never skip through the owner's Car.
        Vector3 direction = Direction(yaw, pitch);
        bool clear = !IntersectsBody(Pivot, direction);
        if (!clear)
        {
            float lo = pitch, hi = 0;
            for (int i = 0; i < 20; i++)
            {
                float mid = (lo + hi) * .5f;
                if (IntersectsBody(Pivot, Direction(yaw, mid))) { lo = mid; } else { hi = mid; }
            }
            pitch = hi;
            direction = Direction(yaw, pitch);
        }
        return new(vehicle.VehicleId, vehicle.LifeId, token, tick, yaw, pitch,
            pose.Position + Vector3.Transform(Pivot, pose.Orientation), Vector3.Normalize(Vector3.Transform(direction, pose.Orientation)), clear);
    }

    /// <summary>Chassis-relative yaw then payload pitch, matching the Client hierarchy.</summary>
    public static Vector3 Direction(float yaw, float pitch) => new(-MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), -MathF.Cos(yaw) * MathF.Cos(pitch));

    /// <summary>Conservative swept ray test, also reusable for future muzzle contracts.</summary>
    public static bool IntersectsBody(Vector3 origin, Vector3 direction) => BodyDistance(origin, direction).HasValue;

    /// <summary>Distance to the conservative owner envelope, including a ray that starts inside it.</summary>
    public static float? BodyDistance(Vector3 origin, Vector3 direction)
    {
        Vector3 minimum = new(-VehicleDimensions.Width / 2 - .05f, -1.1f, -VehicleDimensions.Length / 2 - .05f);
        Vector3 maximum = new(VehicleDimensions.Width / 2 + .05f, 1.30f, VehicleDimensions.Length / 2 + .05f);
        float near = 0, far = 10;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
            float d = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
            float min = axis == 0 ? minimum.X : axis == 1 ? minimum.Y : minimum.Z;
            float max = axis == 0 ? maximum.X : axis == 1 ? maximum.Y : maximum.Z;
            if (Math.Abs(d) < 1e-6f) { if (o < min || o > max) { return null; } continue; }
            float a = (min - o) / d, b = (max - o) / d;
            near = Math.Max(near, Math.Min(a, b));
            far = Math.Min(far, Math.Max(a, b));
            if (near > far) { return null; }
        }
        return far >= near ? near : null;
    }
}
