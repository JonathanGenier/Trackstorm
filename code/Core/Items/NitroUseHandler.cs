using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Stages a bounded boost through the same item/vehicle transaction as other uses.</summary>
internal sealed class NitroUseHandler : IItemUseHandler
{
    public bool Stage(ItemSlot slot, VehiclePhysicsState pose, ItemConfiguration configuration,
        List<MissileState> missiles, Dictionary<ulong, float> repairs, List<OilPatch> patches,
        Func<ItemSlot, VehiclePhysicsState, OilPatch?>? placeOil, Dictionary<ulong, NitroState> boosts, List<ProxyMineState> mines, Func<ItemSlot, VehiclePhysicsState, ProxyMineState?>? placeMine, Func<ulong> nextToken, Func<System.Numerics.Vector3, System.Numerics.Vector3?>? ground)
    {
        boosts.Add(slot.Vehicle, new(Math.Clamp((int)Math.Ceiling(slot.NitroCharge * 60 / configuration.NitroConsumptionPerSecond), 1, 3600), configuration.NitroForwardThrust, configuration.NitroSpeedMultiplier, configuration.NitroAirborneThrustScale));
        return true;
    }
}
