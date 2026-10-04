using System.Numerics;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Verification;

/// <summary>Damaged wall and attached shield exercise both checkpoint ownership modes.</summary>
internal static class ShieldRecoveryFixture
{
    internal static IReadOnlyList<ShieldState> Seed(NetworkVehicleArena arena, bool rearShield = false)
    {
        var host = arena.Driver.Host!;
        for (int i = 0; i < 2; i++)
        {
            if (!host.Items.Grant(host.World, host.HostPlayerId, HeldItem.Shield)) { throw new InvalidOperationException("Shield fixture grant failed."); }
            var state = host.Items.Shields.Last();
            var pose = new VehiclePhysicsState(new(100 + i * 10, 2, 100), Quaternion.Identity, Vector3.Zero, Vector3.Zero);
            if (host.World.State.Match?.Phase == Core.Matches.MatchPhase.Countdown)
            {
                // Like the Oil/Mine recovery fixtures, install a validated continuation to isolate
                // recovery. Never invoke damage/deployment during the two-player Countdown test.
                var wall = state with
                {
                    Stage = ShieldStage.WorldWall, Life = 0, Token = 0, HP = ShieldState.DefaultHP - (300 + i * 100),
                    DamageSequence = 7, Position = pose.Position, Orientation = pose.Orientation,
                    ExpiresAtTick = checked(host.World.State.Tick + (ulong)MathF.Ceiling(host.Items.Configuration.ShieldLifetimeSeconds * 60)),
                };
                var slots = host.Items.Slots.Select(slot => slot.Vehicle != state.Owner ? slot : slot.Token == state.Token
                    ? slot with { Item = HeldItem.None } : slot with { SecondItem = HeldItem.None });
                var publication = new ItemPublication(Math.Max(1, host.Items.Revision), host.Snapshot(), slots,
                    host.Items.Missiles, [], host.Spawns?.States, host.Items.Patches, host.Items.OilContacts,
                    host.Spawns?.Balances, host.Items.Mines, host.Items.Shields.Select(shield => shield.Id == state.Id ? wall : shield), host.Items.DiscardRevision);
                host.Items.Restore(publication, host.Items.Revision + 1, host.Items.TokenHighWater);
                continue;
            }
            host.Items.DamageShield(host.World, state.Id, 7, 300 + i * 100, new DamageContext("world", 0, "recovery-fixture"));
            var slot = host.Items.Slots.Single(s => s.Vehicle == state.Owner);
            if (slot.Active.Token != state.Token) { host.Items.Switch(host.World, state.Owner, state.Life, slot.SelectionRevision + 1); }
            if ((!rearShield || i == 0) && !host.Items.TransitionShield(host.World, state.Id, ShieldStage.RearShield, ShieldStage.WorldWall, pose))
            { throw new InvalidOperationException("Shield fixture transition failed."); }
        }
        return host.Items.Shields.TakeLast(2).ToArray();
    }

    internal static void Verify(IReadOnlyList<ShieldState> actual, IReadOnlyList<ShieldState> expected, IReadOnlyList<ShieldState> boundary, ulong tick)
    {
        bool Matches(ShieldState seed) => !seed.Attached && seed.ExpiresAtTick <= tick
            ? actual.All(state => state.Id != seed.Id)
            : actual.Any(state => state.Id == seed.Id && state.HP == seed.HP && state.DamageSequence == seed.DamageSequence && state.Stage == seed.Stage && state.ExpiresAtTick == seed.ExpiresAtTick && state.Tipping == seed.Tipping);
        if (expected.Count != 2 || expected.Any(seed => !Matches(seed)) || !actual.SequenceEqual(boundary))
        { throw new InvalidOperationException("Shield checkpoint lost health, lifecycle, pose or replay memory."); }
        Godot.GD.Print($"Shield recovery verified: {actual.Count} retained pools; {expected.Count(s => !s.Attached && s.ExpiresAtTick <= tick)} expired walls absent; exact HP, expiry, tipping, pose and damage watermark; no refill or timer restart.");
    }
}
