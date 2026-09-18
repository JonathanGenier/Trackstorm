namespace Trackstorm.Core.Sessions;

/// <summary>Match-lifetime display identity retained after its admission slot is released.</summary>
/// <param name="Id">Original stable player identity.</param>
/// <param name="Name">Authoritative sanitized display name.</param>
public sealed record MatchParticipant(ulong Id, string Name);
