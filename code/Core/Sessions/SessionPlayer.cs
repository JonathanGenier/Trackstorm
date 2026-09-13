namespace Trackstorm.Core.Sessions;

/// <summary>Immutable session identity and host-accepted readiness.</summary>
/// <param name="Id">Nonzero identity stable until departure.</param>
/// <param name="Name">Sanitized display name, independent of identity.</param>
/// <param name="Ready">Whether this player is ready for the next arena.</param>
public sealed record SessionPlayer(ulong Id, string Name, bool Ready);
