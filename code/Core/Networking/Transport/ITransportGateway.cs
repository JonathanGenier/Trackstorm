namespace Trackstorm.Core.Networking.Transport;

/// <summary>
/// Defines the engine-independent boundary implemented by a native transport adapter outside Core.
/// </summary>
public interface ITransportGateway
{
    /// <summary>
    /// Gets the current transport connection state.
    /// </summary>
    TransportConnectionState ConnectionState { get; }

    /// <summary>
    /// Sends one opaque payload to its specified remote peer.
    /// </summary>
    /// <param name="message">The transport-facing message.</param>
    void Send(TransportMessage message);

    /// <summary>
    /// Tries to receive one opaque payload and its sending peer.
    /// </summary>
    /// <param name="message">The received message when available.</param>
    /// <returns><see langword="true"/> when a message was received; otherwise, <see langword="false"/>.</returns>
    bool TryReceive(out TransportMessage message);
}
