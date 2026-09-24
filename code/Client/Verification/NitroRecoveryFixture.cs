using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Verification;

/// <summary>Seeds a partial held resource to isolate exact checkpoint recovery; normal use has its own native harness.</summary>
internal static class NitroRecoveryFixture
{
    internal static void Seed(NetworkVehicleArena arena, ulong player)
    {
        var host = arena.Driver.Host!;
        if (!host.Items.Slots.Any(s => s.Vehicle == player && s.Item == HeldItem.Nitro))
        {
            if (!host.Items.Grant(host.World, player, HeldItem.Nitro)) { throw new InvalidOperationException("Nitro recovery fixture requires an empty slot."); }
        }
        var slots = host.Items.Slots.Select(s => s.Vehicle == player ? s with { NitroCharge = 37.5, EngagedToken = 0 } : s);
        host.Items.Restore(new ItemPublication(Math.Max(1, host.Items.Revision), host.Snapshot(), slots, host.Items.Missiles, [], host.Spawns?.States, host.Items.Patches, host.Items.OilContacts, host.Spawns?.Balances), host.Items.Revision + 1, host.Items.TokenHighWater);
    }
}
