namespace Trackstorm.Core.Items;

/// <summary>Patch-lifetime enemy history: stable vehicle identity counts once across all lives.</summary>
public sealed record OilContact(ulong Patch, ulong Vehicle, ulong Life);
