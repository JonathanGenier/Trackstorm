namespace Trackstorm.Core.Items;

/// <summary>Authority-staged patch entry, consumed only with its successful vehicle batch.</summary>
internal sealed record OilTrigger(ulong Patch, ulong Owner, ulong Vehicle, ulong Life);
