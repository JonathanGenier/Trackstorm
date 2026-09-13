namespace Trackstorm.Core.Vehicles;

/// <summary>Unprocessed combat intent; health changes only when Simulation accepts its fixed step.</summary>
public sealed record VehicleEffectRequest
{
    /// <summary>Creates a validated generic combat request.</summary>
    /// <param name="effect">Damage and native impulse request.</param>
    /// <param name="attribution">Stable source metadata.</param>
    public VehicleEffectRequest(DamageEffect effect, DamageContext attribution)
    {
        ArgumentNullException.ThrowIfNull(attribution);
        Effect = effect;
        Attribution = attribution;
    }

    /// <summary>Generic effect payload.</summary>
    public DamageEffect Effect { get; }
    /// <summary>Attribution to retain in accepted damage outcomes.</summary>
    public DamageContext Attribution { get; }
}
