namespace Trackstorm.Core.Items;

/// <summary>Stable category tuning metadata in canonical selection order.</summary>
public sealed record ItemCategoryDefinition(ItemCategory Identity, string Key, int DefaultWeight);
