using System.Numerics;

namespace Trackstorm.Core.Items;

/// <summary>First swept world contact with the supported local footprint radius; zero discards unsupported impacts.</summary>
public sealed record OilImpact(Vector3 Position, Vector3 Normal, float Radius);
