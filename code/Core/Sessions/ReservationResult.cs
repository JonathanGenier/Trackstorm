namespace Trackstorm.Core.Sessions;

/// <summary>Authoritative response to a retained-player control request; never grants gameplay ownership.</summary>
public enum ReservationResult
{
    /// <summary>No matching disconnected reservation exists.</summary>
    Missing,
    /// <summary>The authenticated subject owns the unchanged disconnected reservation.</summary>
    Available,
    /// <summary>The requested reservation was permanently released.</summary>
    Abandoned,
}
