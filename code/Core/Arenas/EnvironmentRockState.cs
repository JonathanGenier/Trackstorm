using System.Numerics;

namespace Trackstorm.Core.Arenas;

/// <summary>One reserved rock-piece slot; stage zero is dormant, without native solver continuation.</summary>
public readonly record struct EnvironmentRockState(byte Stage, float Damage, ulong ImpactReadyTick, Vector3 Offset, Vector3 Velocity);
