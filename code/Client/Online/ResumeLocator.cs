namespace Trackstorm.Client.Online;

/// <summary>Match-resume routing hint. Possession grants no authority; EOS identity is authenticated again.</summary>
/// <param name="Lobby">EOS lobby locator.</param>
/// <param name="Session">Expected stable Trackstorm session.</param>
/// <param name="Player">Previously assigned gameplay identity.</param>
/// <param name="Generation">Last acknowledged connection generation.</param>
/// <param name="Identity">Local online identity to prevent accidental account switching.</param>
/// <param name="AuthorityEpoch">Last established Trackstorm authority epoch.</param>
/// <param name="Host">Expected authenticated host.</param>
internal sealed record ResumeLocator(string Lobby, ulong Session, ulong Player, ulong Generation, string Identity, ulong AuthorityEpoch, string Host);
