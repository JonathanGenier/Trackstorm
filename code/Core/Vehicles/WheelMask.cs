namespace Trackstorm.Core.Vehicles;

/// <summary>Independent tire identities in suspension order.</summary>
[Flags]
public enum WheelMask : byte
{
    None = 0,
    FrontLeft = 1,
    FrontRight = 2,
    RearLeft = 4,
    RearRight = 8,
    All = 15
}
