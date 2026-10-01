using System.Numerics;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Verification;

/// <summary>Released damaged walls isolate full checkpoint installation from the later physical deployment Stories.</summary>
internal static class TombstoneRecoveryFixture
{
    internal static IReadOnlyList<TombstoneState> Seed(NetworkVehicleArena arena)
    {
        var host = arena.Driver.Host!;
        for (int i = 0; i < 2; i++)
        {
            if (!host.Items.Grant(host.World, host.HostPlayerId, HeldItem.Tombstone)) { throw new InvalidOperationException("Tombstone fixture grant failed."); }
            var state = host.Items.Tombstones.Last();
            host.Items.DamageTombstone(host.World, state.Id, 7, 300 + i * 100, new DamageContext("world", 0, "recovery-fixture"));
            var pose = new VehiclePhysicsState(new(100 + i * 10, 2, 100), Quaternion.Identity, Vector3.Zero, Vector3.Zero);
            if (!host.Items.TransitionTombstone(host.World, state.Id, TombstoneStage.Held, TombstoneStage.RearShield) ||
                !host.Items.TransitionTombstone(host.World, state.Id, TombstoneStage.RearShield, TombstoneStage.WorldWall, pose))
            { throw new InvalidOperationException("Tombstone fixture transition failed."); }
        }
        return host.Items.Tombstones.TakeLast(2).ToArray();
    }

    internal static void Verify(IReadOnlyList<TombstoneState> actual, IReadOnlyList<TombstoneState> expected)
    {
        if (expected.Count != 2 || expected.Any(state => !actual.Contains(state)))
        { throw new InvalidOperationException("Tombstone checkpoint lost health, lifecycle, pose or replay memory."); }
        Godot.GD.Print("Tombstone recovery verified: two independent world walls at 700/600 HP, exact pose and damage watermark; no refill.");
    }
}
