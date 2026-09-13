namespace Trackstorm.Core.Networking.Transport;

/// <summary>Stable reasons independent of a native library's error numbers.</summary>
public enum TransportDisconnectReason
{
    /// <summary>No failure or disconnection.</summary>
    None,
    /// <summary>Closed by the local caller.</summary>
    LocalRequest,
    /// <summary>Closed by the remote caller.</summary>
    RemoteRequest,
    /// <summary>The host has no available player slots.</summary>
    SessionFull,
    /// <summary>The remote endpoint did not respond before the deadline.</summary>
    Timeout,
    /// <summary>The transport could not establish or maintain the connection.</summary>
    Failure,
    /// <summary>The local transport is shutting down.</summary>
    Shutdown,
    /// <summary>The remote endpoint exceeded the bounded receive capacity.</summary>
    ReceiveOverflow,
}
