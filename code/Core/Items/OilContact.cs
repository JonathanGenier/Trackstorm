namespace Trackstorm.Core.Items;

/// <summary>Entry latch scoped to one patch and vehicle life; retained by authority recovery.</summary>
public sealed record OilContact(ulong Patch, ulong Vehicle, ulong Life);
