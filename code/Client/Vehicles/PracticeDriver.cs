using Godot;
using Trackstorm.Core.Input;

namespace Trackstorm.Client.Vehicles;

/// <summary>Local-practice input driver following the production oval with ordinary vehicle physics.</summary>
internal sealed class PracticeDriver(VehicleBody vehicle)
{
    private float _integral;
    private ulong _life;
    internal const float Speed = 50f / 3.6f;
    private const float Radius = 91;
    private const float Straight = 214;
    private static readonly Vector3[] Route = Enumerable.Range(0, 1000).Select(index => Point(index * (2 * Straight + Mathf.Tau * Radius) / 1000)).ToArray();

    internal InputFrame Capture(ulong tick)
    {
        if (_life != vehicle.Snapshot.LifeId) { _life = vehicle.Snapshot.LifeId; _integral = 0; }
        if (!vehicle.Snapshot.CanInteract) { return new(tick, 0, 0, 0, 0, 0, 0); }
        Vector3 position = vehicle.GlobalPosition;
        int nearest = 0;
        float distance = float.MaxValue;
        for (int index = 0; index < Route.Length; index++)
        {
            float candidate = new Vector2(position.X - Route[index].X, position.Z - Route[index].Z).LengthSquared();
            if (candidate < distance) { distance = candidate; nearest = index; }
        }
        Vector3 target = Route[(nearest + 18) % Route.Length] - position;
        Vector3 forward = -vehicle.GlobalBasis.Z;
        float angle = new Vector3(forward.X, 0, forward.Z).SignedAngleTo(new(target.X, 0, target.Z), Vector3.Up);
        float wheel = MathF.Atan(2 * vehicle.Configuration.Wheelbase * MathF.Sin(-angle) / Math.Max(1, new Vector2(target.X, target.Z).Length()));
        short steering = (short)(Math.Clamp(wheel / vehicle.Configuration.SteeringLimit(vehicle.Snapshot.Speed), -1, 1) * short.MaxValue);
        float error = Speed - vehicle.Snapshot.Speed;
        _integral = Math.Clamp(_integral + error / vehicle.Configuration.TicksPerSecond * .08f, 0, .7f);
        float demand = Math.Clamp(error * .3f + _integral, -1, 1);
        return new(tick, steering, (ushort)(Math.Max(0, demand) * ushort.MaxValue),
            (ushort)(Math.Max(0, -demand) * ushort.MaxValue), 0, 0, 0);
    }

    private static Vector3 Point(float distance)
    {
        if (distance < Straight) { return new(-107 + distance, 0, Radius); }
        distance -= Straight;
        if (distance < Mathf.Pi * Radius)
        {
            float angle = distance / Radius;
            return new(107 + Radius * Mathf.Sin(angle), 0, Radius * Mathf.Cos(angle));
        }
        distance -= Mathf.Pi * Radius;
        if (distance < Straight) { return new(107 - distance, 0, -Radius); }
        float turn = (distance - Straight) / Radius;
        return new(-107 - Radius * Mathf.Sin(turn), 0, -Radius * Mathf.Cos(turn));
    }
}
