using System.Numerics;

namespace Trackstorm.Core.Vehicles;

/// <summary>Engine-independent observation of the last resolved body pose and velocities.</summary>
public readonly record struct VehiclePhysicsState
{
    /// <summary>Creates a finite body observation. Orientation must be a unit quaternion.</summary>
    /// <param name="position">World position in metres.</param>
    /// <param name="orientation">World orientation.</param>
    /// <param name="linearVelocity">Metres per second.</param>
    /// <param name="angularVelocity">Radians per second.</param>
    public VehiclePhysicsState(Vector3 position, Quaternion orientation, Vector3 linearVelocity, Vector3 angularVelocity)
    {
        if (!IsFinite(position) || !IsFinite(linearVelocity) || !IsFinite(angularVelocity) || !float.IsFinite(orientation.LengthSquared()) || Math.Abs(orientation.LengthSquared() - 1) > 0.001f)
        {
            throw new ArgumentException("Physics observations require finite vectors and a unit orientation.");
        }

        Position = position;
        Orientation = orientation;
        LinearVelocity = linearVelocity;
        AngularVelocity = angularVelocity;
    }

    /// <summary>World position.</summary>
    public Vector3 Position { get; }
    /// <summary>World orientation; forward is local negative Z.</summary>
    public Quaternion Orientation { get; }
    /// <summary>World linear velocity.</summary>
    public Vector3 LinearVelocity { get; }
    /// <summary>World angular velocity.</summary>
    public Vector3 AngularVelocity { get; }

    /// <summary>Rejects non-finite native observations before they enter authoritative state.</summary>
    /// <param name="value">Vector to inspect.</param>
    /// <returns>Whether every component is finite.</returns>
    public static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
