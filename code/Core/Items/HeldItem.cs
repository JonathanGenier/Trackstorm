namespace Trackstorm.Core.Items;

/// <summary>Stable wire identities; None represents an empty slot.</summary>
public enum HeldItem : byte
{
    /// <summary>Empty slot.</summary>
    None = 0,
    /// <summary>Single-use repair.</summary>
    Wrench = 1,
    /// <summary>Straight explosive projectile.</summary>
    Missile = 2,
    /// <summary>Oil deployable identity.</summary>
    Oil = 3,
    /// <summary>Temporary boost identity.</summary>
    Nitro = 4,
    /// <summary>Magnetic contact deployable.</summary>
    ProxyMine = 5,
    /// <summary>Fixed-range arcing missile salvo.</summary>
    Salvo = 6,
    /// <summary>Close-range sustained ballistic weapon.</summary>
    MachineGun = 7,
}
