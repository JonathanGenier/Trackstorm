using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Stages a validated host emission/geometry boundary in the ordinary item transaction.</summary>
internal sealed class OilUseHandler : IItemUseHandler
{
    public bool Stage(ItemSlot slot, VehiclePhysicsState pose, ItemConfiguration configuration,
        List<MissileState> missiles, Dictionary<ulong, float> repairs, List<OilPatch> patches,
        Func<ItemSlot, VehiclePhysicsState, OilPatch?>? placeOil, Dictionary<ulong, NitroState> boosts, List<ProxyMineState> mines, Func<ItemSlot, VehiclePhysicsState, ProxyMineState?>? placeMine, Func<ulong> nextToken, Func<System.Numerics.Vector3, System.Numerics.Vector3?>? ground)
    {
        if (placeOil?.Invoke(slot, pose) is not OilPatch patch)
        {
            return false;
        }

        patch.Validate();
        if (patch.Pour is { } pour && (pour.Life != slot.Life || pour.Speed != configuration.OilSpeed ||
            pour.Gravity != configuration.OilGravity || pour.Radius != configuration.OilRadius ||
            pour.DurationTicks != (int)MathF.Ceiling(configuration.OilFireSeconds * 60) || pour.IntervalTicks != configuration.OilIntervalTicks ||
            pour.Stiffness != configuration.OilSpringStiffness || pour.Damping != configuration.OilSpringDamping || pour.Reaction != configuration.OilSpringReaction ||
            pour.Emitted != 0 || pour.Deposited || pour.Cancelled || pour.Drops.Length != 0))
        { throw new ArgumentException("Oil emission differs from the authorized configuration."); }
        if (patch.Id != slot.Token || patch.Owner != slot.Vehicle || (patch.Pour is null && patch.Radius != 3) ||
            System.Numerics.Vector3.Distance(patch.Position, pose.Position) > 9)
        {
            throw new ArgumentException("Oil placement differs from the authorized use.");
        }

        patches.Add(patch with { PassLimit = configuration.OilPasses, PassesUsed = 0 });
        return true;
    }
}
