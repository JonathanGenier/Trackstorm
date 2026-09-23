namespace Trackstorm.Core.Vehicles;

/// <summary>Portable temporary boost, captured at activation and advanced only by simulation ticks.</summary>
public readonly record struct NitroState(int RemainingTicks, float AccelerationMultiplier, float SpeedMultiplier)
{
    /// <summary>Whether this movement decision uses the boost.</summary>
    public bool Active => RemainingTicks > 0;

    /// <summary>Rejects malformed or noncanonical expired state.</summary>
    public void Validate()
    {
        if (RemainingTicks is < 0 or > 3600 ||
            (RemainingTicks == 0 ? AccelerationMultiplier != 0 || SpeedMultiplier != 0 :
                !float.IsFinite(AccelerationMultiplier) || AccelerationMultiplier is < 1 or > 4 ||
                !float.IsFinite(SpeedMultiplier) || SpeedMultiplier is < 1 or > 2))
        {
            throw new ArgumentException("Invalid Nitro continuation.");
        }
    }

    /// <summary>Advances duration without extending it during replay or recovery.</summary>
    public NitroState Advance() => RemainingTicks > 1 ? this with { RemainingTicks = RemainingTicks - 1 } : default;
}
