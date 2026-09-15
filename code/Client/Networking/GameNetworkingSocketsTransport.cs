using System.Diagnostics;
using System.Runtime.InteropServices;
using GnsSharp;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Client.Networking;

/// <summary>Windows x64 native transport. Use and dispose on the creating thread; Poll dispatches managed events.</summary>
public sealed class GameNetworkingSocketsTransport : ITransportGateway
{
    /// <summary>Bounds one message and prevents unbounded managed receive allocation.</summary>
    public const int MaximumPayloadBytes = 64 * 1024;

    private const int MaximumQueuedMessages = 256;
    private const int ListenerReleaseMilliseconds = 100;
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly TransportConnections _connections = new();
    private readonly Dictionary<ulong, HSteamNetConnection> _handles = [];
    private readonly Queue<SteamNetConnectionStatusChangedCallback_t> _nativeChanges = new();
    private readonly Queue<TransportConnectionChange> _changes = new();
    private readonly Queue<TransportMessage> _messages = new();
    private readonly FnSteamNetConnectionStatusChanged _callback;
    private readonly int _timeoutMilliseconds;
    private HSteamListenSocket _listener;
    private string? _listenAddress;
    private string? _closedListenAddress;
    private long _listenerClosedAt;
    private bool _disposed;
    private bool _polling;

