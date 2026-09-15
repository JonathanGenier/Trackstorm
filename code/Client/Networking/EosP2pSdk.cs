using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using Trackstorm.Client.Online;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Client.Networking;

/// <summary>Uses the authenticated platform's P2P interface without ticking or releasing that platform.</summary>
internal sealed class EosP2pSdk(P2PInterface p2p, ProductUserId local) : IEosP2p
{
    private readonly Dictionary<OnlineProductUserId, ProductUserId> _peers = new();
    private SocketId _socket;
    private SocketId _receivedSocket;
    private ProductUserId _receivedPeer = new();
    private ulong _requests;
    private ulong _established;
    private ulong _closed;
    private ulong _interrupted;
    private ulong _queueFull;
    private long _epoch;
    private bool _disposed;

    /// <inheritdoc/>
    public void Start(string socket, Action<OnlineProductUserId, TransportConnectionState, TransportDisconnectReason> changed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Stop();
        _socket = new SocketId { SocketName = socket };
        var queues = new SetPacketQueueSizeOptions { IncomingPacketQueueMaxSizeBytes = 1024 * 1024, OutgoingPacketQueueMaxSizeBytes = 1024 * 1024 };
        if (p2p.SetPacketQueueSize(ref queues) != Result.Success)
        {
            throw new InvalidOperationException("EOS P2P packet queue limits could not be configured. Retry login.");
        }

        long epoch = ++_epoch;
        var full = default(AddNotifyIncomingPacketQueueFullOptions);
        _queueFull = p2p.AddNotifyIncomingPacketQueueFull(ref full, this, (ref OnIncomingPacketQueueFullInfo info) =>
        {
            if (epoch != _epoch)
            {
                return;
            }

            foreach (var peer in _peers.Keys)
            {
                changed(peer, TransportConnectionState.Disconnected, TransportDisconnectReason.ReceiveOverflow);
            }
        });
        var requests = new AddNotifyPeerConnectionRequestOptions { LocalUserId = local, SocketId = _socket };
        _requests = p2p.AddNotifyPeerConnectionRequest(ref requests, this, (ref OnIncomingConnectionRequestInfo info) =>
        {
            if (epoch == _epoch)
            {
                changed(new(info.RemoteUserId.ToString()), TransportConnectionState.Connecting, TransportDisconnectReason.None);
            }
        });
        var established = new AddNotifyPeerConnectionEstablishedOptions { LocalUserId = local, SocketId = _socket };
        _established = p2p.AddNotifyPeerConnectionEstablished(ref established, this, (ref OnPeerConnectionEstablishedInfo info) =>
        {
            if (epoch == _epoch)
            {
                changed(new(info.RemoteUserId.ToString()), TransportConnectionState.Connected, TransportDisconnectReason.None);
            }
        });
        var closed = new AddNotifyPeerConnectionClosedOptions { LocalUserId = local, SocketId = _socket };
        _closed = p2p.AddNotifyPeerConnectionClosed(ref closed, this, (ref OnRemoteConnectionClosedInfo info) =>
        {
            // Local close was already applied by the gateway; a delayed echo must not close a subsequent connection.
            if (epoch == _epoch && info.Reason != ConnectionClosedReason.ClosedByLocalUser)
            {
                changed(new(info.RemoteUserId.ToString()), TransportConnectionState.Disconnected, Reason(info.Reason));
            }
        });
        var interrupted = new AddNotifyPeerConnectionInterruptedOptions { LocalUserId = local, SocketId = _socket };
        _interrupted = p2p.AddNotifyPeerConnectionInterrupted(ref interrupted, this, (ref OnPeerConnectionInterruptedInfo info) =>
        {
            if (epoch == _epoch)
            {
                changed(new(info.RemoteUserId.ToString()), TransportConnectionState.Disconnected, TransportDisconnectReason.Timeout);
            }
        });
        if (_requests == 0 || _established == 0 || _closed == 0 || _interrupted == 0 || _queueFull == 0)
        {
            Stop();
            throw new InvalidOperationException("EOS P2P notifications failed. Retry login and check the P2P client policy.");
        }
    }

    /// <inheritdoc/>
    public bool Accept(OnlineProductUserId peer)
    {
        var options = new AcceptConnectionOptions { LocalUserId = local, RemoteUserId = User(peer), SocketId = _socket };
        return p2p.AcceptConnection(ref options) == Result.Success;
    }

