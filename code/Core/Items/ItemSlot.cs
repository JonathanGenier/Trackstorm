namespace Trackstorm.Core.Items;

/// <summary>One player's two fixed held slots and selection in one vehicle life. Each grant token is match-unique.</summary>
public sealed record ItemSlot(ulong Vehicle, ulong Life, ulong Token, HeldItem Item)
{
    /// <summary>Unfired Salvo rounds in the first physical slot.</summary>
    public int SalvoShots { get; init; } = Item == HeldItem.Salvo ? 5 : 0;
    /// <summary>Unfired Salvo rounds in the second physical slot.</summary>
    public int SecondSalvoShots { get; init; }
    /// <summary>Earliest authoritative tick accepting another shot from the first slot.</summary>
    public ulong SalvoReadyTick { get; init; }
    /// <summary>Earliest authoritative tick accepting another shot from the second slot.</summary>
    public ulong SecondSalvoReadyTick { get; init; }
    /// <summary>Discrete resource in the first physical slot, absent for other items.</summary>
    public MachineGunAmmo? Ammo { get; init; }
    /// <summary>Discrete resource in the second physical slot.</summary>
    public MachineGunAmmo? SecondAmmo { get; init; }
    /// <summary>Generic percentage projection for the HUD.</summary>
    public double ResourcePercentage => Ammo?.Percentage ?? NitroCharge;
    /// <summary>Generic second-slot percentage projection.</summary>
    public double SecondResourcePercentage => SecondAmmo?.Percentage ?? SecondNitroCharge;
    /// <summary>Remaining percentage in the first physical slot; zero for other items.</summary>
    public double NitroCharge { get; init; } = Item == HeldItem.Nitro ? 100 : 0;
    /// <summary>Remaining percentage in the second physical slot.</summary>
    public double SecondNitroCharge { get; init; }
    /// <summary>Exact capability engaged by an accepted use, retained only while held.</summary>
    public ulong EngagedToken { get; init; }
    /// <summary>Second physical slot's grant capability; zero before its first grant.</summary>
    public ulong SecondToken { get; init; }
    /// <summary>Second physical slot's held identity.</summary>
    public HeldItem SecondItem { get; init; }
    /// <summary>Selected physical slot, zero or one, including when empty.</summary>
    public byte ActiveSlot { get; init; }
    /// <summary>Monotonic selection command watermark within this life.</summary>
    public ulong SelectionRevision { get; init; }
    /// <summary>Capability for the currently selected slot, for existing item handlers.</summary>
    public ItemSlot Active => new(Vehicle, Life, ActiveSlot == 0 ? Token : SecondToken, ActiveSlot == 0 ? Item : SecondItem)
    { NitroCharge = ActiveSlot == 0 ? NitroCharge : SecondNitroCharge, Ammo = ActiveSlot == 0 ? Ammo : SecondAmmo,
        SalvoShots = ActiveSlot == 0 ? SalvoShots : SecondSalvoShots,
        SalvoReadyTick = ActiveSlot == 0 ? SalvoReadyTick : SecondSalvoReadyTick };
    /// <summary>Whether acquisition must leave both held items untouched.</summary>
    public bool Full => Item != HeldItem.None && SecondItem != HeldItem.None;
}
