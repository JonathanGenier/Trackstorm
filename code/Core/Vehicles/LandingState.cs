namespace Trackstorm.Core.Vehicles;

/// <summary>Bounded landing continuation retained across authority restoration.</summary>
public readonly record struct LandingState(LandingPhase Phase, byte UnsupportedTicks, byte RecoveryTicks, byte StableTicks)
{
    /// <summary>Rejects malformed episode memory at serialization and authority boundaries.</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(Phase) || UnsupportedTicks > 3 || RecoveryTicks > 60 || StableTicks > 6 ||
            ((Phase is LandingPhase.Recovery or LandingPhase.Recovered) != (RecoveryTicks > 0)))
        {
            throw new ArgumentException("Invalid landing episode.");
        }
    }
}
