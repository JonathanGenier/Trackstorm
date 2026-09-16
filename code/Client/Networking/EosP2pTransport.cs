using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Trackstorm.Client.Online;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Client.Networking;

/// <summary>Session-scoped EOS gateway. All SDK callbacks are deferred to bounded owner-thread polling.</summary>
internal sealed class EosP2pTransport : ITransportGateway
{
    /// <summary>Maximum received packets or queued native callbacks processed per poll.</summary>
    internal const int ReceiveBudget = 256;
    private readonly IEosP2p _native;
    private readonly Func<OnlineLobby?> _membership;
    private readonly OnlineProductUserId _local;
    private readonly TimeProvider _time;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly TransportConnections _connections = new();
    private readonly Dictionary<ulong, Peer> _peers = new();
    private readonly Queue<(OnlineProductUserId Identity, TransportConnectionState State, TransportDisconnectReason Reason, ulong Peer)> _callbacks = new();
    private readonly Queue<TransportConnectionChange> _changes = new();
    private readonly Queue<TransportMessage> _messages = new();
    private readonly byte[] _send = new byte[1170];
    private readonly byte[] _receive = new byte[1170];
    private readonly ulong[] _iteration = new ulong[7];
    private OnlineLobby? _session;
    private string? _credential;
    private bool _disposed;
    private bool _polling;
    private bool _overflow;
    private long _epoch;
    private OnlineProductUserId? _gameplayHost;

