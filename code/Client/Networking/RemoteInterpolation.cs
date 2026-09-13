using System.Numerics;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Networking;

/// <summary>Render-time sampling of ordered Core snapshot data, with endpoint holding and no extrapolation.</summary>
internal sealed class RemoteInterpolation
{
    /// <summary>Two snapshot intervals behind authority absorb ordinary jitter at 20 Hz.</summary>
    internal const double DelayTicks = 6;
    private bool _initialized;
    /// <summary>Monotonic presentation cursor in host ticks.</summary>
    internal double RenderTick { get; private set; }
    /// <summary>Actual buffered delay behind the most recently received host tick.</summary>
    internal double DelayMilliseconds { get; private set; }

    /// <summary>Samples a remote pose, clamping before/after known data and never blending distinct lives.</summary>
    /// <param name="history">Ordered immutable snapshots.</param>
    /// <param name="vehicleId">Remote gameplay identity.</param>
    /// <param name="tick">Presentation cursor.</param>
    /// <returns>A presentation-only physics pose, or null if the vehicle has no retained sample.</returns>
    internal static VehiclePhysicsState? Sample(SnapshotHistory history, ulong vehicleId, double tick)
    {
        if (!double.IsFinite(tick))
        {
            throw new ArgumentOutOfRangeException(nameof(tick));
        }

        VehicleSnapshot? previous = null;
        foreach (WorldSnapshot world in history.Snapshots)
        {
            VehicleSnapshot? current = world.Vehicles.FirstOrDefault(vehicle => vehicle.State.VehicleId == vehicleId)?.State;
            if (current is null)
            {
                continue;
            }

            if (current.Movement.Tick >= tick)
            {
                if (previous is null || current.Movement.Tick == tick)
                {
                    return current.ObservedPhysics;
                }

                if (current.LifeId != previous.LifeId)
                {
                    return previous.ObservedPhysics;
                }

                float weight = (float)((tick - previous.Movement.Tick) / (current.Movement.Tick - previous.Movement.Tick));
                VehiclePhysicsState from = previous.ObservedPhysics;
                VehiclePhysicsState to = current.ObservedPhysics;
                return new VehiclePhysicsState(Vector3.Lerp(from.Position, to.Position, weight), Quaternion.Normalize(Quaternion.Slerp(from.Orientation, to.Orientation, weight)), Vector3.Lerp(from.LinearVelocity, to.LinearVelocity, weight), Vector3.Lerp(from.AngularVelocity, to.AngularVelocity, weight));
            }

            previous = current;
        }

        return previous?.ObservedPhysics;
    }

    /// <summary>Advances a smooth clock without resetting it on each jittered packet arrival.</summary>
    /// <param name="history">Ordered snapshot data.</param>
    /// <param name="seconds">Render elapsed time.</param>
    /// <param name="snapshotAge">Elapsed local time since the latest accepted snapshot.</param>
    internal void Advance(SnapshotHistory history, double seconds, double snapshotAge)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || !double.IsFinite(snapshotAge) || snapshotAge < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        if (history.Snapshots.Count == 0)
        {
            return;
        }

        double latest = history.Snapshots[^1].Tick;
        double desired = latest - DelayTicks + (snapshotAge * HostVehicleSession.TickRate);
        if (!_initialized)
        {
            RenderTick = Math.Clamp(desired, 0, latest);
            _initialized = true;
        }
        else
        {
            double rate = Math.Clamp(1 + ((desired - RenderTick) * 0.05), 0.9, 1.1);
            RenderTick = Math.Min(latest, RenderTick + (seconds * HostVehicleSession.TickRate * rate));
        }

        DelayMilliseconds = Math.Max(0, latest - RenderTick) * 1000 / HostVehicleSession.TickRate;
    }

}
