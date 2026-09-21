namespace Trackstorm.Client.Networking;

/// <summary>Podium requests interpreted by Application Flow, never by the scene itself.</summary>
internal enum PostMatchDestination
{
    Rematch,
    Lobby,
    EndMatch,
    MainMenu,
    Quit,
}
