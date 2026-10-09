namespace Trackstorm.Core.Sessions;

/// <summary>Immutable session identity and host-accepted readiness.</summary>
/// <param name="Id">Nonzero identity stable until departure.</param>
/// <param name="Name">Sanitized display name, independent of identity.</param>
/// <param name="Ready">Whether this player is ready for the next arena.</param>
/// <param name="Connected">Whether the human peer is connected or the host-owned practice car is available.</param>
/// <param name="Generation">Monotonic connection generation, independent of player and match identity.</param>
/// <param name="RetainedHost">Marks former authority for diagnostics; every disconnected arena player is retained until Return.</param>
/// <param name="PracticeCar">Host-driven vehicle with no authenticated subject or transport ownership.</param>
public sealed record SessionPlayer(ulong Id, string Name, bool Ready, bool Connected = true, ulong Generation = 1, bool RetainedHost = false, bool PracticeCar = false);
