namespace Trackstorm.Core.Items;

/// <summary>Current successful overlap latch. A new pass requires leaving or starting a new life.</summary>
public sealed record OilContact(ulong Patch, ulong Vehicle, ulong Life);
