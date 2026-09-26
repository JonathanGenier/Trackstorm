using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Verification;

/// <summary>Recovery fixture preserving partial rounds and the fractional firing clock without historical fire.</summary>
internal static class MachineGunRecoveryFixture
{
    internal static void Seed(NetworkVehicleArena arena, ulong player)
    {
        var host = arena.Driver.Host!;
        if (!host.Items.Grant(host.World, player, HeldItem.MachineGun)) { throw new InvalidOperationException("Machine gun recovery requires one empty slot."); }
        var slots = host.Items.Slots.Select(slot => slot.Vehicle != player ? slot : slot.Item == HeldItem.MachineGun
            ? slot with { Ammo = new(299, 800, 0.25), EngagedToken = 0 }
            : slot with { SecondAmmo = new(299, 800, 0.25), EngagedToken = 0 });
        host.Items.Restore(new(Math.Max(1, host.Items.Revision), host.Snapshot(), slots, host.Items.Missiles, [], host.Spawns?.States, host.Items.Patches, host.Items.OilContacts, host.Spawns?.Balances, host.Items.Mines), host.Items.Revision + 1, host.Items.TokenHighWater);
    }

    internal static void Verify(ItemPublication state, ulong player)
    {
        var slot = state.Slots.Single(s => s.Vehicle == player);
        var ammo = slot.Item == HeldItem.MachineGun ? slot.Ammo : slot.SecondAmmo;
        if (ammo != new MachineGunAmmo(299, 800, 0.25) || state.Events.Any(e => e.Item == HeldItem.MachineGun))
        { throw new InvalidOperationException("Machine gun checkpoint changed rounds/cadence or replayed a historical shot."); }
        Godot.GD.Print("Machine gun recovery verified: 299/800 rounds, phase 0.25, no historical shot.");
    }
}
