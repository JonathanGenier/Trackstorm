using Trackstorm.Client.Networking;
using Trackstorm.Core.Arenas;

namespace Trackstorm.Client.Verification;

/// <summary>Seeds a precise committed boundary to isolate recovery from native impact tests.</summary>
internal static class EnvironmentRecoveryFixture
{
    internal static void Seed(NetworkVehicleArena arena)
    {
        var host = arena.Driver.Host!;
        var snapshot = host.Environment!.Snapshot(host.SessionId, host.World.State.Tick);
        var rocks = snapshot.Rocks.ToArray();
        rocks[0] = new(2, 17, 0, default, default);
        rocks[1] = new(3, 0, 0, default, default);
        var plants = snapshot.Plants.ToArray(); plants[0] = true; plants[1] = true;
        host.Environment.Restore(new(snapshot.Session, snapshot.Tick, rocks, plants));
    }

    internal static void Verify(NetworkVehicleArena arena)
    {
        var state = arena.Driver.Host?.Environment?.Snapshot(arena.Driver.Host.SessionId, arena.Driver.Host.World.State.Tick) ?? arena.Driver.EnvironmentState;
        if (state is null || state.Rocks[0].Stage != 2 || state.Rocks[0].Damage != 17 || state.Rocks[1].Stage != 3 || !state.Plants[0] || !state.Plants[1])
        {
            throw new InvalidOperationException("Environment recovery lost staged damage or cleared plants.");
        }
        Godot.GD.Print("Environment recovery verified: Stage 2 damage 17, final rock and persistent cleared plants.");
    }
}
