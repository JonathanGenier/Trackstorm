namespace Trackstorm.Core.Vehicles;

/// <summary>Portable brake/reverse intent; service braking continues through zero into reverse.</summary>
public enum BrakeMode : byte
{
    /// <summary>No remaining pedal demand; the next press chooses braking or reverse.</summary>
    Ready,
    /// <summary>Forward service braking; crossing rest permits reverse on the same hold.</summary>
    Stopping,
    /// <summary>A press from rest or a continued forward stop permits reverse propulsion.</summary>
    Reversing,
    /// <summary>Physical release occurred but digital pedal decay still requests service braking.</summary>
    ReleaseTail,
}
