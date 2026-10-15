using System.Numerics;

namespace Trackstorm.Core.Items;

/// <summary>One bounded ballistic parcel belonging to a single Oil activation.</summary>
public sealed record OilDrop(Vector3 Position, Vector3 Velocity, int RemainingTicks);
