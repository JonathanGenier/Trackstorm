using System.Numerics;

namespace Trackstorm.Core.Items;

/// <summary>Authoritative point projectile, swept across its complete fixed-step segment.</summary>
public sealed record MissileState(ulong Id, ulong Owner, Vector3 Position, Vector3 Velocity, int RemainingTicks);
