namespace Trackstorm.Core.Sessions;

/// <summary>Immutable session identity and host-accepted readiness.</summary>
/// <param name="Id">Nonzero identity stable until departure.</param>
/// <param name="Name">Sanitized display name, independent of identity.</param>
/// <param name="Ready">Whether this player is ready for the next arena.</param>
/// <param name="Connected">Whether a live peer currently owns this reserved slot.</param>
/// <param name="Generation">Monotonic connection generation, independent of player and match identity.</param>
/// <param name="RetainedHost">Former authority retained until the current match ends, independently of reconnect grace.</param>
public sealed record SessionPlayer(ulong Id, string Name, bool Ready, bool Connected = true, ulong Generation = 1, bool RetainedHost = false);
