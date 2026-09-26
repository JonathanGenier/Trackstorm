using System.Numerics;

namespace Trackstorm.Core.Vehicles;

/// <summary>Complete airborne continuation for authoritative stepping, prediction and recovery.</summary>
public readonly record struct AirControlState(float Seconds, Vector3 Input, Vector3 Stabilization)
{
    /// <summary>Rejects malformed continuation before restoring a vehicle.</summary>
    public void Validate()
    {
        if (!float.IsFinite(Seconds) || Seconds is < 0 or > 60 ||
            !VehiclePhysicsState.IsFinite(Input) || Math.Abs(Input.X) > 1 || Math.Abs(Input.Y) > 1 || Math.Abs(Input.Z) > 1 ||
            !VehiclePhysicsState.IsFinite(Stabilization) || Stabilization.X is < 0 or > 1 || Stabilization.Y is < 0 or > 1 || Stabilization.Z is < 0 or > 1)
        {
            throw new ArgumentException("Invalid air-control continuation.");
        }
    }
}
