namespace Trackstorm.Core.Vehicles;

/// <summary>Per-tire fixed-step contamination, including the full impairment and recovery phases.</summary>
public readonly record struct OilTractionState(int FrontLeft, int FrontRight, int RearLeft, int RearRight)
{
    /// <summary>Longest remaining contamination, for aggregate diagnostics only.</summary>
    public int RemainingTicks => Math.Max(Math.Max(FrontLeft, FrontRight), Math.Max(RearLeft, RearRight));

    /// <summary>Remaining ticks for one tire in suspension order.</summary>
    public int this[int index] => index switch
    {
        0 => FrontLeft, 1 => FrontRight, 2 => RearLeft, 3 => RearRight,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    /// <summary>Rejects invalid restored continuation.</summary>
    public void Validate()
    {
        for (int i = 0; i < 4; i++)
        {
            if (this[i] is < 0 or > 7200) { throw new ArgumentException("Invalid wheel Oil duration."); }
        }
    }

    internal OilTractionState Advance(WheelMask entries, VehicleConfiguration tuning)
    {
        if ((entries & ~WheelMask.All) != 0) { throw new ArgumentOutOfRangeException(nameof(entries)); }
        var previous = this;
        int duration = HoldTicks(tuning) + RecoveryTicks(tuning);
        int Next(int index) => (entries & (WheelMask)(1 << index)) != 0 ? duration : Math.Max(0, previous[index] - 1);
        return new(Next(0), Next(1), Next(2), Next(3));
    }

    /// <summary>Full contamination followed by continuous smoothstep-shaped recovery.</summary>
    public float Strength(int index, VehicleConfiguration tuning)
    {
        float remaining = Math.Clamp((float)this[index] / RecoveryTicks(tuning), 0, 1);
        float progress = MathF.Pow(1 - remaining, tuning.OilRecoveryCurve);
        return 1 - progress * progress * (3 - 2 * progress);
    }

    internal OilTractionState Retune(VehicleConfiguration previous, VehicleConfiguration next)
    {
        var state = this;
        int Scale(int index)
        {
            int ticks = state[index];
            return ticks > RecoveryTicks(previous)
                ? RecoveryTicks(next) + (int)MathF.Ceiling((ticks - RecoveryTicks(previous)) * (float)HoldTicks(next) / HoldTicks(previous))
                : (int)MathF.Ceiling(ticks * (float)RecoveryTicks(next) / RecoveryTicks(previous));
        }
        return new(Scale(0), Scale(1), Scale(2), Scale(3));
    }

    private static int HoldTicks(VehicleConfiguration c) => (int)MathF.Ceiling(c.OilImpairmentSeconds * c.TicksPerSecond);
    private static int RecoveryTicks(VehicleConfiguration c) => (int)MathF.Ceiling(c.OilRecoverySeconds * c.TicksPerSecond);
}
