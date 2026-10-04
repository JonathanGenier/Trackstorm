using System.Globalization;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Hud;

/// <summary>Detached physical-slot presentation. Resource adapters never own or consume inventory.</summary>
internal sealed record ItemHudSlotView(HeldItem Item, string Name, string? IconKey, ItemHudResource? Resource)
{
    internal static ItemHudSlotView Empty { get; } = new(HeldItem.None, "EMPTY", null, null);
    internal string Description => Resource is null ? Name : $"{Name} {Resource.Text}";

    /// <summary>One extension point for existing resource semantics; layout is independent of item identity.</summary>
    internal static ItemHudSlotView From(ItemSlot? inventory, bool second, IReadOnlyList<TombstoneState>? tombstones = null)
    {
        if (inventory is null) return Empty;
        HeldItem item = second ? inventory.SecondItem : inventory.Item;
        ItemDefinition? definition = ItemRegistry.Find(item);
        if (definition is null) return Empty;
        ItemHudResource? resource = item switch
        {
            HeldItem.Nitro => Percentage(second ? inventory.SecondNitroCharge : inventory.NitroCharge),
            HeldItem.MachineGun => Percentage((second ? inventory.SecondAmmo : inventory.Ammo)?.Percentage ?? 0),
            HeldItem.Tombstone => Durability(inventory, second, tombstones),
            // The authoritative Salvo boundary has a remaining count, not its captured starting capacity.
            HeldItem.Salvo => new((second ? inventory.SecondSalvoShots : inventory.SalvoShots).ToString(CultureInfo.InvariantCulture), null),
            _ => null,
        };
        return new(item, definition.DisplayName.ToUpperInvariant(), definition.PresentationKey, resource);
    }

    private static ItemHudResource? Durability(ItemSlot inventory, bool second, IReadOnlyList<TombstoneState>? tombstones)
    {
        ulong token = second ? inventory.SecondToken : inventory.Token;
        TombstoneState? stone = tombstones?.FirstOrDefault(state => state.Attached && state.Owner == inventory.Vehicle &&
            state.Life == inventory.Life && token != 0 && state.Token == token);
        // Missing or retired ownership must never borrow vehicle HP or another pool.
        if (stone is null || !float.IsFinite(stone.HP) || stone.HP <= 0) return null;
        return new(Math.Ceiling(stone.HP).ToString("0", CultureInfo.InvariantCulture),
            CombatHudView.NormalizeHealth(stone.HP, TombstoneState.DefaultHP));
    }

    private static ItemHudResource Percentage(double value)
    {
        double safe = double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
        return new(Math.Ceiling(safe).ToString("0", CultureInfo.InvariantCulture) + "%", safe / 100);
    }
}
