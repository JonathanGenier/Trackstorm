using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Verification;

/// <summary>Seeds a committed hazard to isolate recovery from separately exercised native deployment.</summary>
internal static class OilRecoveryFixture
{
    internal static OilPatch Seed(NetworkVehicleArena arena)
    {
        var host = arena.Driver.Host!;
        ulong owner = host.HostPlayerId;
        if (!host.Items.Grant(host.World, owner, HeldItem.Oil)) { throw new InvalidOperationException("Oil recovery fixture needs an empty slot."); }
        var slot = host.Items.Slots.Single(slot => slot.Vehicle == owner);
        var pose = host.World.GetVehicle(owner).Movement.Physics;
        var patch = new OilPatch(slot.Token, owner, pose.Position - System.Numerics.Vector3.UnitY * 0.9f, System.Numerics.Vector3.UnitY, 3);
        var state = new ItemPublication(Math.Max(1, host.Items.Revision), host.Snapshot(),
            host.Items.Slots.Select(value => value.Vehicle == owner ? value with { Item = HeldItem.None } : value),
            host.Items.Missiles, [], host.Spawns?.States, [patch]);
        host.Items.Restore(state, host.Items.Revision + 1, host.Items.TokenHighWater);
        return patch;
    }
}
