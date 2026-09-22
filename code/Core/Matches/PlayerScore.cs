namespace Trackstorm.Core.Matches;

/// <summary>Per-match totals and a life watermark that prevents duplicate death scoring after restoration.</summary>
/// <param name="Player">Stable player/vehicle identity.</param>
/// <param name="Kills">Credited kills.</param>
/// <param name="Deaths">Scored deaths.</param>
/// <param name="Wins">Zero or one win in this match.</param>
/// <param name="ProcessedLife">Highest destroyed life consumed, including deaths outside Active.</param>
public readonly record struct PlayerScore(ulong Player, int Kills, int Deaths, int Wins, ulong ProcessedLife)
{
    /// <summary>Independent unbanked stunt events; null when idle.</summary>
    public StuntState? Stunts { get; init; }
    /// <summary>Current potential stunt award at the shared K/D; never part of permanent CircusScore.</summary>
    public double PendingStuntScore => Stunts is { } pending ? (pending.Drift.BasePoints + pending.Airtime.BasePoints + pending.LongJumpBasePoints + pending.TopSpeed.BasePoints) * KdMultiplier : 0;
    /// <summary>Permanent Circus points; death never subtracts banked awards.</summary>
    public double CircusScore { get; init; }
    /// <summary>Consecutive credited kills since the last scored death.</summary>
    public int KillStreak { get; init; }
    /// <summary>Life containing the latest consumed applied damage outcome.</summary>
    public ulong ProcessedDamageLife { get; init; }
    /// <summary>Highest applied damage sequence consumed in that life, including outside Active.</summary>
    public ulong ProcessedDamageSequence { get; init; }
    /// <summary>Shared authoritative multiplier for every Circus scoring source.</summary>
    public double KdMultiplier => Math.Max(1d, Kills / ((double)Deaths + 1));
}
