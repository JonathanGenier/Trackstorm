namespace Trackstorm.Core.Items;

/// <summary>A host-issued capability for one item in one vehicle life. Tokens never repeat in a match.</summary>
public sealed record ItemSlot(ulong Vehicle, ulong Life, ulong Token, HeldItem Item);
