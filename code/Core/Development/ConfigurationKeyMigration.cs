namespace Trackstorm.Core.Development;

/// <summary>Read-only aliases for historical host seed files and exported tuning; live catalogs and packets use canonical keys.</summary>
internal static class ConfigurationKeyMigration
{
    internal static string CanonicalKey(string key) => key switch
    {
        "items.tombstone_width" => "items.shield_width",
        "items.tombstone_height" => "items.shield_height",
        "items.tombstone_depth" => "items.shield_depth",
        "items.tombstone_mass" => "items.shield_mass",
        "items.tombstone_clearance" => "items.shield_clearance",
        "items.tombstone_lifetime" => "items.shield_lifetime",
        "items.tombstone_tip_speed" => "items.shield_tip_speed",
        "spawns.tombstone_weight" => "spawns.shield_weight",
        _ => key,
    };

    internal static string CanonicalGroup(string group) => group == "Tombstone" ? "Shield" : group;
}
