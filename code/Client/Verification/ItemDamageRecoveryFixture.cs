using System.Numerics;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Verification;

/// <summary>Seeds applied item damage, not fabricated points, before native recovery checkpoints.</summary>
internal static class ItemDamageRecoveryFixture
{
    internal static void Seed(NetworkVehicleArena arena, ulong owner)
    {
        var host = arena.Driver.Host!;
        if (!host.TryConfigure(0, new Dictionary<string, double> { ["match.item_points_per_damage"] = 0.375 }, out var error))
        { throw new InvalidOperationException(error); }
        var before = host.World.State.Match!.Players.Single(p => p.Player == owner);
        ulong target = host.World.State.Vehicles.First(v => v.VehicleId != owner).VehicleId;
        var input = new InputFrame(host.World.State.Tick + 1, 0, 0, 0, 0, 0, 0);
        host.World.Step(input, host.World.State.Vehicles.Select(v => new VehicleStepRequest(v.VehicleId, input,
            new(v.ObservedPhysics, v.Movement.Grounded ? Vector3.UnitY : Vector3.Zero),
            v.VehicleId == target ? new[] { "missile", "proxy-mine", "salvo" }.Select(source =>
                new VehicleEffectRequest(new DamageEffect(2, Vector3.Zero, Vector3.Zero), new DamageContext(source, owner, "recovery fixture"))).ToArray() : [])).ToArray());
        double expected = 6 * 0.375 * before.KdMultiplier;
        double points = host.World.State.Match!.Awards.Where(a => a.Player == owner && a.Category == Core.Matches.CircusScoreCategory.ItemDamage).Sum(a => a.Points);
        if (points != expected) { throw new InvalidOperationException($"Recovery seed expected {expected} item points, got {points}."); }
        Godot.GD.Print($"Item recovery seed: three distinct applied hits, {points} points, rate 0.375, target watermark {host.World.State.Match.Players.Single(p => p.Player == target).ProcessedDamageSequence}.");
    }
}
