using System.Numerics;

namespace Trackstorm.Core.Items;

/// <summary>Authoritative point projectile, swept across its complete fixed-step segment.</summary>
public sealed record MissileState(ulong Id, ulong Owner, Vector3 Position, Vector3 Velocity, int RemainingTicks)
{
    /// <summary>Optional complete Salvo arc continuation; mutually exclusive with guided EMP flight.</summary>
    public SalvoFlight? Arc { get; init; }
    /// <summary>Optional guided EMP continuation; mutually exclusive with a Salvo arc.</summary>
    public EmpClawFlight? EmpClaw { get; init; }
    /// <summary>Stable item identity for effects and attribution.</summary>
    public HeldItem Item => EmpClaw is not null ? HeldItem.EmpClaw : Arc is null ? HeldItem.Missile : HeldItem.Salvo;
    /// <summary>A staged shot has no world/audio representation until its first committed step.</summary>
    public bool Launched => Arc is null || Arc.ElapsedTicks > 0;
}
