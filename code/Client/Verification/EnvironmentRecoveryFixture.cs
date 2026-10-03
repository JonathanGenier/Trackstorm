using Trackstorm.Client.Networking;
using Trackstorm.Core.Arenas;

namespace Trackstorm.Client.Verification;

/// <summary>Seeds a precise committed boundary to isolate recovery from native impact tests.</summary>
internal static class EnvironmentRecoveryFixture
{
    internal static EnvironmentSnapshot Seed(NetworkVehicleArena arena)
    {
        var host = arena.Driver.Host!;
        var snapshot = host.Environment!.Snapshot(host.SessionId, host.World.State.Tick);
        var rocks = snapshot.Rocks.ToArray();
        ulong deadline = snapshot.Tick + EnvironmentAuthority.DebrisLifetimeTicks;
        rocks[0] = new(2, 17, 0, default, default, deadline);
        rocks[1] = new(arena.MapConfiguration.Environment!.FinalStage(1), 0, 0, new(1, 0, 0), default, deadline);
        var plants = snapshot.Plants.ToArray(); plants[0] = true; plants[1] = true;
        var seeded = new EnvironmentSnapshot(snapshot.Session, snapshot.Tick, rocks, plants);
        host.Environment.Restore(seeded);
        return seeded;
    }

    internal static void Verify(NetworkVehicleArena arena, EnvironmentSnapshot seeded)
    {
        var state = arena.Driver.Host?.Environment?.Snapshot(arena.Driver.Host.SessionId, arena.Driver.Host.World.State.Tick) ?? arena.Driver.EnvironmentState;
        ulong deadline = seeded.Rocks[0].ExpiresAtTick;
        if (state is null || !state.Plants[0] || !state.Plants[1])
        {
            throw new InvalidOperationException("Environment recovery lost cleared plants.");
        }
        for (int i = 0; i < EnvironmentLayout.PiecesPerRock; i++)
        {
            var expected = state.Tick >= deadline ? default : seeded.Rocks[i];
            if (state.Rocks[i] != expected)
                throw new InvalidOperationException($"Environment recovery changed piece {i}, damage or original deadline {deadline} at tick {state.Tick}.");
            if (state.Tick >= deadline && arena.Map.FindChildren($"BrokenRock{i}", "MeshInstance3D", true, false).Any(node => !node.IsQueuedForDeletion()))
                throw new InvalidOperationException("Expired debris was recreated by the recovered native view.");
        }
        Godot.GD.Print($"Environment recovery verified at tick {state.Tick}: original deadline {deadline}, expired={state.Tick >= deadline}, exact piece state and persistent cleared plants.");
    }
}
