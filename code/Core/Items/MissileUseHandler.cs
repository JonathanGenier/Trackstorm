using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Stages one non-homing projectile at the production rack pivot; flight owns terrain correction.</summary>
internal sealed class MissileUseHandler : IItemUseHandler
{
    public bool Stage(ItemSlot slot, VehiclePhysicsState pose, ItemConfiguration configuration,
        List<MissileState> missiles, Dictionary<ulong, float> repairs, List<OilPatch> patches, Func<ItemSlot, VehiclePhysicsState, OilPatch?>? placeOil, Dictionary<ulong, NitroState> boosts, List<ProxyMineState> mines, Func<ItemSlot, VehiclePhysicsState, ProxyMineState?>? placeMine, Func<ulong> nextToken, Func<System.Numerics.Vector3, System.Numerics.Vector3?>? ground)
    {
        return Stage(slot, pose, configuration, missiles, null);
    }

    internal static bool Stage(ItemSlot slot, VehiclePhysicsState pose, ItemConfiguration configuration,
        List<MissileState> missiles, WeaponAimSolution? aim)
    {
        if (missiles.Count >= ItemAuthority.MaximumProjectiles)
        {
            return false;
        }

        Vector3 direction = aim?.Direction ?? Vector3.Transform(-Vector3.UnitZ, pose.Orientation);
        Vector3 origin = aim?.Origin ?? pose.Position + Vector3.Transform(WeaponAim.Pivot, pose.Orientation);
        missiles.Add(new MissileState(slot.Token, slot.Vehicle, origin,
            direction * configuration.MissileSpeed, Math.Min(configuration.MissileLifetimeTicks, (int)MathF.Ceiling(configuration.MissileLifetimeSeconds * 60))));
        return true;
    }
}
