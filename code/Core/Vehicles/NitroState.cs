namespace Trackstorm.Core.Vehicles;

/// <summary>Portable held-boost prediction budget and inactive momentum-recovery continuation.</summary>
public readonly record struct NitroState(int RemainingTicks, float ForwardThrust, float SpeedMultiplier, float AirborneThrustScale)
{
    /// <summary>Whether this movement decision uses the boost.</summary>
    public bool Active => RemainingTicks > 0;

    /// <summary>Momentum recovery remains after held boost ends, without acceleration or an elevated cap.</summary>
    public bool Recovering => RemainingTicks == 0 && ForwardThrust == 0 && SpeedMultiplier == 1 && AirborneThrustScale == 0;
    /// <summary>Canonical inactive recovery continuation.</summary>
    public static NitroState Recovery => new(0, 0, 1, 0);

    /// <summary>Rejects malformed or noncanonical expired state.</summary>
    public void Validate()
    {
        if (RemainingTicks is < 0 or > 3600 ||
            (RemainingTicks == 0 ? !Recovering && (ForwardThrust != 0 || SpeedMultiplier != 0 || AirborneThrustScale != 0) :
                !float.IsFinite(ForwardThrust) || ForwardThrust is < 0 or > 100000 ||
                !float.IsFinite(SpeedMultiplier) || SpeedMultiplier is < 1 or > 2 ||
                !float.IsFinite(AirborneThrustScale) || AirborneThrustScale is < 0 or > 1))
        {
            throw new ArgumentException("Invalid Nitro continuation.");
        }
    }

    /// <summary>Bounds predicted held use by the latest authoritative available-charge budget.</summary>
    public NitroState Advance() => RemainingTicks > 1 ? this with { RemainingTicks = RemainingTicks - 1 } : Active || Recovering ? Recovery : default;
}
