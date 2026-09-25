using System.Numerics;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Verification;

/// <summary>Slow scheduled/in-flight rounds isolate checkpoint installation from normal flight timing.</summary>
internal static class SalvoRecoveryFixture
{
    internal static void Seed(NetworkVehicleArena arena)
    {
        var host = arena.Driver.Host!;
        var owner = host.World.GetVehicle(host.HostPlayerId);
        Vector3 origin = owner.Movement.Physics.Position + Vector3.UnitY * 80;
        Vector3 target = origin - Vector3.UnitZ * 65;
        ulong token = host.Items.TokenHighWater;
        var rounds = Enumerable.Range(0, 5).Select(i =>
        {
            var arc = new SalvoFlight(origin, target, 12, 1800, i == 0 ? 30 : 0, i == 0 ? 0 : 900 - i * 6, owner.LifeId);
            return new MissileState(++token, owner.VehicleId, arc.At(arc.ElapsedTicks), (arc.At(arc.ElapsedTicks + 1) - arc.At(arc.ElapsedTicks)) * 60, arc.DurationTicks - arc.ElapsedTicks) { Arc = arc };
        }).ToArray();
        host.Items.Restore(new(Math.Max(1, host.Items.Revision), host.Snapshot(), host.Items.Slots, rounds, [], host.Spawns?.States,
            host.Items.Patches, host.Items.OilContacts, host.Spawns?.Balances, host.Items.Mines), host.Items.Revision + 1, token);
    }

    internal static void Verify(ItemPublication state, IReadOnlyDictionary<ulong, IReadOnlyList<MissileState>> boundaries)
    {
        if (!boundaries.TryGetValue(state.World.Tick, out var expected) || expected.Count != 5 || !state.Missiles.SequenceEqual(expected))
        { throw new InvalidOperationException("Salvo recovery did not install exact scheduled and in-flight rounds."); }
        GDPrint(state);
    }

    private static void GDPrint(ItemPublication state) => Godot.GD.Print($"Salvo recovery verified: exact five-round continuation at tick {state.World.Tick}; no historical events.");
}