    /// <summary>Owns an authenticated native subscription and bounded session packet state.</summary>
    /// <param name="native">Owned native adapter; platform Tick remains external.</param>
    /// <param name="local">Authenticated local identity.</param>
    /// <param name="membership">Current lobby membership, absent after leave.</param>
    /// <param name="credential">Transient client access code, discarded after admission.</param>
    /// <param name="time">Monotonic clock for connection deadlines.</param>
    internal EosP2pTransport(IEosP2p native, OnlineProductUserId local, Func<OnlineLobby?> membership, string? credential = null, TimeProvider? time = null)
    {
        _native = native;
        _local = local;
        _membership = membership;
        _credential = credential;
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public event Action<TransportConnectionChange>? ConnectionChanged;
    /// <inheritdoc/>
    public string Name => "EOS P2P";
    /// <inheritdoc/>
    public TransportCapabilities Capabilities => TransportCapabilities.None;
    /// <inheritdoc/>
    public bool IsListening { get; private set; }
    /// <inheritdoc/>
    public IReadOnlyDictionary<ulong, TransportConnectionState> Connections => _connections.Snapshot;
    /// <inheritdoc/>
    public TransportConnectionState ConnectionState => _peers.Values.Any(p => _connections.IsConnected(p.Id)) ? TransportConnectionState.Connected : _peers.Count > 0 ? TransportConnectionState.Connecting : TransportConnectionState.Disconnected;
    /// <summary>Host admission callback, invoked with EOS-authenticated identity before Connected.</summary>
    internal Func<ulong, OnlineProductUserId, string?, bool>? Authorize { get; set; }
    /// <summary>Explicit gameplay routing target; provider ownership changes cannot replace it.</summary>
    internal OnlineProductUserId? GameplayHost => _gameplayHost;

    /// <summary>Successfully queued datagrams, including handshake and fragmentation.</summary>
    internal long SentPackets { get; private set; }
    /// <summary>Consumed native datagrams, including discarded traffic.</summary>
    internal long ReceivedPackets { get; private set; }
    /// <summary>Successfully queued reliable datagrams, including handshake.</summary>
    internal long ReliablePackets { get; private set; }
    /// <summary>Successfully queued bytes, including framing.</summary>
    internal long SentBytes { get; private set; }
    /// <summary>Largest successfully queued datagram.</summary>
    internal int PeakPacketBytes { get; private set; }
    /// <summary>Largest observed gateway poll cost, excluding platform Tick.</summary>
    internal double PeakPollMilliseconds { get; private set; }

    /// <inheritdoc/>
    public void Listen(TransportEndpoint endpoint)
    {
        Start(endpoint, true);
        IsListening = true;
    }

    /// <inheritdoc/>
    public ulong Connect(TransportEndpoint endpoint)
    {
        Start(endpoint, false);
        Peer peer = Admit(_gameplayHost!)!;
        if (!_native.Accept(peer.Identity) || !Packet(peer, 0, 0))
        {
            Close(peer.Id, TransportDisconnectReason.Failure);
        }

        return peer.Id;
    }

    /// <inheritdoc/>
    public void Disconnect(ulong peerId)
    {
        Check();
        Close(peerId, TransportDisconnectReason.LocalRequest);
    }

    /// <inheritdoc/>
    public TransportStatistics GetStatistics(ulong peerId)
    {
        Check();
        return default;
    }

    /// <inheritdoc/>
    public void ConfigureSimulation(NetworkSimulation simulation) => throw new NotSupportedException("EOS does not expose artificial network simulation. Use the Direct-IP development transport.");
    /// <inheritdoc/>
    public bool TryReceive(out TransportMessage message)
    {
        Check();
        return _messages.TryDequeue(out message);
    }

    /// <inheritdoc/>
    public void Send(TransportMessage message)
    {
        Check();
        if (!Enum.IsDefined(message.Delivery) || message.Payload.Length > EosPacketAssembly.MaximumPayload)
        {
            throw new ArgumentOutOfRangeException(nameof(message));
        }

        if (!_connections.IsConnected(message.RemotePeerId))
        {
            throw new InvalidOperationException("EOS peer is not connected.");
        }

        var peer = _peers[message.RemotePeerId];
        uint sequence = message.Delivery == TransportDelivery.Reliable ? ++peer.ReliableSequence : ++peer.UnreliableSequence;
        int count = Math.Max(1, (message.Payload.Length + EosPacketAssembly.FragmentBytes - 1) / EosPacketAssembly.FragmentBytes);
        for (int index = 0; index < count; index++)
        {
            Header(peer, 4, peer.RemoteNonce);
            BinaryPrimitives.WriteUInt32LittleEndian(_send.AsSpan(17), sequence);
            BinaryPrimitives.WriteInt32LittleEndian(_send.AsSpan(21), message.Payload.Length);
            BinaryPrimitives.WriteInt32LittleEndian(_send.AsSpan(25), index);
            int offset = index * EosPacketAssembly.FragmentBytes;
            int bytes = Math.Min(EosPacketAssembly.FragmentBytes, message.Payload.Length - offset);
            message.Payload.Span.Slice(offset, bytes).CopyTo(_send.AsSpan(EosPacketAssembly.Header));
            if (!SendPacket(peer, EosPacketAssembly.Header + bytes, message.Delivery))
            {
                Close(peer.Id, TransportDisconnectReason.Failure);
                throw new InvalidOperationException("EOS send queue rejected a packet. Leave and retry the session.");
            }
        }
    }

    /// <inheritdoc/>
    public void Poll()
    {
        Check();
        if (_polling)
        {
            throw new InvalidOperationException("Poll cannot be called recursively.");
        }

        _polling = true;
        long started = Stopwatch.GetTimestamp();
        try
        {
            var lobby = _membership();
            if (_session is not null && (lobby is null || !lobby.Compatible || !lobby.MemberIds.Contains(_local) || lobby.Id != _session.Id || lobby.Session != _session.Session))
            {
                Stop();
                return;
            }

            int count = _peers.Count;
            _peers.Keys.CopyTo(_iteration, 0);
            for (int i = 0; i < count; i++)
            {
                var peer = _peers[_iteration[i]];
                peer.Unreliable.Expire();
                if (_overflow || lobby?.MemberIds.Contains(peer.Identity) != true)
                {
                    Close(peer.Id, _overflow ? TransportDisconnectReason.ReceiveOverflow : TransportDisconnectReason.RemoteRequest);
                }
                else if (!_connections.IsConnected(peer.Id) && _time.GetElapsedTime(peer.Started).TotalSeconds >= 12)
                {
                    Close(peer.Id, TransportDisconnectReason.Timeout);
                }
            }

            _overflow = false;
            for (int i = 0; i < ReceiveBudget && _callbacks.TryDequeue(out var callback); i++)
            {
                if (callback.State != TransportConnectionState.Disconnected || callback.Peer == (FindPeer(callback.Identity)?.Id ?? 0))
                {
                    OnChange(callback.Identity, callback.State, callback.Reason);
                }
            }

            for (int i = 0; i < ReceiveBudget && _session is not null && ReadPacket(out var identity, out int length, out var delivery); i++)
            {
                ReceivedPackets++;
                var peer = FindPeer(identity);
                if (peer is null || length < 17 || length > _receive.Length)
                {
                    continue;
                }

                try
                {
                    Receive(peer, _receive.AsSpan(0, length), delivery);
                }
                catch (ArgumentException)
                {
                    Close(peer.Id, TransportDisconnectReason.Failure);
                }
            }

            int changes = _changes.Count;
            for (int i = 0; i < changes && !_disposed && _changes.TryDequeue(out var change); i++)
            {
                ConnectionChanged?.Invoke(change);
            }
        }
        finally
        {
            _polling = false;
            PeakPollMilliseconds = Math.Max(PeakPollMilliseconds, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    /// <inheritdoc/>
    public void Stop()
    {
        if (_disposed)
        {
            return;
        }

        Check();
        ++_epoch;
        int count = _peers.Count;
        _peers.Keys.CopyTo(_iteration, 0);
        for (int i = 0; i < count; i++)
        {
            Close(_iteration[i], TransportDisconnectReason.Shutdown);
        }

        _native.Stop();
        _callbacks.Clear();
        _messages.Clear();
        CryptographicOperations.ZeroMemory(_send);
        CryptographicOperations.ZeroMemory(_receive);
        _credential = null;
        _session = null;
        IsListening = false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _native.Dispose();
        _changes.Clear();
        ConnectionChanged = null;
        Authorize = null;
        _disposed = true;
    }

    /// <summary>Derives a bounded alphanumeric socket name from the lobby and session lifetime.</summary>
    /// <param name="lobby">Compatible active lobby.</param>
    /// <returns>Session-scoped socket name.</returns>
    internal static string SocketName(OnlineLobby lobby) => "TS" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lobby.Id + ":" + lobby.Session.ToString(System.Globalization.CultureInfo.InvariantCulture))))[..30];
    /// <summary>Builds the portable endpoint at the Client composition boundary.</summary>
    /// <param name="lobby">Compatible active lobby.</param>
    /// <param name="peer">Authenticated target identity.</param>
    /// <returns>Opaque peer/session target.</returns>
    internal static TransportEndpoint Endpoint(OnlineLobby lobby, OnlineProductUserId peer) => TransportEndpoint.PeerSession(peer.Value, SocketName(lobby));

    /// <summary>Changes only routing after the provider-neutral migration coordinator selects a candidate.</summary>
    /// <param name="host">Authenticated candidate already in this lobby.</param>
    /// <returns>New server peer, or zero for the candidate's listener.</returns>
    internal ulong RebindHost(OnlineProductUserId host)
    {
        Stop();
        _gameplayHost = host;
        var lobby = _membership() ?? throw new InvalidOperationException("Session membership unavailable.");
        if (host.Equals(_local))
        {
            Listen(Endpoint(lobby, host));
            return 0;
        }

        return Connect(Endpoint(lobby, host));
    }

    private void Start(TransportEndpoint endpoint, bool host)
    {
        Check();
        if (_session is not null)
        {
            throw new InvalidOperationException("Stop the current EOS session first.");
        }

        var lobby = _membership();
        _gameplayHost ??= lobby?.HostIdentity;
        if (lobby is null || !lobby.Compatible || !lobby.MemberIds.Contains(_local) || _gameplayHost is null || !lobby.MemberIds.Contains(_gameplayHost) || host != _gameplayHost.Equals(_local) || endpoint != Endpoint(lobby, host ? _local : _gameplayHost))
        {
            throw new ArgumentException("EOS endpoint must match the active compatible lobby and host.");
        }

        _session = lobby;
        _changes.Clear();
        long epoch = ++_epoch;
        try
        {
            _native.Start(SocketName(lobby), (identity, state, reason) =>
            {
                if (_disposed || epoch != _epoch)
                {
                    return;
                }

                if (_callbacks.Count >= ReceiveBudget)
                {
                    _overflow = true;
                    return;
                }

                _callbacks.Enqueue((identity, state, reason, FindPeer(identity)?.Id ?? 0));
            });
        }
        catch
        {
            _session = null;
            _native.Stop();
            throw;
        }
    }

    private Peer? Admit(OnlineProductUserId identity)
    {
        if (!_connections.TryAdmit(out ulong id))
        {
            _native.Close(identity);
            return null;
        }

        var peer = new Peer(id, identity, _time.GetTimestamp(), _time);
        _peers.Add(id, peer);
        _changes.Enqueue(new(id, TransportConnectionState.Connecting, TransportDisconnectReason.None, "Connecting through EOS…"));
        return peer;
    }

    private void OnChange(OnlineProductUserId identity, TransportConnectionState state, TransportDisconnectReason reason)
    {
        var peer = FindPeer(identity);
        if (state == TransportConnectionState.Connecting)
        {
            if (peer is not null)
            {
                return;
            }

            if (!IsListening || identity.Equals(_local) || _membership()?.MemberIds.Contains(identity) != true)
            {
                _native.Close(identity);
                return;
            }

            peer = Admit(identity);
            if (peer is not null && !_native.Accept(identity))
            {
                Close(peer.Id, TransportDisconnectReason.Failure);
            }
        }
        else if (peer is not null && state == TransportConnectionState.Disconnected)
        {
            Close(peer.Id, reason);
        }
        else if (peer is not null && state == TransportConnectionState.Connected && IsListening && !peer.Challenged)
        {
            peer.Challenged = true;
            if (!Packet(peer, 1, 0))
            {
                Close(peer.Id, TransportDisconnectReason.Failure);
            }
        }
    }

    private void Receive(Peer peer, ReadOnlySpan<byte> packet, TransportDelivery delivery)
    {
        byte type = packet[0];
        ulong remote = BinaryPrimitives.ReadUInt64LittleEndian(packet[1..]);
        ulong target = BinaryPrimitives.ReadUInt64LittleEndian(packet[9..]);
        if (type == 0)
        {
            return; // Triggers EOS connection negotiation only.
        }

        if (delivery == TransportDelivery.Reliable && type == 1 && !IsListening && !_connections.IsConnected(peer.Id) && peer.RemoteNonce == 0 && remote != 0)
        {
            peer.RemoteNonce = remote;
            Header(peer, 2, remote);
            int bytes = Encoding.UTF8.GetBytes(_credential ?? string.Empty, _send.AsSpan(17));
            _credential = null;
            bool sent = SendPacket(peer, 17 + bytes, TransportDelivery.Reliable);
            CryptographicOperations.ZeroMemory(_send.AsSpan(17, bytes));
            if (!sent)
            {
                Close(peer.Id, TransportDisconnectReason.Failure);
            }

            return;
        }

        if (target != peer.Nonce || remote == 0)
        {
            return;
        }

        if (delivery == TransportDelivery.Reliable && type == 2 && IsListening && peer.Challenged && !_connections.IsConnected(peer.Id))
        {
            if (packet.Length > 273 || Authorize?.Invoke(peer.Id, peer.Identity, Encoding.UTF8.GetString(packet[17..])) != true)
            {
                Close(peer.Id, TransportDisconnectReason.Failure);
                return;
            }

            peer.RemoteNonce = remote;
            if (Packet(peer, 3, remote))
            {
                Connected(peer);
            }
            else
            {
                Close(peer.Id, TransportDisconnectReason.Failure);
            }

            return;
        }

        if (remote != peer.RemoteNonce)
        {
            return;
        }

        if (type == 3 && delivery == TransportDelivery.Reliable && !IsListening)
        {
            _credential = null;
            Connected(peer);
            return;
        }

        if (type != 4 || !_connections.IsConnected(peer.Id) || packet.Length < EosPacketAssembly.Header)
        {
            return;
        }

        var payload = delivery == TransportDelivery.Reliable ? peer.Reliable.Receive(packet, true) : peer.Unreliable.Receive(packet);
        if (payload is null)
        {
            return;
        }

        if (_messages.Count >= ReceiveBudget)
        {
            Close(peer.Id, TransportDisconnectReason.ReceiveOverflow);
            return;
        }

        _messages.Enqueue(new(peer.Id, payload, delivery));
    }

    private void Connected(Peer peer)
    {
        if (_connections.TryTransition(peer.Id, TransportConnectionState.Connected))
        {
            _changes.Enqueue(new(peer.Id, TransportConnectionState.Connected, TransportDisconnectReason.None, "EOS gameplay connected."));
        }
    }

    private void Header(Peer peer, byte type, ulong target)
    {
        _send[0] = type;
        BinaryPrimitives.WriteUInt64LittleEndian(_send.AsSpan(1), peer.Nonce);
        BinaryPrimitives.WriteUInt64LittleEndian(_send.AsSpan(9), target);
    }

    private bool Packet(Peer peer, byte type, ulong target)
    {
        Header(peer, type, target);
        return SendPacket(peer, 17, TransportDelivery.Reliable);
    }

    private bool SendPacket(Peer peer, int length, TransportDelivery delivery)
    {
        if (!_native.Send(peer.Identity, new(_send, 0, length), delivery))
        {
            return false;
        }

        SentPackets++;
        if (delivery == TransportDelivery.Reliable)
        {
            ReliablePackets++;
        }

        SentBytes += length;
        PeakPacketBytes = Math.Max(PeakPacketBytes, length);
        return true;
    }

    private void Close(ulong id, TransportDisconnectReason reason)
    {
        if (!_peers.Remove(id, out var peer))
        {
            return;
        }

        _native.Close(peer.Identity);
        _connections.TryTransition(id, TransportConnectionState.Disconnected);
        _changes.Enqueue(new(id, TransportConnectionState.Disconnected, reason, $"EOS connection ended ({reason}). Check Internet access and P2P client policy, then rejoin the lobby."));
    }

    private void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _thread)
        {
            throw new InvalidOperationException("EOS transport must run on its owning thread.");
        }
    }

    private Peer? FindPeer(OnlineProductUserId? identity)
    {
        foreach (var peer in _peers.Values)
        {
            if (peer.Identity.Equals(identity))
            {
                return peer;
            }
        }

        return null;
    }

    private bool ReadPacket(out OnlineProductUserId? identity, out int length, out TransportDelivery delivery)
    {
        try
        {
            return _native.Receive(_receive, out identity, out length, out delivery);
        }
        catch (InvalidOperationException)
        {
            Stop();
            _changes.Enqueue(new(0, TransportConnectionState.Disconnected, TransportDisconnectReason.Failure, "EOS packet receive failed. Leave and retry login."));
            identity = null;
            length = 0;
            delivery = default;
            return false;
        }
    }

    private sealed class Peer(ulong id, OnlineProductUserId identity, long started, TimeProvider time)
    {
        internal ulong Id { get; } = id;
        internal OnlineProductUserId Identity { get; } = identity;
        internal long Started { get; } = started;
        internal ulong Nonce { get; } = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)) | 1;
        internal ulong RemoteNonce { get; set; }
        internal bool Challenged { get; set; }
        internal uint ReliableSequence { get; set; }
        internal uint UnreliableSequence { get; set; }
        internal EosPacketAssembly Reliable { get; } = new();
        internal EosUnreliableWindow Unreliable { get; } = new(time);
    }
}
