namespace Trackstorm.Core.Items;

/// <summary>The only two inventory items; None represents an empty slot.</summary>
public enum HeldItem : byte
{
    /// <summary>Empty slot.</summary>
    None,
    /// <summary>Single-use repair.</summary>
    Wrench,
    /// <summary>Straight explosive projectile.</summary>
    Missile,
}
