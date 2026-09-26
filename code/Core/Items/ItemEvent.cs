using System.Numerics;

namespace Trackstorm.Core.Items;

/// <summary>Reliable presentation outcome. Impact is false for launch/repair, true only for a collision explosion.</summary>
public sealed record ItemEvent(ulong Token, ulong Owner, HeldItem Item, Vector3 Position, bool Impact)
{
    /// <summary>Confirmed ray origin for machine-gun tracers; zero for other outcomes.</summary>
    public Vector3 Origin { get; init; }
    /// <summary>Whether this confirmed round is selected for a visible tracer.</summary>
    public bool Tracer { get; init; }
}
