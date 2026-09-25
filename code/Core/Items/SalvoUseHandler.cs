using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Stages one individually requested shot; unused ammunition stays in the inventory.</summary>
internal sealed class SalvoUseHandler : IItemUseHandler
{
    public bool Stage(ItemSlot slot, VehiclePhysicsState pose, ItemConfiguration configuration,
        List<MissileState> missiles, Dictionary<ulong, float> repairs, List<OilPatch> patches,
        Func<ItemSlot, VehiclePhysicsState, OilPatch?>? placeOil, Dictionary<ulong, NitroState> boosts, List<ProxyMineState> mines, Func<ItemSlot, VehiclePhysicsState, ProxyMineState?>? placeMine,
        Func<ulong> nextToken, Func<Vector3, Vector3?>? ground)
    {
        if (slot.SalvoShots <= 0 || missiles.Count >= ItemAuthority.MaximumProjectiles) { return false; }
        if (SalvoFlight.ProjectTarget(pose, configuration.SalvoRange, ground) is not Vector3 target) { return false; }
        Vector3 origin = pose.Position + Vector3.UnitY * configuration.SalvoLaunchHeight;
        // Speed is mean travel speed along a sampled parabola, rather than its horizontal chord.
        var arc = new SalvoFlight(origin, target, configuration.SalvoArcHeight, 60, 0, slot.Life).Launch(origin, configuration.SalvoSpeed);
        if (Vector3.Distance(origin, target) is < 1 or > 600 || arc.DurationTicks > 3600) { return false; }
        missiles.Add(new(slot.Token, slot.Vehicle, origin, (arc.At(1) - origin) * 60, arc.DurationTicks) { Arc = arc });
        return true;
    }
}
