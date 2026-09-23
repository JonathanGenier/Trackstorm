using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Stages a host-projected persistent patch in the ordinary item transaction.</summary>
internal sealed class OilUseHandler : IItemUseHandler
{
    public bool Stage(ItemSlot slot, VehiclePhysicsState pose, ItemConfiguration configuration,
        List<MissileState> missiles, Dictionary<ulong, float> repairs, List<OilPatch> patches,
        Func<ItemSlot, VehiclePhysicsState, OilPatch?>? placeOil)
    {
        if (patches.Count >= configuration.MaximumOilPatches || placeOil?.Invoke(slot, pose) is not OilPatch patch)
        {
            return false;
        }

        patch.Validate();
        if (patch.Id != slot.Token || patch.Owner != slot.Vehicle || patch.Radius != 3 ||
            System.Numerics.Vector3.Distance(patch.Position, pose.Position) > 9)
        {
            throw new ArgumentException("Oil placement differs from the authorized use.");
        }

        patches.Add(patch);
        return true;
    }
}
