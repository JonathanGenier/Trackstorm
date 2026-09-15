namespace Trackstorm.Core.Vehicles;

/// <summary>Immutable host tuning; delay uses simulation ticks, never wall-clock time.</summary>
public sealed record RespawnConfiguration
{
    /// <summary>Three seconds at the production 60 Hz rate. Must be positive.</summary>
    public ulong DelayTicks { get; init; } = 180;
    /// <summary>Retire held inventory on death; false retains it with a fresh capability on respawn.</summary>
    public bool ClearHeldItemOnDeath { get; init; } = true;

    /// <summary>Rejects a zero delay before authority starts.</summary>
    public void Validate() => ArgumentOutOfRangeException.ThrowIfZero(DelayTicks);
}
