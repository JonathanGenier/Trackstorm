using System.Numerics;

namespace Trackstorm.Core.Items;

/// <summary>Reliable presentation outcome. Impact is false for launch/repair, true only for a collision explosion.</summary>
public sealed record ItemEvent(ulong Token, ulong Owner, HeldItem Item, Vector3 Position, bool Impact);