    /// <summary>Initializes the native runtime with a bounded connection/idle timeout.</summary>
    /// <param name="timeoutMilliseconds">Initial and connected timeout from 1000 through 60000 ms.</param>
    public GameNetworkingSocketsTransport(int timeoutMilliseconds = 10000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMilliseconds, 1000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeoutMilliseconds, 60000);
        _timeoutMilliseconds = timeoutMilliseconds;
        _callback = OnNativeChange;
        GnsRuntime.Acquire();
    }

    /// <inheritdoc/>
    public event Action<TransportConnectionChange>? ConnectionChanged;

    /// <inheritdoc/>
    public bool IsListening => _listener != HSteamListenSocket.Invalid;

    /// <inheritdoc/>
    public IReadOnlyDictionary<ulong, TransportConnectionState> Connections => _connections.Snapshot;

    /// <inheritdoc/>
    public TransportConnectionState ConnectionState => Connections.Values.Contains(TransportConnectionState.Connected)
        ? TransportConnectionState.Connected
        : _connections.Count != 0 ? TransportConnectionState.Connecting : TransportConnectionState.Disconnected;

    /// <inheritdoc/>
    public string Name => "Direct-IP";
    /// <inheritdoc/>
    public TransportCapabilities Capabilities => TransportCapabilities.Ping | TransportCapabilities.ConnectionQuality | TransportCapabilities.NetworkSimulation;

    private static ISteamNetworkingSockets Sockets => ISteamNetworkingSockets.User!;

    /// <inheritdoc/>
    public void Listen(TransportEndpoint endpoint)
    {
        EnsureIdle();
        SteamNetworkingIPAddr nativeEndpoint = ParseAddress(DirectAddress(endpoint));
        string canonicalAddress = nativeEndpoint.ToString();
        SteamNetworkingConfigValue_t[] configuration = Configuration();
        _listener = GnsRuntime.CreateListener(in nativeEndpoint, configuration, out string? error);
        // GNS 1.6 closes the raw UDP socket on its service thread, not in RunCallbacks.
        // Yield only for a confirmed WSAEADDRINUSE immediately after our own same-endpoint close.
        // The deadline starts at Stop, so repeated Listen calls cannot extend the release window.
        while (!IsListening && canonicalAddress == _closedListenAddress
            && error == "Cannot create listen socket.  Failed to bind socket.  Error code 0x00002740."
            && Stopwatch.GetElapsedTime(_listenerClosedAt).TotalMilliseconds < ListenerReleaseMilliseconds)
        {
            Thread.Sleep(1);
            _listener = GnsRuntime.CreateListener(in nativeEndpoint, configuration, out error);
        }

        if (!IsListening)
        {
            throw new InvalidOperationException($"Could not listen on {nativeEndpoint}: {error ?? "Native listener creation returned an invalid handle without a diagnostic."}");
        }

        _listenAddress = canonicalAddress;
        _closedListenAddress = null;
    }

    /// <inheritdoc/>
    public ulong Connect(TransportEndpoint endpoint)
    {
        EnsureIdle();
        SteamNetworkingIPAddr nativeEndpoint = ParseAddress(DirectAddress(endpoint));
        HSteamNetConnection handle = Sockets.ConnectByIPAddress(in nativeEndpoint, Configuration());
        if (handle == HSteamNetConnection.Invalid)
        {
            throw new InvalidOperationException("Could not create the outgoing connection.");
        }

        return Admit(handle);
    }

    /// <inheritdoc/>
    public void Disconnect(ulong peerId)
    {
        EnsureThread();
        Close(peerId, TransportDisconnectReason.LocalRequest, "Local disconnect");
    }

    /// <inheritdoc/>
    public void Send(TransportMessage message)
    {
        EnsureThread();
        ESteamNetworkingSendType flags = GnsConversions.SendFlags(message.Delivery);
        if (message.Payload.Length > MaximumPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(message), "Payload exceeds 64 KiB.");
        }

        if (!_connections.IsConnected(message.RemotePeerId))
        {
            throw new InvalidOperationException("The destination peer is not connected.");
        }

        EResult result = Sockets.SendMessageToConnection(_handles[message.RemotePeerId], message.Payload.Span, flags);
        if (result != EResult.OK)
        {
            throw new InvalidOperationException($"Transport send failed: {result}");
        }
    }

    /// <inheritdoc/>
    public bool TryReceive(out TransportMessage message)
    {
        EnsureThread();
        return _messages.TryDequeue(out message);
    }

    /// <inheritdoc/>
    public void Poll()
    {
        EnsureThread();
        if (_polling)
        {
            throw new InvalidOperationException("Poll cannot be called recursively.");
        }

        _polling = true;
        try
        {
            Sockets.RunCallbacks();
            while (_nativeChanges.TryDequeue(out SteamNetConnectionStatusChangedCallback_t change))
            {
                ProcessChange(change);
            }

            foreach ((ulong peerId, HSteamNetConnection handle) in _handles.ToArray())
            {
                Receive(peerId, handle);
            }

            // User handlers never execute inside a reverse native callback.
            int count = _changes.Count;
            for (int i = 0; i < count && !_disposed && _changes.TryDequeue(out TransportConnectionChange change); i++)
            {
                ConnectionChanged?.Invoke(change);
            }
        }
        finally
        {
            _polling = false;
        }
    }

    /// <inheritdoc/>
    public TransportStatistics GetStatistics(ulong peerId)
    {
        EnsureThread();
        if (!_connections.IsConnected(peerId) || Sockets.GetConnectionRealTimeStatus(_handles[peerId], out SteamNetConnectionRealTimeStatus_t sample) != EResult.OK)
        {
            return default;
        }

        return TransportStatistics.FromSample(sample.Ping, sample.ConnectionQualityLocal, sample.ConnectionQualityRemote);
    }

    /// <inheritdoc/>
    public void ConfigureSimulation(NetworkSimulation simulation)
    {
        EnsureThread();
        ArgumentNullException.ThrowIfNull(simulation);
        ISteamNetworkingUtils utils = ISteamNetworkingUtils.User!;
        bool applied = utils.SetGlobalConfigValueInt32(ESteamNetworkingConfigValue.FakePacketLag_Send, simulation.LatencyMilliseconds);
        applied &= utils.SetGlobalConfigValueFloat(ESteamNetworkingConfigValue.FakePacketLoss_Send, simulation.LossPercent);
        applied &= utils.SetGlobalConfigValueFloat(ESteamNetworkingConfigValue.FakePacketReorder_Send, simulation.ReorderPercent);
        applied &= utils.SetGlobalConfigValueInt32(ESteamNetworkingConfigValue.FakePacketReorder_Time, simulation.ReorderMilliseconds);
        applied &= utils.SetGlobalConfigValueFloat(ESteamNetworkingConfigValue.FakePacketJitter_Send_Avg, simulation.JitterMilliseconds);
        applied &= utils.SetGlobalConfigValueFloat(ESteamNetworkingConfigValue.FakePacketJitter_Send_Max, simulation.JitterMilliseconds * 2);
        applied &= utils.SetGlobalConfigValueFloat(ESteamNetworkingConfigValue.FakePacketJitter_Send_Pct, simulation.JitterMilliseconds == 0 ? 0 : 100);
        if (!applied)
        {
            throw new InvalidOperationException("The native library rejected a network simulation option.");
        }
    }

    /// <inheritdoc/>
    public void Stop()
    {
        EnsureThread();
        foreach (ulong peer in _handles.Keys.ToArray())
        {
            Close(peer, TransportDisconnectReason.Shutdown, "Transport stopped");
        }

        if (IsListening)
        {
            if (!Sockets.CloseListenSocket(_listener))
            {
                throw new InvalidOperationException("Native listener close failed; listener ownership has been retained.");
            }

            _listener = HSteamListenSocket.Invalid;
            _closedListenAddress = _listenAddress;
            _listenerClosedAt = Stopwatch.GetTimestamp();
            _listenAddress = null;
        }

        // Drain queued callbacks while their delegate is rooted, before reuse or final release.
        Sockets.RunCallbacks();
        _nativeChanges.Clear();
        _messages.Clear();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _changes.Clear();
        ConnectionChanged = null;
        _disposed = true;
        GnsRuntime.Release();
    }

    private static string DirectAddress(TransportEndpoint endpoint) => endpoint.Session is null ? endpoint.Address : throw new ArgumentException("Direct-IP requires an IP endpoint.", nameof(endpoint));
    private static SteamNetworkingIPAddr ParseAddress(string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        SteamNetworkingIPAddr endpoint = default;
        if (!endpoint.ParseString(address) || endpoint.Port == 0)
        {
            throw new ArgumentException("Use a numeric IP address and port, for example 127.0.0.1:27020 or [::1]:27020.", nameof(address));
        }

        return endpoint;
    }

    private SteamNetworkingConfigValue_t[] Configuration()
    {
        SteamNetworkingConfigValue_t[] values = new SteamNetworkingConfigValue_t[3];
        values[0].SetPtr(ESteamNetworkingConfigValue.Callback_ConnectionStatusChanged, Marshal.GetFunctionPointerForDelegate(_callback));
        values[1].SetInt32(ESteamNetworkingConfigValue.TimeoutInitial, _timeoutMilliseconds);
        values[2].SetInt32(ESteamNetworkingConfigValue.TimeoutConnected, _timeoutMilliseconds);
        return values;
    }

    private void OnNativeChange(ref SteamNetConnectionStatusChangedCallback_t change) => _nativeChanges.Enqueue(change);

    private ulong Admit(HSteamNetConnection handle)
    {
        if (!_connections.TryAdmit(out ulong peerId))
        {
            Sockets.CloseConnection(handle, GnsConversions.SessionFullCode, "Session full", false);
            return 0;
        }

        _handles.Add(peerId, handle);
        _changes.Enqueue(new(peerId, TransportConnectionState.Connecting, TransportDisconnectReason.None, string.Empty));
        return peerId;
    }

    private void ProcessChange(SteamNetConnectionStatusChangedCallback_t change)
    {
        ulong peer = _handles.FirstOrDefault(pair => pair.Value == change.Conn).Key;
        if (peer == 0 && change.Info.State == ESteamNetworkingConnectionState.Connecting && IsListening && change.Info.ListenSocket == _listener)
        {
            peer = Admit(change.Conn);
            if (peer != 0 && Sockets.AcceptConnection(change.Conn) != EResult.OK)
            {
                Close(peer, TransportDisconnectReason.Failure, "Native accept failed");
            }

            return;
        }

        if (peer == 0)
        {
            return;
        }

        if (change.Info.State == ESteamNetworkingConnectionState.Connected && _connections.TryTransition(peer, TransportConnectionState.Connected))
        {
            _changes.Enqueue(new(peer, TransportConnectionState.Connected, TransportDisconnectReason.None, string.Empty));
        }
        else if (change.Info.State is ESteamNetworkingConnectionState.ClosedByPeer or ESteamNetworkingConnectionState.ProblemDetectedLocally)
        {
            Close(peer, GnsConversions.DisconnectReason((int)change.Info.EndReason, change.Info.State == ESteamNetworkingConnectionState.ClosedByPeer), change.Info.EndDebug ?? string.Empty);
        }
    }

    private void Receive(ulong peer, HSteamNetConnection handle)
    {
        Span<IntPtr> buffer = stackalloc IntPtr[1];
        for (int i = 0; i < 32; i++)
        {
            int received = Sockets.ReceiveMessagesOnConnection(handle, buffer);
            if (received <= 0)
            {
                return;
            }

            try
            {
                SteamNetworkingMessage_t native = Marshal.PtrToStructure<SteamNetworkingMessage_t>(buffer[0]);
                if (native.Size < 0 || native.Size > MaximumPayloadBytes || _messages.Count >= MaximumQueuedMessages)
                {
                    Close(peer, TransportDisconnectReason.ReceiveOverflow, "Receive capacity exceeded", GnsConversions.OverflowCode);
                    return;
                }

                byte[] payload = new byte[native.Size];
                Marshal.Copy(native.Data, payload, 0, payload.Length);
                TransportDelivery delivery = (native.Flags & ESteamNetworkingSendType.Reliable) != 0 ? TransportDelivery.Reliable : TransportDelivery.Unreliable;
                _messages.Enqueue(new(peer, payload, delivery));
            }
            finally
            {
                SteamNetworkingMessage_t.Release(buffer[0]);
            }
        }
    }

    private void Close(ulong peer, TransportDisconnectReason reason, string detail, int nativeReason = 1000)
    {
        if (!_handles.Remove(peer, out HSteamNetConnection handle))
        {
            return;
        }

        Sockets.CloseConnection(handle, nativeReason, detail, false);
        _connections.TryTransition(peer, TransportConnectionState.Disconnected);
        _changes.Enqueue(new(peer, TransportConnectionState.Disconnected, reason, detail));
    }

    private void EnsureIdle()
    {
        EnsureThread();
        if (IsListening || _connections.Count != 0)
        {
            throw new InvalidOperationException("Stop the current session before hosting or joining another.");
        }
    }

    private void EnsureThread()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_threadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Transport operations must run on the creating thread.");
        }
    }
}
