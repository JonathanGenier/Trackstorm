namespace Trackstorm.Core.Vehicles;

/// <summary>Authoritative terrain landing episode; airborne never implies collision immunity.</summary>
public enum LandingPhase : byte
{
    Driving,
    Airborne,
    Recovery,
    Recovered,
    Crash,
}
