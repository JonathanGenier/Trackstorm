namespace Trackstorm.Core.Items;

/// <summary>One player's two fixed held slots and selection in one vehicle life. Each grant token is match-unique.</summary>
public sealed record ItemSlot(ulong Vehicle, ulong Life, ulong Token, HeldItem Item)
{
    /// <summary>Second physical slot's grant capability; zero before its first grant.</summary>
    public ulong SecondToken { get; init; }
    /// <summary>Second physical slot's held identity.</summary>
    public HeldItem SecondItem { get; init; }
    /// <summary>Selected physical slot, zero or one, including when empty.</summary>
    public byte ActiveSlot { get; init; }
    /// <summary>Monotonic selection command watermark within this life.</summary>
    public ulong SelectionRevision { get; init; }
    /// <summary>Capability for the currently selected slot, for existing item handlers.</summary>
    public ItemSlot Active => new(Vehicle, Life, ActiveSlot == 0 ? Token : SecondToken, ActiveSlot == 0 ? Item : SecondItem);
    /// <summary>Whether acquisition must leave both held items untouched.</summary>
    public bool Full => Item != HeldItem.None && SecondItem != HeldItem.None;
}
