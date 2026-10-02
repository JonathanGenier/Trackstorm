using System.Numerics;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Verification;

/// <summary>Damaged wall and attached shield exercise both checkpoint ownership modes.</summary>
internal static class TombstoneRecoveryFixture
{
    internal static IReadOnlyList<TombstoneState> Seed(NetworkVehicleArena arena, bool rearShield = false)
    {
        var host = arena.Driver.Host!;
        for (int i = 0; i < 2; i++)
        {
            if (!host.Items.Grant(host.World, host.HostPlayerId, HeldItem.Tombstone)) { throw new InvalidOperationException("Tombstone fixture grant failed."); }
            var state = host.Items.Tombstones.Last();
            host.Items.DamageTombstone(host.World, state.Id, 7, 300 + i * 100, new DamageContext("world", 0, "recovery-fixture"));
            var pose = new VehiclePhysicsState(new(100 + i * 10, 2, 100), Quaternion.Identity, Vector3.Zero, Vector3.Zero);
            var slot = host.Items.Slots.Single(s => s.Vehicle == state.Owner);
            if (slot.Active.Token != state.Token) { host.Items.Switch(host.World, state.Owner, state.Life, slot.SelectionRevision + 1); }
            if ((!rearShield || i == 0) && !host.Items.TransitionTombstone(host.World, state.Id, TombstoneStage.RearShield, TombstoneStage.WorldWall, pose))
            { throw new InvalidOperationException("Tombstone fixture transition failed."); }
        }
        return host.Items.Tombstones.TakeLast(2).ToArray();
    }

    internal static void Verify(IReadOnlyList<TombstoneState> actual, IReadOnlyList<TombstoneState> expected)
    {
        if (expected.Count != 2 || expected.Any(state => !actual.Contains(state)))
        { throw new InvalidOperationException("Tombstone checkpoint lost health, lifecycle, pose or replay memory."); }
        Godot.GD.Print($"Tombstone recovery verified: {string.Join('/', expected.Select(s => s.Stage))} at 700/600 HP, exact attachment/pose and damage watermark; no refill.");
    }
}
