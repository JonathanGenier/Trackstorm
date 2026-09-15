using Trackstorm.Client.Online;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Client.Networking;

/// <summary>Client-only native packet seam. The platform owns Tick; the gateway owns this subscription lifetime.</summary>
internal interface IEosP2p : IDisposable
{
    /// <summary>Registers callbacks for one session socket; consumer only enqueues bounded work.</summary>
    /// <param name="socket">Session-scoped socket name.</param>
    /// <param name="changed">Receives authenticated remote identity and portable lifecycle state.</param>
    void Start(string socket, Action<OnlineProductUserId, TransportConnectionState, TransportDisconnectReason> changed);
    /// <summary>Explicitly accepts a validated online member.</summary>
    /// <param name="peer">Authenticated online identity.</param>
    /// <returns>Whether EOS accepted the request.</returns>
    bool Accept(OnlineProductUserId peer);
    /// <summary>Queues one datagram without transferring ownership of its buffer.</summary>
    /// <param name="peer">Authenticated destination.</param>
    /// <param name="data">Buffer valid for the duration of this call.</param>
    /// <param name="delivery">Requested native reliability.</param>
    /// <returns>Whether EOS accepted the packet.</returns>
    bool Send(OnlineProductUserId peer, ArraySegment<byte> data, TransportDelivery delivery);
    /// <summary>Reads one packet into reusable storage; unrelated traffic yields a null peer.</summary>
    /// <param name="buffer">Caller-owned storage of at least 1170 bytes.</param>
    /// <param name="peer">Authenticated sender, null for unrelated traffic.</param>
    /// <param name="length">Received byte count.</param>
    /// <param name="delivery">Delivery channel.</param>
    /// <returns>Whether a native packet was consumed.</returns>
    bool Receive(byte[] buffer, out OnlineProductUserId? peer, out int length, out TransportDelivery delivery);
    /// <summary>Closes a peer and discards cached routing state.</summary>
    /// <param name="peer">Online identity to disconnect.</param>
    void Close(OnlineProductUserId peer);
    /// <summary>Removes notifications and closes all session connections before platform release.</summary>
    void Stop();
}
