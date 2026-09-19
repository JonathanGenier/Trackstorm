namespace Trackstorm.Client.Online;

/// <summary>Read-only result for locating retained-session metadata without joining its lobby.</summary>
/// <param name="Lobby">Fresh compatible candidate metadata, or absence.</param>
/// <param name="Failure">Service/search failure, or absence when the old lobby is simply missing.</param>
internal sealed record OnlineLobbyLookup(OnlineLobby? Lobby, string? Failure);
