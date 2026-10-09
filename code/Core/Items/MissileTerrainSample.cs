using System.Numerics;

namespace Trackstorm.Core.Items;

/// <summary>Host-observed suitable static surface; vehicles, props and water are never steering targets.</summary>
public sealed record MissileTerrainSample(Vector3 Position, Vector3 Normal);
