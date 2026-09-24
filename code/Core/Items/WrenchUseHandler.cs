using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Stages repair through the existing vehicle damage authority, including full-health consumption.</summary>
internal sealed class WrenchUseHandler : IItemUseHandler
{
    public bool Stage(ItemSlot slot, VehiclePhysicsState pose, ItemConfiguration configuration,
        List<MissileState> missiles, Dictionary<ulong, float> repairs, List<OilPatch> patches, Func<ItemSlot, VehiclePhysicsState, OilPatch?>? placeOil, Dictionary<ulong, NitroState> boosts, List<ProxyMineState> mines, Func<ItemSlot, VehiclePhysicsState, ProxyMineState?>? placeMine)
    {
        repairs.Add(slot.Vehicle, configuration.WrenchHeal);
        return true;
    }
}
