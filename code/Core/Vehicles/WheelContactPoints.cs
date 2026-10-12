using System.Numerics;

namespace Trackstorm.Core.Vehicles;

/// <summary>Actual native tire support locations; null identifies an unsupported tire.</summary>
public readonly record struct WheelContactPoints(Vector3? FrontLeft, Vector3? FrontRight, Vector3? RearLeft, Vector3? RearRight)
{
    /// <summary>Supported location in suspension order.</summary>
    public Vector3? this[int index] => index switch
    {
        0 => FrontLeft, 1 => FrontRight, 2 => RearLeft, 3 => RearRight,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    internal void Validate()
    {
        for (int i = 0; i < 4; i++)
        {
            if (this[i] is Vector3 point && !VehiclePhysicsState.IsFinite(point)) { throw new ArgumentException("Invalid wheel contact point."); }
        }
    }
}
