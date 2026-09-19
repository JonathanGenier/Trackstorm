namespace Trackstorm.Core.Vehicles;

/// <summary>Metre dimensions of the Blender-authored Trackstorm vehicle, independent of engine transforms.</summary>
public static class VehicleDimensions
{
    /// <summary>Uniform conversion from the preserved 3.505 metre design.</summary>
    public const float Scale = 4.81f / 3.505f;
    /// <summary>Complete bumper-to-bumper length.</summary>
    public const float Length = 4.81f;
    /// <summary>Complete armor width.</summary>
    public const float Width = 1.94f * Scale;
    /// <summary>Distance between authored axle centers.</summary>
    public const float Wheelbase = 1.8954f * Scale;
    /// <summary>Distance between left and right tire centers.</summary>
    public const float WheelTrack = 1.19f * Scale;
    /// <summary>Authored vertical tire radius; wheels remain static presentation.</summary>
    public const float WheelRadius = 0.354f * Scale;
    /// <summary>Origin above a level road at default spring equilibrium.</summary>
    public const float RideHeight = 0.9f;
    /// <summary>Additional spawn height above the oval's original 0.85 m markers, clearing static tires during reconstruction.</summary>
    public const float SpawnLift = 0.2f;
    /// <summary>Vertical translation baked after the uniform mesh conversion.</summary>
    public const float OriginShift = (0.735f * Scale) - RideHeight;
    /// <summary>Conservative horizontal diameter used to avoid overlapping arbitrary respawn headings.</summary>
    public const float SpawnClearance = 5.6f;
}
