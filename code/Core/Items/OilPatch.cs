using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>One firing activation's logical surface hazard. All geometry shares its grant identity and quota.</summary>
public sealed record OilPatch(ulong Id, ulong Owner, Vector3 Position, Vector3 Normal, float Radius)
{
    /// <summary>Optional authoritative ballistic continuation; absent on legacy/recovery fixture puddles.</summary>
    public OilPour? Pour { get; init; }
    /// <summary>Absolute simulation cleanup deadline; retained through recovery.</summary>
    public ulong ExpiresAtTick { get; init; } = 1800;
    /// <summary>Optional irregular or elongated footprint pieces; they never become independent hazards.</summary>
    public OilFootprint AdditionalSegments { get; init; } = OilFootprint.Empty;
    /// <summary>Pass budget captured from tuning at deployment, including owner passes.</summary>
    public int PassLimit { get; init; } = 2;
    /// <summary>Successful entry passes already consumed; leaving does not refund them.</summary>
    public int PassesUsed { get; init; }
    /// <summary>Rejects invalid portable placement data.</summary>
    public void Validate()
    {
        Pour?.Validate();
        if (AdditionalSegments is null || ExpiresAtTick == 0 || PassLimit is < 1 or > 7 || PassesUsed < 0 || PassesUsed >= PassLimit ||
            Id == 0 || Owner == 0 || !VehiclePhysicsState.IsFinite(Position) ||
            !VehiclePhysicsState.IsFinite(Normal) || Math.Abs(Normal.LengthSquared() - 1) > 0.001f ||
            !float.IsFinite(Radius) || Radius is < 0.05f or > 5)
        {
            throw new ArgumentException("Invalid oil patch.");
        }
    }

    /// <summary>Tests individual supported tire locations against the complete footprint.</summary>
    /// <param name="observation">Host-observed vehicle support and pose.</param>
    /// <param name="configuration">The same suspension dimensions used by the native wheel rays.</param>
    /// <returns>The contacting tire mask, excluding flight and other road levels.</returns>
    public WheelMask ContactWheels(VehicleObservation observation, VehicleConfiguration configuration)
    {
        if (Pour is { Deposited: false }) { return WheelMask.None; }
        if (observation.Wheels is not WheelSupport wheels) { return WheelMask.None; }
        WheelMask result = WheelMask.None;
        var primary = new OilSegment(Position, Position, Normal, Radius);
        for (int index = 0; index < 4; index++)
        {
            Vector3? point;
            if (wheels.Contacts is WheelContactPoints points) { point = points[index]; }
            else
            {
                // Pure physics fixtures can supply spring compression without native objects.
                float compression = wheels.Compression[index];
                point = compression <= 0 ? null : observation.Physics.Position + Vector3.Transform(new Vector3(
                    (index % 2 == 0 ? -1 : 1) * VehicleDimensions.WheelTrack / 2,
                    -(configuration.SuspensionLength - compression),
                    (index < 2 ? -1 : 1) * configuration.Wheelbase / 2), observation.Physics.Orientation);
            }
            if (point is Vector3 supported && (primary.Contains(supported) || AdditionalSegments.Segments.Any(segment => segment.Contains(supported))))
            {
                result |= (WheelMask)(1 << index);
            }
        }
        return result;
    }
}
