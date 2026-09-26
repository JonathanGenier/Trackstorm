using System.Numerics;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Networking;

/// <summary>Render-time sampling of ordered Core snapshot data, with endpoint holding and no extrapolation.</summary>
internal sealed class RemoteInterpolation
{
    /// <summary>One publication interval on a stable stream; measured arrival variation adds bounded headroom.</summary>
    internal const double DelayTicks = HostVehicleSession.SnapshotInterval;
    private bool _initialized;
    private ulong _latestTick;
    private double _arrivalSeconds;
    private double _jitterTicks;
    /// <summary>Monotonic presentation cursor in host ticks.</summary>
    internal double RenderTick { get; private set; }
    /// <summary>Actual buffered delay behind the most recently received host tick.</summary>
    internal double DelayMilliseconds { get; private set; }
    /// <summary>Delay relative to the locally advancing received timeline; excludes unknown one-way transit.</summary>
    internal double TimelineDelayMilliseconds { get; private set; }
    /// <summary>Explicit cursor recoveries after a stall exhausts the presentation buffer.</summary>
    internal int BufferRecoveries { get; private set; }

    /// <summary>Samples a remote pose, clamping before/after known data and never blending distinct lives.</summary>
    /// <param name="history">Ordered immutable snapshots.</param>
    /// <param name="vehicleId">Remote gameplay identity.</param>
    /// <param name="tick">Presentation cursor.</param>
    /// <returns>A presentation-only physics pose, or null if the vehicle has no retained sample.</returns>
    /// <param name="lifeId">Optional current visible life; excludes stale poses after a reliable respawn.</param>
    internal static VehiclePhysicsState? Sample(SnapshotHistory history, ulong vehicleId, double tick, ulong? lifeId = null)
    {
        if (!double.IsFinite(tick))
        {
            throw new ArgumentOutOfRangeException(nameof(tick));
        }

        VehicleSnapshot? previous = null;
        foreach (WorldSnapshot world in history.Snapshots)
        {
            VehicleSnapshot? current = world.Vehicles.FirstOrDefault(vehicle => vehicle.State.VehicleId == vehicleId)?.State;
            if (current is null || (lifeId.HasValue && current.LifeId != lifeId.Value))
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

    /// <summary>Discards the presentation cursor at a fresh authoritative resume boundary.</summary>
    internal void Reset()
    {
        _initialized = false;
        RenderTick = 0;
        DelayMilliseconds = 0;
        TimelineDelayMilliseconds = 0;
        _latestTick = 0;
        _arrivalSeconds = 0;
        _jitterTicks = 0;
        BufferRecoveries = 0;
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
        _arrivalSeconds += seconds;
        _jitterTicks = Math.Max(0, _jitterTicks - seconds * 0.25);
        if (_initialized && history.Snapshots[^1].Tick != _latestTick)
        {
            double variation = Math.Abs((_arrivalSeconds * HostVehicleSession.TickRate) - (latest - _latestTick));
            _jitterTicks = Math.Clamp(Math.Max(_jitterTicks, variation), 0, DelayTicks);
            _arrivalSeconds = 0;
        }

        _latestTick = history.Snapshots[^1].Tick;
        double targetDelay = DelayTicks + _jitterTicks;
        double desired = latest - targetDelay + (snapshotAge * HostVehicleSession.TickRate);
        if (!_initialized)
        {
            RenderTick = Math.Clamp(desired, history.Snapshots[0].Tick, latest);
            _initialized = true;
            _arrivalSeconds = 0;
        }
        else
        {
            double rate = Math.Clamp(1 + ((desired - RenderTick) * 0.05), 0.9, 1.1);
            RenderTick = Math.Min(latest, RenderTick + (seconds * HostVehicleSession.TickRate * rate));
            // A slow rate correction cannot recover seconds of scheduling debt. Resume
            // within retained data after a stall; ordinary jitter still uses the smooth clock.
            double oldestUseful = Math.Max(history.Snapshots[0].Tick, latest - targetDelay - DelayTicks);
            if (RenderTick < oldestUseful)
            {
                RenderTick = Math.Max(oldestUseful, latest - targetDelay);
                BufferRecoveries++;
            }
        }

        DelayMilliseconds = Math.Max(0, latest - RenderTick) * 1000 / HostVehicleSession.TickRate;
        TimelineDelayMilliseconds = DelayMilliseconds + snapshotAge * 1000;
    }

}
