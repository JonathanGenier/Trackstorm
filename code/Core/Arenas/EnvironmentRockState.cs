using System.Numerics;

namespace Trackstorm.Core.Arenas;

/// <summary>One bounded staged rock; no fragment allocation or native solver continuation.</summary>
public readonly record struct EnvironmentRockState(byte Stage, float Damage, ulong ImpactReadyTick, Vector3 Offset, Vector3 Velocity);
