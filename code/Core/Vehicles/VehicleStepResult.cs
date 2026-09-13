namespace Trackstorm.Core.Vehicles;

/// <summary>Accepted Core snapshot and one-shot native/presentation outputs for the same tick.</summary>
public sealed class VehicleStepResult
{
    /// <summary>Builds an immutable accepted result inside Core.</summary>
    /// <param name="snapshot">Next authoritative aggregate.</param>
    /// <param name="effects">Accepted effects.</param>
    /// <param name="damageEvents">Positive damage outcomes.</param>
    /// <param name="reset">Whether a new life begins.</param>
    internal VehicleStepResult(VehicleSnapshot snapshot, IReadOnlyList<VehicleEffectRequest> effects, List<DamageEvent> damageEvents, bool reset)
    {
        Snapshot = snapshot;
        Effects = effects;
        DamageEvents = damageEvents.AsReadOnly();
        Reset = reset;
    }

    /// <summary>Committed authoritative state, including movement commands.</summary>
    public VehicleSnapshot Snapshot { get; }
    /// <summary>Accepted impulses applied by the native adapter after movement commands.</summary>
    public IReadOnlyList<VehicleEffectRequest> Effects { get; }
    /// <summary>All positive damage outcomes, including the unique death candidate.</summary>
    public IReadOnlyList<DamageEvent> DamageEvents { get; }
    /// <summary>Whether native pose/velocities and presentation must start a new life.</summary>
    public bool Reset { get; }
}
