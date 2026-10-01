using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Temporary rear armor envelope shared by authoritative intersection and native reconstruction.</summary>
public static class TombstoneGeometry
{
    /// <summary>Vehicle-local center, behind the positive-Z bumper.</summary>
    public static Vector3 Center => new(0, 0.2f, VehicleDimensions.Length / 2 + 0.25f);
    /// <summary>Physical box dimensions in metres.</summary>
    public static Vector3 Size => new(VehicleDimensions.Width, 1.5f, 0.35f);

    /// <summary>Tests an actual local contact, with only native solver margin tolerance.</summary>
    public static bool Contains(Vector3 localPoint)
    {
        var distance = Vector3.Abs(localPoint - Center);
        return distance.X <= Size.X / 2 + 0.02f && distance.Y <= Size.Y / 2 + 0.02f && distance.Z <= Size.Z / 2 + 0.02f;
    }

    /// <summary>Closest segment intersection with the full oriented box, including starts inside it.</summary>
    public static float? Intersect(VehiclePhysicsState pose, Vector3 start, Vector3 end)
    {
        var inverse = Quaternion.Conjugate(pose.Orientation);
        var origin = Vector3.Transform(start - pose.Position, inverse) - Center;
        var direction = Vector3.Transform(end - start, inverse);
        var half = Size / 2;
        float near = 0, far = 1;
        for (int axis = 0; axis < 3; axis++)
        {
            float p = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
            float d = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
            float h = axis == 0 ? half.X : axis == 1 ? half.Y : half.Z;
            if (Math.Abs(d) < 0.000001f) { if (Math.Abs(p) > h) { return null; } continue; }
            float a = (-h - p) / d, b = (h - p) / d;
            near = Math.Max(near, Math.Min(a, b));
            far = Math.Min(far, Math.Max(a, b));
            if (near > far) { return null; }
        }
        return near;
    }
}
