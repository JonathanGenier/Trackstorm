namespace Trackstorm.Core.Networking.Transport;

/// <summary>
/// Defines the engine-independent boundary implemented by a native transport adapter outside Core.
/// </summary>
public interface ITransportGateway : IDisposable
{
    /// <summary>Raised from Poll after native callbacks return; failures include a stable reason.</summary>
    event Action<TransportConnectionChange>? ConnectionChanged;

    /// <summary>Gets whether this gateway is listening for incoming peers.</summary>
    bool IsListening { get; }

    /// <summary>Gets a detached snapshot of connecting and connected peers.</summary>
    IReadOnlyDictionary<ulong, TransportConnectionState> Connections { get; }

    /// <summary>Gets the aggregate peer state; IsListening independently reports host availability.</summary>
    TransportConnectionState ConnectionState { get; }

    /// <summary>Gets the active adapter's presentation-safe name.</summary>
    string Name => "Unknown transport";

    /// <summary>Gets optional capabilities; unavailable metrics remain null.</summary>
    TransportCapabilities Capabilities => TransportCapabilities.None;

    /// <summary>Starts a host using the selected provider's endpoint.</summary>
    /// <param name="endpoint">Local endpoint and optional session context.</param>
    void Listen(TransportEndpoint endpoint);

    /// <summary>Starts a client connection through the selected provider, returning a local peer ID.</summary>
    /// <param name="endpoint">Remote endpoint and optional session context.</param>
    /// <returns>The new local peer identity.</returns>
    ulong Connect(TransportEndpoint endpoint);

    /// <summary>Closes one peer and releases its slot.</summary>
    /// <param name="peerId">The local identity of the peer to close.</param>
    void Disconnect(ulong peerId);

    /// <summary>Pumps native callbacks, bounded receives and lifecycle events on the owning thread.</summary>
    void Poll();

    /// <summary>Stops the session and releases all peers and the listener; the gateway can be reused.</summary>
    void Stop();

    /// <summary>Returns the current diagnostics or unavailable values for an inactive peer.</summary>
    /// <param name="peerId">The local peer identity.</param>
    /// <returns>Current sampled statistics with null for unavailable values.</returns>
    TransportStatistics GetStatistics(ulong peerId);

    /// <summary>Configures process-wide outbound network simulation. All gateways in this process are affected.</summary>
    /// <param name="simulation">The validated packet simulation settings.</param>
    void ConfigureSimulation(NetworkSimulation simulation);

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