    /// <inheritdoc/>
    public bool Send(OnlineProductUserId peer, ArraySegment<byte> data, TransportDelivery delivery)
    {
        var options = new SendPacketOptions
        {
            LocalUserId = local,
            RemoteUserId = User(peer),
            SocketId = _socket,
            Channel = delivery == TransportDelivery.Reliable ? (byte)0 : (byte)1,
            Data = data,
            Reliability = Reliability(delivery),
            AllowDelayedDelivery = true,
            DisableAutoAcceptConnection = true,
        };
        return p2p.SendPacket(ref options) == Result.Success;
    }

    /// <inheritdoc/>
    public bool Receive(byte[] buffer, out OnlineProductUserId? peer, out int length, out TransportDelivery delivery)
    {
        var options = new ReceivePacketOptions { LocalUserId = local, MaxDataSizeBytes = (uint)buffer.Length };
        Result result = p2p.ReceivePacket(ref options, ref _receivedPeer, ref _receivedSocket, out byte channel, buffer, out uint size);
        peer = null;
        length = (int)size;
        delivery = channel == 0 ? TransportDelivery.Reliable : TransportDelivery.Unreliable;
        if (result == Result.NotFound)
        {
            return false;
        }

        if (result != Result.Success)
        {
            throw new InvalidOperationException("EOS P2P receive failed. Leave and retry login.");
        }

        if (channel > 1 || _receivedSocket.SocketName != _socket.SocketName)
        {
            return true;
        }

        foreach (var entry in _peers)
        {
            if (entry.Value.Equals(_receivedPeer))
            {
                peer = entry.Key;
                break;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public void Close(OnlineProductUserId peer)
    {
        if (_disposed)
        {
            return;
        }

        var options = new CloseConnectionOptions { LocalUserId = local, RemoteUserId = ProductUserId.FromString(peer.Value), SocketId = _socket };
        p2p.CloseConnection(ref options);
        var clear = new ClearPacketQueueOptions { LocalUserId = local, RemoteUserId = options.RemoteUserId, SocketId = _socket };
        p2p.ClearPacketQueue(ref clear);
        _peers.Remove(peer);
    }

    /// <inheritdoc/>
    public void Stop()
    {
        if (_disposed)
        {
            return;
        }

        ++_epoch;
        if (_queueFull != 0)
        {
            p2p.RemoveNotifyIncomingPacketQueueFull(_queueFull);
        }

        if (_requests != 0)
        {
            p2p.RemoveNotifyPeerConnectionRequest(_requests);
        }

        if (_established != 0)
        {
            p2p.RemoveNotifyPeerConnectionEstablished(_established);
        }

        if (_closed != 0)
        {
            p2p.RemoveNotifyPeerConnectionClosed(_closed);
        }

        if (_interrupted != 0)
        {
            p2p.RemoveNotifyPeerConnectionInterrupted(_interrupted);
        }

        if (_requests != 0)
        {
            var close = new CloseConnectionsOptions { LocalUserId = local, SocketId = _socket };
            p2p.CloseConnections(ref close);
        }

        _requests = _established = _closed = _interrupted = _queueFull = 0;
        _peers.Clear();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Stop();
        _disposed = true;
    }

    /// <summary>Maps independent transport delivery guarantees to official EOS modes.</summary>
    /// <param name="delivery">Validated portable delivery mode.</param>
    /// <returns>Native delivery mode.</returns>
    internal static PacketReliability Reliability(TransportDelivery delivery) => delivery switch
    {
        TransportDelivery.Reliable => PacketReliability.ReliableOrdered,
        TransportDelivery.Unreliable => PacketReliability.UnreliableUnordered,
        _ => throw new ArgumentOutOfRangeException(nameof(delivery)),
    };

    /// <summary>Maps native closure codes to stable transport reasons.</summary>
    /// <param name="reason">Native closure reason.</param>
    /// <returns>Portable reason, with unknown codes treated as failures.</returns>
    internal static TransportDisconnectReason Reason(ConnectionClosedReason reason) => reason switch
    {
        ConnectionClosedReason.ClosedByLocalUser => TransportDisconnectReason.LocalRequest,
        ConnectionClosedReason.ClosedByPeer => TransportDisconnectReason.RemoteRequest,
        ConnectionClosedReason.TimedOut or ConnectionClosedReason.ConnectionClosed => TransportDisconnectReason.Timeout,
        ConnectionClosedReason.TooManyConnections => TransportDisconnectReason.SessionFull,
        _ => TransportDisconnectReason.Failure,
    };

    private ProductUserId User(OnlineProductUserId peer)
    {
        if (!_peers.TryGetValue(peer, out var user))
        {
            _peers.Add(peer, user = ProductUserId.FromString(peer.Value));
        }

        return user;
    }
}
