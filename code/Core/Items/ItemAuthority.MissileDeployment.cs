namespace Trackstorm.Core.Items;

public sealed partial class ItemAuthority
{
    private ItemSlot SelectMissile(ItemSlot inventory, ulong tick)
    {
        ulong start = Math.Max(tick, inventory.MissileStowEndTick);
        return inventory with { MissileDeployStartTick = start,
            MissileReadyTick = checked(start + (ulong)MathF.Ceiling(Configuration.MissileDeploySeconds * 60)) };
    }

    private ItemSlot StowMissile(ItemSlot inventory, ulong tick) => inventory with
    {
        MissileDeployStartTick = 0, MissileReadyTick = 0,
        MissileStowStartTick = tick,
        MissileStowEndTick = checked(tick + (ulong)MathF.Ceiling(Configuration.MissileStowSeconds * 60)),
    };
}
