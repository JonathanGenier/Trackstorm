using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Portable parabola committed at each launch, including reserved rounds for checkpoint continuation.</summary>
public sealed record SalvoFlight(Vector3 Origin, Vector3 Target, float Height, int DurationTicks, int ElapsedTicks, int DelayTicks, ulong Life)
{
    /// <summary>Analytic arc; integer time avoids integration drift at the intended impact point.</summary>
    public Vector3 At(int tick)
    {
        float t = Math.Clamp((float)tick / DurationTicks, 0, 1);
        return Vector3.Lerp(Origin, Target, t) + Vector3.UnitY * (4 * Height * t * (1 - t));
    }

    /// <summary>Starts above the current host-observed vehicle toward this round's committed target.</summary>
    public SalvoFlight Launch(Vector3 origin, float speed)
    {
        var curve = this with { Origin = origin, DurationTicks = 60 };
        float length = 0;
        for (int i = 1; i <= 60; i++) { length += Vector3.Distance(curve.At(i - 1), curve.At(i)); }
        return this with { Origin = origin, DurationTicks = Math.Max(1, (int)Math.Ceiling(length / speed * 60)), DelayTicks = 0 };
    }

    /// <summary>Rejects malformed continuation before restore or publication.</summary>
    public void Validate()
    {
        if (!VehiclePhysicsState.IsFinite(Origin) || !VehiclePhysicsState.IsFinite(Target) ||
            !float.IsFinite(Height) || Height is < 1 or > 60 || DurationTicks is < 1 or > 3600 ||
            ElapsedTicks < 0 || ElapsedTicks >= DurationTicks || DelayTicks is < 0 or > 900 ||
            (DelayTicks > 0 && ElapsedTicks != 0) || Life == 0 || Vector3.Distance(Origin, Target) is < 1 or > 600)
        {
            throw new ArgumentException("Invalid salvo continuation.");
        }
    }

    /// <summary>Fixed horizontal forward range, independent of camera or free targeting.</summary>
    public static Vector3 Aim(VehiclePhysicsState pose, float range)
    {
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, pose.Orientation);
        forward.Y = 0;
        forward = forward.LengthSquared() > 0.0001f ? Vector3.Normalize(forward) : -Vector3.UnitZ;
        return pose.Position + forward * range;
    }

    /// <summary>Projects the host's current forward aim without accepting a nearby or displaced target.</summary>
    internal static Vector3? ProjectTarget(VehiclePhysicsState pose, float range, Func<Vector3, Vector3?>? ground)
    {
        Vector3 aim = Aim(pose, range);
        return ground?.Invoke(aim) is Vector3 target && VehiclePhysicsState.IsFinite(target) &&
            Math.Abs(target.X - aim.X) <= 0.01f && Math.Abs(target.Z - aim.Z) <= 0.01f && Math.Abs(target.Y - aim.Y) <= 200
            ? target : null;
    }
}
