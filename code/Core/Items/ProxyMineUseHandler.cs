using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Stages a seated terrain deployment in the existing item transaction.</summary>
internal sealed class ProxyMineUseHandler : IItemUseHandler
{
    public bool Stage(ItemSlot slot, VehiclePhysicsState pose, ItemConfiguration configuration,
        List<MissileState> missiles, Dictionary<ulong, float> repairs, List<OilPatch> patches,
        Func<ItemSlot, VehiclePhysicsState, OilPatch?>? placeOil, Dictionary<ulong, NitroState> boosts,
        List<ProxyMineState> mines, Func<ItemSlot, VehiclePhysicsState, ProxyMineState?>? placeMine)
    {
        if (mines.Count >= ItemAuthority.MaximumMines || placeMine?.Invoke(slot, pose) is not ProxyMineState mine) { return false; }
        mine.Validate();
        if (mine.Id != slot.Token || mine.Owner != slot.Vehicle || mine.SeatingTicks != 30 || mine.Velocity != System.Numerics.Vector3.Zero ||
            mine.Normal.Y < 0.55f || System.Numerics.Vector3.Distance(mine.Position, pose.Position) > 9)
        {
            throw new ArgumentException("Proxy Mine placement differs from the authorized use.");
        }
        mines.Add(mine);
        return true;
    }
}
