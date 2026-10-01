namespace Trackstorm.Core.Items;

/// <summary>Forward-only authoritative Tombstone lifecycle. Destroyed entities leave the live item set.</summary>
public enum TombstoneStage : byte
{
    /// <summary>Acquired and occupying a physical inventory slot.</summary>
    Held,
    /// <summary>Attached rear shield, still reserving the same slot.</summary>
    RearShield,
    /// <summary>Match-owned wall, independent of the former owner's life.</summary>
    WorldWall,
}
