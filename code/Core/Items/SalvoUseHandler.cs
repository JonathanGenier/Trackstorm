using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Reserves the entire salvo atomically; all rounds share the firing-time host target.</summary>
internal sealed class SalvoUseHandler : IItemUseHandler
{
    public bool Stage(ItemSlot slot, VehiclePhysicsState pose, ItemConfiguration configuration,
        List<MissileState> missiles, Dictionary<ulong, float> repairs, List<OilPatch> patches,
        Func<ItemSlot, VehiclePhysicsState, OilPatch?>? placeOil, Dictionary<ulong, NitroState> boosts, List<ProxyMineState> mines, Func<ItemSlot, VehiclePhysicsState, ProxyMineState?>? placeMine,
        Func<ulong> nextToken, Func<Vector3, Vector3?>? ground)
    {
        if (missiles.Count + configuration.SalvoCount > ItemAuthority.MaximumProjectiles) { return false; }
        Vector3 aim = SalvoFlight.Aim(pose, configuration.SalvoRange);
        if (ground?.Invoke(aim) is not Vector3 target || !VehiclePhysicsState.IsFinite(target) ||
            Math.Abs(target.X - aim.X) > 0.01f || Math.Abs(target.Z - aim.Z) > 0.01f || Math.Abs(target.Y - aim.Y) > 200) { return false; }
        Vector3 origin = pose.Position + Vector3.UnitY * configuration.SalvoLaunchHeight;
        // Speed is mean travel speed along a sampled parabola, rather than its horizontal chord.
        var curve = new SalvoFlight(origin, target, configuration.SalvoArcHeight, 60, 0, 0, slot.Life);
        int duration = curve.Launch(origin, configuration.SalvoSpeed).DurationTicks;
        for (int i = 0; i < configuration.SalvoCount; i++)
        {
            var arc = curve with { DurationTicks = duration, DelayTicks = i * configuration.SalvoIntervalTicks };
            missiles.Add(new(i == 0 ? slot.Token : nextToken(), slot.Vehicle, origin, (arc.At(1) - origin) * 60, duration) { Arc = arc });
        }
        return true;
    }
}
