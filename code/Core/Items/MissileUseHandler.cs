using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Stages a bounded straight projectile from the observed vehicle center, avoiding muzzle-offset tunneling.</summary>
internal sealed class MissileUseHandler : IItemUseHandler
{
    public bool Stage(ItemSlot slot, VehiclePhysicsState pose, ItemConfiguration configuration,
        List<MissileState> missiles, Dictionary<ulong, float> repairs, List<OilPatch> patches, Func<ItemSlot, VehiclePhysicsState, OilPatch?>? placeOil, Dictionary<ulong, NitroState> boosts, List<ProxyMineState> mines, Func<ItemSlot, VehiclePhysicsState, ProxyMineState?>? placeMine, Func<ulong> nextToken, Func<System.Numerics.Vector3, System.Numerics.Vector3?>? ground)
    {
        if (missiles.Count >= ItemAuthority.MaximumProjectiles)
        {
            return false;
        }

        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, pose.Orientation);
        missiles.Add(new MissileState(slot.Token, slot.Vehicle, pose.Position,
            forward * configuration.MissileSpeed, configuration.MissileLifetimeTicks));
        return true;
    }
}
