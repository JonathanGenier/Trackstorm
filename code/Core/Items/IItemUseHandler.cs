using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Stages item-specific effects in a detached batch; ItemAuthority alone commits it.</summary>
internal interface IItemUseHandler
{
    bool Stage(ItemSlot slot, VehiclePhysicsState pose, ItemConfiguration configuration,
        List<MissileState> missiles, Dictionary<ulong, float> repairs, List<OilPatch> patches, Func<ItemSlot, VehiclePhysicsState, OilPatch?>? placeOil, Dictionary<ulong, NitroState> boosts, List<ProxyMineState> mines, Func<ItemSlot, VehiclePhysicsState, ProxyMineState?>? placeMine, Func<ulong> nextToken, Func<System.Numerics.Vector3, System.Numerics.Vector3?>? ground);
}
