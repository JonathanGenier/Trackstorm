namespace Trackstorm.Core.Vehicles;

/// <summary>Portable brake/reverse intent; service braking cannot become reverse until a new press.</summary>
public enum BrakeMode : byte
{
    /// <summary>No remaining pedal demand; the next press chooses braking or reverse.</summary>
    Ready,
    /// <summary>A forward stop is held until release.</summary>
    Stopping,
    /// <summary>A deliberate press from rest/reverse permits reverse propulsion.</summary>
    Reversing,
    /// <summary>Physical release occurred but digital pedal decay still requests service braking.</summary>
    ReleaseTail,
}
