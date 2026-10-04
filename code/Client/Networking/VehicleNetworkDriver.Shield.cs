using Trackstorm.Core.Items;

namespace Trackstorm.Client.Networking;

internal sealed partial class VehicleNetworkDriver
{
    private (ulong Life, ulong Token, ulong Selection)? _pendingShieldUse;

    /// <summary>Local presentation readiness; delays sending intent, never decides an authoritative outcome.</summary>
    internal Func<ItemSlot, bool>? ShieldReady { get; set; }

    private void FlushShieldUse(uint? inputSequence = null)
    {
        if (_pendingShieldUse is not { } pending) { return; }
        var inventory = RequestedInventory();
        if (!AllowsParticipation || LocalState is not { CanInteract: true } state || state.LifeId != pending.Life ||
            inventory is null || inventory.Life != pending.Life || inventory.SelectionRevision != pending.Selection ||
            inventory.Active is not { Item: HeldItem.Shield } slot || slot.Token != pending.Token)
        { _pendingShieldUse = null; return; }

        // Selection must be confirmed before a remote player's old rack pose can
        // release the queued press. Repeated taps coalesce into this one capability.
        if (LocalItem?.SelectionRevision != pending.Selection || ShieldReady?.Invoke(inventory) == false) { return; }
        _pendingShieldUse = null;
        RequestItemUse(inputSequence);
    }
}
