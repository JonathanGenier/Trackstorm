namespace Trackstorm.Core.Items;

/// <summary>Canonical roster in stable distribution order; it owns definitions, never inventory.</summary>
public static class ItemRegistry
{
    /// <summary>All real items, excluding the empty-slot sentinel.</summary>
    public static IReadOnlyList<ItemDefinition> All { get; } = Array.AsReadOnly<ItemDefinition>(
    [
        new(HeldItem.Wrench, "wrench", "Wrench", 1, "Wrench", "WrenchPickup", "WrenchUse", null) { Handler = new WrenchUseHandler(), UseVfx = "spark_01" },
        new(HeldItem.Missile, "missile", "Missile", 1, "Missile", "WeaponPickup", "MissileFire", "MissileImpact") { Handler = new MissileUseHandler(), UseVfx = "spark_01", ImpactVfx = "fire_01" },
        new(HeldItem.Oil, "oil", "Oil", 1, "Oil", "WeaponPickup", null, null) { Handler = new OilUseHandler() },
        new(HeldItem.Nitro, "nitro", "Nitro", 1, "Nitro", "WeaponPickup", null, null),
    ]);

    /// <summary>Returns a registered definition, or null for empty/unknown wire identities.</summary>
    /// <param name="identity">Inventory identity.</param>
    /// <returns>Immutable definition.</returns>
    public static ItemDefinition? Find(HeldItem identity) => All.FirstOrDefault(item => item.Identity == identity);
}
