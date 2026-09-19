namespace Trackstorm.Client.Networking;

/// <summary>Application navigation, separate from authoritative match phases.</summary>
internal enum ApplicationStage
{
    /// <summary>Frontend entry and navigation.</summary>
    MainMenu,
    /// <summary>Online discovery inside the persistent frontend.</summary>
    LobbyBrowser,
    /// <summary>Asynchronous membership and authoritative admission.</summary>
    Admission,
    /// <summary>Admitted multiplayer staging.</summary>
    Lobby,
    /// <summary>Selected resources and authoritative synchronization.</summary>
    MatchLoader,
    /// <summary>Completed application handoff.</summary>
    GameLoop,
    /// <summary>Retained authoritative Finished handoff and post-match destinations.</summary>
    Podium,
    /// <summary>Explicit session/membership cleanup before returning to MenuShell.</summary>
    Leaving,
}
