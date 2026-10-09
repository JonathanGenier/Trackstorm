using System.Numerics;
using Trackstorm.Core.Input;

namespace Trackstorm.Core.Vehicles;

/// <summary>Host-owned practice driving intent using ordinary steering, throttle and brakes.</summary>
/// <param name="oval">Production oval route; false uses the smaller prototype perimeter.</param>
public sealed class PracticeVehicleInput(bool oval = true)
{
    private float _integral;
    private ulong _life;
    /// <summary>Nominal cruise speed in metres per second.</summary>
    public const float Speed = 50f / 3.6f;
    private const float Radius = 91;
    private const float Straight = 214;
    private readonly Vector3[] _route = Enumerable.Range(0, 1000).Select(index => oval ? Point(index * (2 * Straight + MathF.Tau * Radius) / 1000) : new Vector3(28 * MathF.Sin(index * MathF.Tau / 1000), 0, 28 * MathF.Cos(index * MathF.Tau / 1000))).ToArray();

    /// <summary>Produces ordinary driving input; inactive lives receive neutral input.</summary>
    /// <param name="state">Current vehicle boundary.</param>
    /// <param name="configuration">Current host handling configuration.</param>
    /// <param name="tick">Next fixed tick.</param>
    /// <returns>Driving input only.</returns>
    public InputFrame Capture(VehicleSnapshot state, VehicleConfiguration configuration, ulong tick)
    {
        if (_life != state.LifeId) { _life = state.LifeId; _integral = 0; }
        if (!state.CanInteract) { return new(tick, 0, 0, 0, 0, 0, 0); }
        Vector3 position = state.Movement.Physics.Position;
        int nearest = 0;
        float distance = float.MaxValue;
        for (int index = 0; index < _route.Length; index++)
        {
            float candidate = new Vector2(position.X - _route[index].X, position.Z - _route[index].Z).LengthSquared();
            if (candidate < distance) { distance = candidate; nearest = index; }
        }
        Vector3 target = _route[(nearest + (oval ? 18 : 102)) % _route.Length] - position;
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, state.Movement.Physics.Orientation);
        float angle = MathF.Atan2(Vector3.Cross(forward, new Vector3(target.X, 0, target.Z)).Y, forward.X * target.X + forward.Z * target.Z);
        float wheel = MathF.Atan(2 * configuration.Wheelbase * MathF.Sin(-angle) / Math.Max(1, new Vector2(target.X, target.Z).Length()));
        short steering = (short)(Math.Clamp(wheel / configuration.SteeringLimit(state.Speed), -1, 1) * short.MaxValue);
        float error = Speed - state.Speed;
        _integral = Math.Clamp(_integral + error / configuration.TicksPerSecond * .08f, 0, .7f);
        float demand = Math.Clamp(error * .3f + _integral, -1, 1);
        return new(tick, steering, (ushort)(Math.Max(0, demand) * ushort.MaxValue),
            (ushort)(Math.Max(0, -demand) * ushort.MaxValue), 0, 0, 0);
    }

    private static Vector3 Point(float distance)
    {
        if (distance < Straight) { return new(-107 + distance, 0, Radius); }
        distance -= Straight;
        if (distance < MathF.PI * Radius)
        {
            float angle = distance / Radius;
            return new(107 + Radius * MathF.Sin(angle), 0, Radius * MathF.Cos(angle));
        }
        distance -= MathF.PI * Radius;
        if (distance < Straight) { return new(107 - distance, 0, -Radius); }
        float turn = (distance - Straight) / Radius;
        return new(-107 - Radius * MathF.Sin(turn), 0, -Radius * MathF.Cos(turn));
    }
}
