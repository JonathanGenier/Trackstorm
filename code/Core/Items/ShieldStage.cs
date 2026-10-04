namespace Trackstorm.Core.Items;

/// <summary>Authoritative Shield attachment. Selection reversibly exposes/stows attached pools; destroyed entities leave the live set.</summary>
public enum ShieldStage : byte
{
    /// <summary>Stored in an unselected physical inventory slot.</summary>
    Held,
    /// <summary>Attached rear shield, still reserving the same slot.</summary>
    RearShield,
    /// <summary>Match-owned wall, independent of the former owner's life.</summary>
    WorldWall,
}
