using System.Numerics;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Verification;

/// <summary>Partial ammunition and slow flying rounds isolate checkpoint installation from normal timing.</summary>
internal static class SalvoRecoveryFixture
{
    internal static void Seed(NetworkVehicleArena arena)
    {
        var host = arena.Driver.Host!;
        var owner = host.World.GetVehicle(host.HostPlayerId);
        Vector3 origin = owner.Movement.Physics.Position + Vector3.UnitY * 80;
        Vector3 target = origin - Vector3.UnitZ * 65;
        ulong token = host.Items.TokenHighWater;
        var slots = host.Items.Slots.ToList();
        var inventory = slots.SingleOrDefault(s => s.Vehicle == owner.VehicleId) ?? new(owner.VehicleId, owner.LifeId, 0, HeldItem.None);
        slots.Remove(inventory);
        slots.Add(inventory with { SecondToken = ++token, SecondItem = HeldItem.Salvo, SecondNitroCharge = 0, SecondSalvoShots = 3, SecondSalvoReadyTick = host.World.State.Tick + 30 });
        var rounds = Enumerable.Range(0, 2).Select(i =>
        {
            var arc = new SalvoFlight(origin, target, 12, 1800, 30 + i * 30, owner.LifeId);
            return new MissileState(++token, owner.VehicleId, arc.At(arc.ElapsedTicks), (arc.At(arc.ElapsedTicks + 1) - arc.At(arc.ElapsedTicks)) * 60, arc.DurationTicks - arc.ElapsedTicks) { Arc = arc };
        }).ToArray();
        host.Items.Restore(new(Math.Max(1, host.Items.Revision), host.Snapshot(), slots, rounds, [], host.Spawns?.States,
            host.Items.Patches, host.Items.OilContacts, host.Spawns?.Balances, host.Items.Mines), host.Items.Revision + 1, token);
    }

    internal static void Verify(ItemPublication state, IReadOnlyDictionary<ulong, ItemPublication> boundaries)
    {
        if (!boundaries.TryGetValue(state.World.Tick, out var expected) || expected.Missiles.Count != 2 || state.Events.Count != 0 ||
            !state.Missiles.SequenceEqual(expected.Missiles) || !state.Slots.SequenceEqual(expected.Slots) || !state.Slots.Any(s => s.SecondItem == HeldItem.Salvo && s.SecondSalvoShots == 3))
        { throw new InvalidOperationException("Salvo recovery did not install exact partial ammunition, cooldown and flying rounds."); }
        GDPrint(state);
    }

    private static void GDPrint(ItemPublication state) => Godot.GD.Print($"Salvo recovery verified: exact three held shots, cooldown and two flying rounds at tick {state.World.Tick}; no historical events.");
}
