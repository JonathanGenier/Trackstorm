using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Host-native motion and an actual walkable-ground contact; never client-authored.</summary>
public readonly record struct TombstoneObservation(VehiclePhysicsState Physics, Vector3? GroundContactNormal = null);
