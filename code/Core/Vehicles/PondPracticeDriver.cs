using System.Numerics;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Input;

namespace Trackstorm.Core.Vehicles;

/// <summary>Car 4 alone uses ordinary pedals/steering for one pond pickup trip per life.</summary>
internal sealed class PondPracticeDriver
{
    private readonly Vector3 _pickup;
    private readonly Vector3 _forward;

    internal PondPracticeDriver(ulong vehicle, ArenaSpawn marker, PondPracticeState? restored)
    {
        var heading = Quaternion.CreateFromAxisAngle(Vector3.UnitY, marker.Yaw);
        _forward = Vector3.Transform(-Vector3.UnitZ, heading);
        _pickup = marker.Position + Vector3.UnitY * VehicleDimensions.RideHeight;
        // Initial suspension clearance at the slightly uphill inspection position;
        // ordinary native support determines the settled height during the trip.
        Home = new(_pickup - _forward * 6.096f + Vector3.UnitY * .3f, heading, Vector3.Zero, Vector3.Zero);
        State = restored ?? new(vehicle, 0, PondPracticePhase.Approach);
    }

    internal VehiclePhysicsState Home { get; }
    internal PondPracticeState State { get; private set; }

    internal (InputFrame Input, VehiclePhysicsState? Anchor) Capture(VehicleSnapshot vehicle,
        VehicleConfiguration configuration, ulong tick, bool hasItem, bool full)
    {
        var neutral = new InputFrame(tick, 0, 0, 0, 0, 0, 0);
        if (State.Life != vehicle.LifeId)
            State = new(vehicle.VehicleId, vehicle.LifeId, hasItem ? PondPracticePhase.Returning : PondPracticePhase.Approach, hasItem);
        if (!vehicle.CanInteract) { return (neutral, null); }
        if (hasItem) { State = State with { Acquired = true }; }
        if (State.Phase == PondPracticePhase.Waiting && State.Acquired ||
            State.Phase == PondPracticePhase.Approach && full)
            State = State with { Phase = PondPracticePhase.Returning };
        var pose = vehicle.Movement.Physics;
        var stopped = new VehiclePhysicsState(pose.Position, pose.Orientation, Vector3.Zero, Vector3.Zero);
        if (State.Phase is PondPracticePhase.Waiting or PondPracticePhase.Parked) { return (neutral, stopped); }

        bool reverse = State.Phase == PondPracticePhase.Returning;
        Vector3 delta = (reverse ? Home.Position : _pickup) - pose.Position;
        delta.Y = 0;
        float remaining = Vector3.Dot(delta, reverse ? -_forward : _forward);
        float speed = Vector3.Dot(pose.LinearVelocity, _forward);
        if (remaining <= .10f)
        {
            if (!vehicle.Movement.Grounded || new Vector2(pose.LinearVelocity.X, pose.LinearVelocity.Z).Length() > .18f)
                return (new(tick, 0, 0, 0, InputButtons.Drift, 0, 0), null);
            State = State with { Phase = reverse ? PondPracticePhase.Parked : State.Acquired ? PondPracticePhase.Returning : PondPracticePhase.Waiting };
            return (neutral, stopped);
        }

        Vector3 heading = Vector3.Transform(-Vector3.UnitZ, pose.Orientation);
        // Follow the same short straight in both directions. Reverse driving needs
        // opposite cross-track correction while keeping the nose toward the crate.
        Vector3 lateral = pose.Position - Home.Position;
        lateral.Y = 0;
        lateral -= _forward * Vector3.Dot(lateral, _forward);
        Vector3 direction = _forward + lateral * (reverse ? .65f : -.65f);
        float angle = MathF.Atan2(Vector3.Cross(heading, direction).Y, Vector3.Dot(heading, direction));
        float wheel = -(angle * 1.7f - pose.AngularVelocity.Y * .4f);
        short steering = (short)(Math.Clamp((reverse ? -wheel : wheel) / configuration.SteeringLimit(vehicle.Speed), -1, 1) * short.MaxValue);
        float desired = Math.Min(2, MathF.Sqrt(2 * Math.Max(.05f, remaining))) * (reverse ? -1 : 1);
        float demand = Math.Clamp((desired - speed) * .6f + MathF.CopySign(.12f, desired), -1, 1);
        return (new(tick, steering, (ushort)(Math.Max(0, demand) * ushort.MaxValue),
            (ushort)(Math.Max(0, -demand) * ushort.MaxValue), 0, 0, 0), null);
    }
}
