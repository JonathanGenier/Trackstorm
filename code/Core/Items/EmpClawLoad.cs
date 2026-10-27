namespace Trackstorm.Core.Items;

/// <summary>Three individually identified rounds in a retained launcher magazine.</summary>
public sealed record EmpClawLoad(ulong Magazine, byte Remaining, ulong ReadyTick = 0)
{
    /// <summary>Original live sockets; destruction never renumbers surviving rounds.</summary>
    public byte RoundMask { get; init; } = (byte)((7 << (3 - Remaining)) & 7);
    /// <summary>Accepted shots only; destruction must never replay a discharge.</summary>
    public byte ShotsFired { get; init; } = (byte)(3 - Remaining);
    /// <summary>Last accepted shot socket, or -1 before firing.</summary>
    public int FiredRound { get; init; } = Remaining < 3 ? 2 - Remaining : -1;
    /// <summary>Validates the retained empty magazine as well as a selected live load.</summary>
    public bool Valid(HeldItem item, ulong token, ulong tick) => Magazine > 0 && Magazine <= token && Remaining <= 3 &&
        RoundMask <= 7 && System.Numerics.BitOperations.PopCount(RoundMask) == Remaining && ShotsFired + Remaining <= 3 &&
        FiredRound is >= -1 and <= 2 && (ShotsFired == 0 ? FiredRound == -1 : FiredRound >= 0 && (RoundMask & (1 << FiredRound)) == 0) &&
        (Remaining > 0 ? item == HeldItem.EmpClaw : item == HeldItem.None && ReadyTick == 0) &&
        (ReadyTick <= tick || ReadyTick - tick <= 60);
}
