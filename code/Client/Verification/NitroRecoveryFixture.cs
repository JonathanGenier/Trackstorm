using Trackstorm.Client.Networking;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Verification;

/// <summary>Seeds committed state to isolate checkpoint recovery; normal activation is exercised by NitroIntegrationChecks.</summary>
internal static class NitroRecoveryFixture
{
    internal static void Seed(NetworkVehicleArena arena, ulong player)
    {
        var w = arena.Driver.Host!.World;
        var boundary = w.State;
        w.Restore(new(boundary.Tick, boundary.LastInput, boundary.Vehicles.Select(v => v.VehicleId != player ? v :
            new VehicleSnapshot(v.VehicleId, v.LifeId, v.Movement with { Nitro = new NitroState(3600, 2, 1.4f) },
                v.Damage, v.ObservedPhysics, v.Effects, v.Lifecycle, v.RespawnAtTick)), boundary.Match));
    }
}
