using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Verification;

/// <summary>Seeds a committed hazard to isolate recovery from separately exercised native deployment.</summary>
internal static class OilRecoveryFixture
{
    internal const int PatchCount = 1500;
    internal static OilPatch Seed(NetworkVehicleArena arena)
    {
        var host = arena.Driver.Host!;
        ulong owner = host.HostPlayerId;
        if (!host.Items.Grant(host.World, owner, HeldItem.Oil)) { throw new InvalidOperationException("Oil recovery fixture needs an empty slot."); }
        var slot = host.Items.Slots.Single(slot => slot.Vehicle == owner);
        var pose = host.World.GetVehicle(owner).Movement.Physics;
        var patch = new OilPatch(slot.Token, owner, pose.Position + System.Numerics.Vector3.UnitX * 40 - System.Numerics.Vector3.UnitY * 0.9f, System.Numerics.Vector3.UnitY, 3) { ExpiresAtTick = host.World.State.Tick + 36000, PassesUsed = 1 };
        ulong token = host.Items.TokenHighWater;
        var patches = Enumerable.Range(0, PatchCount).Select(i => i == 0 ? patch : patch with { Id = token + (ulong)i, PassesUsed = 0, Position = patch.Position + System.Numerics.Vector3.UnitX * (100 + i * 7) }).ToArray();
        OilContact[] contacts = [];
        var state = new ItemPublication(Math.Max(1, host.Items.Revision), host.Snapshot(),
            host.Items.Slots.Select(value => value.Vehicle == owner ? value with { Item = HeldItem.None } : value),
            host.Items.Missiles, [], host.Spawns?.States, patches, contacts);
        host.Items.Restore(state, host.Items.Revision + 1, token + PatchCount - 1);
        return patch;
    }

    internal static void Verify(ItemPublication state, OilPatch patch)
    {
        if (state.Patches.Count != PatchCount || state.Patches.Single(p => p.Id == patch.Id) != patch || state.OilContacts.Count != 0)
        { throw new InvalidOperationException("Large Oil continuation lost ownership, deadline or consumed pass count."); }
    }
}
