using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Portable parabola committed by an individual shot, retained for checkpoint continuation.</summary>
public sealed record SalvoFlight(Vector3 Origin, Vector3 Target, float Height, int DurationTicks, int ElapsedTicks, ulong Life)
{
    /// <summary>Initial aimed reach is twice the existing configurable range;
    /// ordinary fire retains its prior range for every existing configuration.</summary>
    public static float AimRange(ItemConfiguration configuration, bool activelyAiming) => configuration.SalvoRange * (activelyAiming ? 2 : 1);

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
        return this with { Origin = origin, DurationTicks = Math.Max(1, (int)Math.Ceiling(length / speed * 60)) };
    }

    /// <summary>Rejects malformed continuation before restore or publication.</summary>
    public void Validate()
    {
        if (!VehiclePhysicsState.IsFinite(Origin) || !VehiclePhysicsState.IsFinite(Target) ||
            !float.IsFinite(Height) || Height is < 1 or > 60 || DurationTicks is < 1 or > 3600 ||
            ElapsedTicks < 0 || ElapsedTicks >= DurationTicks || Life == 0 || Vector3.Distance(Origin, Target) is < 1 or > 600)
        {
            throw new ArgumentException("Invalid salvo continuation.");
        }
    }

    /// <summary>Authored fully extended bowl at the 45-degree release stop, in the current vehicle pose.</summary>
    public static Vector3 LaunchOrigin(VehiclePhysicsState pose, float yaw, float height = 3)
    {
        Vector3 spoon = new Vector3(0, .5f, 0) + Vector3.Transform(new Vector3(0, .06f, 2.09f), Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 4));
        Vector3 local = new Vector3(0, 1.34f, 1.845f) + new Vector3(0, .4f + height - 3, -1.72f) + Vector3.Transform(spoon, Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw));
        return pose.Position + Vector3.Transform(local, pose.Orientation);
    }

    /// <summary>Shared guide/launch target from accepted camera direction. Missing ground never rejects a volley.</summary>
    public static Vector3 ResolveTarget(VehiclePhysicsState pose, Vector3 direction, float range,
        Func<Vector3, Vector3?>? ground, Func<Vector3, Vector3, WeaponRayHit?>? raycast = null)
    {
        Vector3 pivot = pose.Position + Vector3.Transform(WeaponAim.Pivot, pose.Orientation);
        Vector3 end = pivot + direction * range;
        if (raycast?.Invoke(pivot, end) is { Fraction: >= 0 and <= 1 } hit)
        { return Vector3.Lerp(pivot, end, Math.Max(2 / range, hit.Fraction)); }
        // Horizontal/downward free aim retains the existing terrain footprint.
        // Sky/out-of-arena aim has a finite independent endpoint even with no support.
        if (direction.Y <= .05f && ground?.Invoke(end) is Vector3 target && VehiclePhysicsState.IsFinite(target) &&
            Math.Abs(target.X - end.X) <= .01f && Math.Abs(target.Z - end.Z) <= .01f && Math.Abs(target.Y - end.Y) <= 200)
        { return target; }
        return end;
    }
}
