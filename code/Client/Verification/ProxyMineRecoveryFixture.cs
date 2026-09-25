using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Committed isolated mine for recovery tests; normal deployment is exercised by the mine harness.</summary>
internal static class ProxyMineRecoveryFixture
{
    internal static ulong Seed(NetworkVehicleArena hostArena, IEnumerable<NetworkVehicleArena> peers)
    {
        foreach (var arena in peers)
        {
            var platform = new StaticBody3D { Position = new Vector3(500, 20, 0), CollisionLayer = 1 };
            platform.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(20, 1, 20) } });
            arena.AddChild(platform);
        }
        var host = hostArena.Driver.Host!;
        ulong owner = host.HostPlayerId;
        if (!host.Items.Grant(host.World, owner, HeldItem.ProxyMine)) { throw new InvalidOperationException("Mine recovery fixture requires an empty slot."); }
        var slot = host.Items.Slots.Single(slot => slot.Vehicle == owner);
        bool first = slot.Item == HeldItem.ProxyMine;
        ulong id = first ? slot.Token : slot.SecondToken;
        var mine = new ProxyMineState(id, owner, new N.Vector3(500, 20.758f, 0), new N.Vector3(1, 0, 0), N.Vector3.UnitY, 0);
        var state = new ItemPublication(Math.Max(1, host.Items.Revision), host.Snapshot(),
            host.Items.Slots.Select(value => value.Vehicle != owner ? value : first ? value with { Item = HeldItem.None } : value with { SecondItem = HeldItem.None }),
            host.Items.Missiles, [], host.Spawns?.States, host.Items.Patches, host.Items.OilContacts, host.Spawns?.Balances, [mine]);
        host.Items.Restore(state, host.Items.Revision + 1, host.Items.TokenHighWater);
        return id;
    }
}
