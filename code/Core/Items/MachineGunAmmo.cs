namespace Trackstorm.Core.Items;

/// <summary>Discrete ammunition and fractional firing clock, owned by one physical held slot.</summary>
public sealed record MachineGunAmmo(int Remaining, int Capacity, double Phase = 0)
{
    /// <summary>Confirmed resource percentage; capacity is fixed at acquisition.</summary>
    public double Percentage => 100.0 * Remaining / Capacity;

    /// <summary>Rejects impossible continuation before installation.</summary>
    public void Validate()
    {
        if (Capacity is < 1 or > 10000 || Remaining < 1 || Remaining > Capacity ||
            !double.IsFinite(Phase) || Phase is < 0 or >= 1)
        { throw new ArgumentException("Invalid machine gun ammunition."); }
    }
}
