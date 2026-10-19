using System.Numerics;

namespace Trackstorm.Core.Items;

/// <summary>Reliable presentation outcome. Impact is false for launch/repair, true for a confirmed collision, including a non-damaging EMP contact.</summary>
public sealed record ItemEvent(ulong Token, ulong Owner, HeldItem Item, Vector3 Position, bool Impact)
{
    /// <summary>Confirmed ray origin for machine-gun tracers, or launch/contact segment origin for EMP; zero for other outcomes.</summary>
    public Vector3 Origin { get; init; }
    /// <summary>Whether this confirmed round is selected for a visible tracer.</summary>
    public bool Tracer { get; init; }
    /// <summary>EMP-only authoritative vehicle contact for the subsequent attachment consumer.</summary>
    public EmpClawContact? EmpClawContact { get; init; }
}
