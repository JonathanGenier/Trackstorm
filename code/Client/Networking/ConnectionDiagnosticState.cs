namespace Trackstorm.Client.Networking;

/// <summary>Player-facing connection lifecycle, independent of native providers.</summary>
internal enum ConnectionDiagnosticState
{
    /// <summary>No active connection.</summary>
    Disconnected,
    /// <summary>Awaiting initial transport or session admission.</summary>
    Connecting,
    /// <summary>Recovering transport, admission or arena state.</summary>
    Reconnecting,
    /// <summary>Admitted on the current connection.</summary>
    Connected,
    /// <summary>No upstream peer to measure, such as a host.</summary>
    Unavailable
}
