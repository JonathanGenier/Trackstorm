using System.Numerics;

namespace Trackstorm.Core.Vehicles;

/// <summary>Native contact observations only; Core decides severity, attribution and damage.</summary>
public readonly record struct VehicleContact
{
    /// <summary>Copies a finite contact without retaining engine objects.</summary>
    /// <param name="relativeVelocity">Victim contact velocity minus other contact velocity.</param>
    /// <param name="normal">Unit world-space normal away from the other body.</param>
    /// <param name="impulse">Nonnegative contact impulse magnitude.</param>
    /// <param name="otherVehicleId">Stable vehicle identity, or zero for world/prop contacts.</param>
    /// <param name="terrain">Explicit driveable-terrain identity.</param>
    /// <param name="localPosition">Vehicle-local contact point.</param>
    /// <param name="staticObstacle">Immovable side contact, excluding support.</param>
    public VehicleContact(Vector3 relativeVelocity, Vector3 normal, float impulse, ulong otherVehicleId, bool terrain = false, Vector3 localPosition = default, bool staticObstacle = false)
    {
        _ = VehicleDamageMath.CollisionSeverity(relativeVelocity, normal, impulse, 1);
        if (!VehiclePhysicsState.IsFinite(localPosition)) { throw new ArgumentException("Invalid contact position."); }
        Terrain = terrain;
        LocalPosition = localPosition;
        RelativeVelocity = relativeVelocity;
        Normal = normal;
        Impulse = impulse;
        OtherVehicleId = otherVehicleId;
        StaticObstacle = staticObstacle;
    }

    /// <summary>Explicit driveable terrain identity; obstacles and vehicles are excluded.</summary>
    public bool Terrain { get; }
    /// <summary>Immovable side contact, excluding driveable support and movable bodies.</summary>
    public bool StaticObstacle { get; }
    /// <summary>Contact point relative to the vehicle origin in vehicle-local axes.</summary>
    public Vector3 LocalPosition { get; }
    /// <summary>World-space relative contact velocity.</summary>
    public Vector3 RelativeVelocity { get; }
    /// <summary>Unit contact normal.</summary>
    public Vector3 Normal { get; }
    /// <summary>Native impulse magnitude.</summary>
    public float Impulse { get; }
    /// <summary>Other vehicle, or world identity zero.</summary>
    public ulong OtherVehicleId { get; }
}
