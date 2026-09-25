namespace Trackstorm.Core.Items;

/// <summary>Canonical roster in stable distribution order; it owns definitions, never inventory.</summary>
public static class ItemRegistry
{
    /// <summary>Category roster in deterministic order; weights need not sum to a percentage.</summary>
    public static IReadOnlyList<ItemCategoryDefinition> Categories { get; } = Array.AsReadOnly<ItemCategoryDefinition>(
    [
        new(ItemCategory.Weapon, "weapon", 2),
        new(ItemCategory.Consumable, "consumable", 1),
        new(ItemCategory.Droppable, "droppable", 1),
    ]);
    /// <summary>All real items, excluding the empty-slot sentinel.</summary>
    public static IReadOnlyList<ItemDefinition> All { get; } = Array.AsReadOnly<ItemDefinition>(
    [
        new(HeldItem.Wrench, "wrench", "Wrench", 1, "Wrench", "WrenchPickup", "WrenchUse", null) { Category = ItemCategory.Consumable, Handler = new WrenchUseHandler(), UseVfx = "spark_01" },
        new(HeldItem.Missile, "missile", "Missile", 1, "Missile", "WeaponPickup", "MissileFire", "MissileImpact") { Category = ItemCategory.Weapon, Handler = new MissileUseHandler(), UseVfx = "spark_01", ImpactVfx = "fire_01" },
        new(HeldItem.Oil, "oil", "Oil", 1, "Oil", "WeaponPickup", null, null) { Category = ItemCategory.Droppable, Handler = new OilUseHandler() },
        new(HeldItem.Nitro, "nitro", "Nitro", 1, "Nitro", "WeaponPickup", "WrenchUse", null) { Category = ItemCategory.Consumable, Handler = new NitroUseHandler(), UseVfx = "spark_01", ActiveVfx = "fire_01" },
        new(HeldItem.ProxyMine, "proxy_mine", "Proxy Mine", 1, "ProxyMine", "WeaponPickup", null, "MissileImpact") { Category = ItemCategory.Droppable, Handler = new ProxyMineUseHandler(), ImpactVfx = "fire_01" },
        new(HeldItem.Salvo, "salvo", "Salvo", 1, "Salvo", "WeaponPickup", "MissileFire", "MissileImpact") { Category = ItemCategory.Weapon, Handler = new SalvoUseHandler(), UseVfx = "spark_01", ImpactVfx = "fire_01" },
    ]);

    /// <summary>Returns a registered definition, or null for empty/unknown wire identities.</summary>
    /// <param name="identity">Inventory identity.</param>
    /// <returns>Immutable definition.</returns>
    public static ItemDefinition? Find(HeldItem identity) => All.FirstOrDefault(item => item.Identity == identity);
}
