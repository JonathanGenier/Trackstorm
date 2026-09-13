using System.Numerics;

namespace Trackstorm.Core.Arenas;

/// <summary>Stable authored location, independent of session player identity.</summary>
public sealed record ArenaSpawn(string Id, Vector3 Position, float Yaw = 0);
