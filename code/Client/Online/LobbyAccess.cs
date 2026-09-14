namespace Trackstorm.Client.Online;

/// <summary>Discoverable access policy; Locked is a lightweight game admission gate.</summary>
internal enum LobbyAccess
{
    /// <summary>Direct admission without an access code.</summary>
    Public,
    /// <summary>Discoverable lobby with a lightweight access code gate.</summary>
    Locked,
}
