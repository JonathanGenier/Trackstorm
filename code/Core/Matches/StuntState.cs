using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Matches;

/// <summary>Independent pending categories in one vehicle life; complete checkpoint memory, never an award log.</summary>
public sealed record StuntState
{
    /// <summary>Vehicle life owning these events.</summary>
    public ulong Life { get; init; }
    /// <summary>Last fixed boundary evaluated; prevents stale continuation.</summary>
    public ulong Tick { get; init; }
    /// <summary>Pending supported physical drift.</summary>
    public StuntProgress Drift { get; init; }
    /// <summary>Pending airborne duration.</summary>
    public StuntProgress Airtime { get; init; }
    /// <summary>Pending sustained top-speed travel.</summary>
    public StuntProgress TopSpeed { get; init; }
    /// <summary>Last supported position before this jump, in world metres.</summary>
    public Vector3 JumpOrigin { get; init; }
    /// <summary>Current horizontal displacement from takeoff, not integrated path length.</summary>
    public double JumpDistance { get; init; }
    /// <summary>Independent pending Long Jump base points.</summary>
    public double LongJumpBasePoints { get; init; }
    /// <summary>Whether any event is in progress.</summary>
    public bool Active => Drift.Ticks != 0 || Airtime.Ticks != 0 || TopSpeed.Ticks != 0;

    internal bool IsValid(ulong tick) => Life != 0 && Tick == tick && Active &&
        Drift.IsValid(tick) && Airtime.IsValid(tick) && TopSpeed.IsValid(tick) && VehiclePhysicsState.IsFinite(JumpOrigin) &&
        double.IsFinite(JumpDistance) && JumpDistance >= 0 && double.IsFinite(LongJumpBasePoints) && LongJumpBasePoints >= 0 &&
        (Airtime.Ticks != 0 || (JumpOrigin == Vector3.Zero && JumpDistance == 0 && LongJumpBasePoints == 0));
}
