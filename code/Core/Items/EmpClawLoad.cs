namespace Trackstorm.Core.Items;

/// <summary>Minimal three-shot presentation capability, without projectile, damage or destructible-ammunition rules.</summary>
public sealed record EmpClawLoad(ulong Magazine, byte Remaining, ulong ReadyTick = 0)
{
    /// <summary>Validates the retained empty magazine as well as a selected live load.</summary>
    public bool Valid(HeldItem item, ulong token, ulong tick) => Magazine > 0 && Magazine <= token && Remaining <= 3 &&
        (Remaining > 0 ? item == HeldItem.EmpClaw : item == HeldItem.None && ReadyTick == 0) &&
        (ReadyTick <= tick || ReadyTick - tick <= 60);
}
