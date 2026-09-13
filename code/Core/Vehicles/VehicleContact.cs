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
    public VehicleContact(Vector3 relativeVelocity, Vector3 normal, float impulse, ulong otherVehicleId)
    {
        _ = VehicleDamageMath.CollisionSeverity(relativeVelocity, normal, impulse, 1);
        RelativeVelocity = relativeVelocity;
        Normal = normal;
        Impulse = impulse;
        OtherVehicleId = otherVehicleId;
    }

    /// <summary>World-space relative contact velocity.</summary>
    public Vector3 RelativeVelocity { get; }
    /// <summary>Unit contact normal.</summary>
    public Vector3 Normal { get; }
    /// <summary>Native impulse magnitude.</summary>
    public float Impulse { get; }
    /// <summary>Other vehicle, or world identity zero.</summary>
    public ulong OtherVehicleId { get; }
}
