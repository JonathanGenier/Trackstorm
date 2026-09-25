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
    /// <summary>Fixed-range arcing missile salvo. Identity five is reserved for Proxy Mine.</summary>
    Salvo = 6,
}
