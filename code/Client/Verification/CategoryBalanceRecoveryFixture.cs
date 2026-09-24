using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Verification;

/// <summary>Actual authority pickups at authored markers; native recovery then transports nonzero history.</summary>
internal static class CategoryBalanceRecoveryFixture
{
    internal static string Seed(NetworkVehicleArena arena)
    {
        var host = arena.Driver.Host!;
        var boundary = host.World.State;
        int marker = 0;
        foreach (var vehicle in boundary.Vehicles)
        {
            for (int i = 0; i < 3; i++)
            {
                var spawn = arena.MapConfiguration.Items[marker++];
                var pose = new VehiclePhysicsState(spawn.Position, vehicle.ObservedPhysics.Orientation, System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero);
                host.World.Restore(new(boundary.Tick, boundary.LastInput, boundary.Vehicles.Select(v => v.VehicleId == vehicle.VehicleId
                    ? new VehicleSnapshot(v.VehicleId, v.LifeId, new VehicleState(boundary.Tick, pose, false, false, 0, 0), v.Damage, pose) : v), boundary.Match));
                if (!host.Spawns!.TryPickup(host.World, spawn.Id, vehicle.VehicleId)) { throw new InvalidOperationException("Category recovery fixture pickup rejected."); }
                host.Items.RemovePlayer(vehicle.VehicleId);
            }
        }
        host.World.Restore(boundary);
        return Signature(host.Spawns!.Balances);
    }

    internal static string Signature(IReadOnlyList<PlayerItemBalance> balances) => string.Join(";", balances.OrderBy(b => b.Player).Select(b =>
        $"{b.Player}:{b.Total}:{b.SelectedItem}:" + string.Join(",", ItemRegistry.Categories.Select(c => $"{c.Identity}={b.Credits[c.Identity]}/{b.Counts[c.Identity]}"))));
}
