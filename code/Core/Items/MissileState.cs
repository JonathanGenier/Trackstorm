using System.Numerics;

namespace Trackstorm.Core.Items;

/// <summary>Authoritative point projectile, swept across its complete fixed-step segment.</summary>
public sealed record MissileState(ulong Id, ulong Owner, Vector3 Position, Vector3 Velocity, int RemainingTicks)
{
    /// <summary>Optional complete scheduled arc continuation; null retains the straight projectile contract.</summary>
    public SalvoFlight? Arc { get; init; }
    /// <summary>Stable item identity for effects and attribution.</summary>
    public HeldItem Item => Arc is null ? HeldItem.Missile : HeldItem.Salvo;
    /// <summary>Scheduled rounds have no world/audio representation until launched.</summary>
    public bool Launched => Arc is null || Arc.ElapsedTicks > 0;
}
