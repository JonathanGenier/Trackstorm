namespace Trackstorm.Core.Networking.Transport;

/// <summary>
/// Describes the native transport connection lifecycle without exposing a transport API.
/// </summary>
public enum TransportConnectionState
{
    /// <summary>
    /// No transport connection is available.
    /// </summary>
    Disconnected,

    /// <summary>
    /// The transport is establishing a connection.
    /// </summary>
    Connecting,

    /// <summary>
    /// The transport can send and receive messages.
    /// </summary>
    Connected,
}
