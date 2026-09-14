namespace Trackstorm.Client.Online;

/// <summary>Whitelisted browser presentation, excluding identity and credential material.</summary>
internal sealed record LobbyRow(string Id, string Name, int Members, int Capacity, LobbyAccess Access, bool Joinable);
