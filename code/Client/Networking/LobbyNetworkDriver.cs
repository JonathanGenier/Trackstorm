using Trackstorm.Core.Events;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Networking;

/// <summary>Routes reliable session intents and publications over the existing caller-owned gateway.</summary>
internal sealed class LobbyNetworkDriver
{
    private readonly ITransportGateway _gateway;
    private readonly string _name;
    private readonly GameVersion _version;
    private readonly Func<ulong, bool>? _admission;
    private readonly Func<ulong, string?>? _identity;
    private readonly ulong _expectedSession;
    private readonly LobbyReplica _replica = new();
    private readonly Queue<TransportMessage> _pendingGameplay = new();
    private readonly EventStream _clientEvents = new();
    private readonly Dictionary<ulong, ulong> _eventSent = new();
    private readonly Dictionary<ulong, double> _versionRejected = new();
    private bool _joined;
    private ulong _published;
    private double _joiningSeconds;
    private double _latencySeconds;
    private double _seconds;
    private double? _interruptedAt;
    private double _nextAttempt;
    private ulong _resumePlayer;
    private ulong _resumeGeneration;
    private double? _leaveAt;
    private bool _left;
    private string _loggedResume = string.Empty;
    private bool _loggedFailure;
    private int _loggedRejections;
    private double _nextRejectionReport;

    /// <summary>Creates a host lobby or a client waiting for admission.</summary>
    /// <param name="gateway">Existing transport.</param>
    /// <param name="session">Host lifetime, zero on a client.</param>
    /// <param name="serverPeer">Client's actual server connection.</param>
    /// <param name="name">Local name request.</param>
    /// <param name="admission">Optional online admission gate; direct-IP retains development admission.</param>
    /// <param name="expectedSession">Online clients require this discovered session lifetime; zero retains development behavior.</param>
    /// <param name="identity">Trusted authenticated subject resolver, absent for unauthenticated Direct-IP.</param>
    /// <param name="graceTicks">Host reservation duration in 60 Hz ticks.</param>
    /// <param name="gameVersion">Runtime build identity; defaults to the canonical provider.</param>
    internal LobbyNetworkDriver(ITransportGateway gateway, ulong session, ulong serverPeer, string name, Func<ulong, bool>? admission = null, ulong expectedSession = 0, Func<ulong, string?>? identity = null, ulong graceTicks = 1800, GameVersion? gameVersion = null)
    {
        _gateway = gateway;
        _name = name;
        _version = gameVersion ?? GameVersion.Current;
        _admission = admission;
        _identity = identity;
        _expectedSession = expectedSession;
        ServerPeer = serverPeer;
        _clientEvents.PlayerName = id => State?.Players.SingleOrDefault(player => player.Id == id)?.Name ?? $"Player {id}";
        if (session != 0)
        {
            Authority = new LobbyAuthority(session, name, graceTicks, _version);
        }
    }

    /// <summary>Host-owned rules; absent on clients.</summary>
    internal LobbyAuthority? Authority { get; }
    /// <summary>Structured current-session history; replicated outcomes never originate on a client.</summary>
    internal EventStream Events => Authority?.Events ?? _clientEvents;

    /// <summary>Latest complete authoritative state.</summary>
    internal LobbySnapshot? State => Authority?.State ?? _replica.State;
    /// <summary>Session-stable local identity, zero before admission.</summary>
    internal ulong LocalPlayerId => Authority is null ? (_replica.PlayerId == 0 ? _resumePlayer : _replica.PlayerId) : 1;
    /// <summary>Actual host transport peer on a client.</summary>
    internal ulong ServerPeer { get; private set; }
    /// <summary>Transport-specific re-establishment, retaining this driver and logical session.</summary>
    internal Func<ulong>? Reconnect { get; set; }
    /// <summary>Whether controls are suspended pending a fresh connection and state boundary.</summary>
    internal bool Reconnecting => _interruptedAt.HasValue;
    /// <summary>Stable player-facing reconnect state.</summary>
    internal string ResumeStatus { get; private set; } = string.Empty;
    /// <summary>Whether an arena resume must receive its complete checkpoint before prediction.</summary>
    internal bool NeedsArenaCheckpoint { get; set; }
    /// <summary>True after the host removed this player or the bounded leave exchange elapsed.</summary>
    internal bool LeaveComplete => _left || (_leaveAt.HasValue && _seconds - _leaveAt.Value >= 1);
    /// <summary>Current authenticated local stream generation.</summary>
    internal ulong Generation => State?.Players.SingleOrDefault(player => player.Id == LocalPlayerId)?.Generation ?? _resumeGeneration;
    /// <summary>Terminal connection failure, rendered by the session UI.</summary>
    internal string Failure { get; private set; } = string.Empty;
    /// <summary>Rejected malformed or unauthorized intents/publications.</summary>
    internal int RejectedPackets { get; private set; }
    /// <summary>Presentation-only host RTT samples keyed by session player identity.</summary>
    internal PlayerLatency Latency { get; } = new();

    /// <summary>Pumps the single gateway and optionally routes non-lobby packets into the active vehicle driver.</summary>
    /// <param name="seconds">Elapsed monotonic time for admission timeout.</param>
    /// <param name="vehicleMessage">Consumer for an active arena only.</param>
    internal void Pump(double seconds, Action<TransportMessage>? vehicleMessage = null)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        _latencySeconds += seconds;
        _seconds += seconds;
        Events.AdvanceTime((ulong)(_seconds * 1000));
        Authority?.AdvanceTime((ulong)(_seconds * 60));
        _gateway.Poll();
        if (Authority is not null)
        {
            foreach (var rejected in _versionRejected.ToArray())
            {
                if (_seconds >= rejected.Value)
                {
                    _gateway.Disconnect(rejected.Key);
                    _versionRejected.Remove(rejected.Key);
                }
            }

            foreach (ulong peer in Authority.Peers.Keys)
            {
                if (!_gateway.Connections.TryGetValue(peer, out var connection) || connection != TransportConnectionState.Connected)
                {
                    Authority.Disconnect(peer);
                }
            }
        }
        else if (Failure.Length == 0)
        {
            _joiningSeconds += seconds;
            if (_leaveAt.HasValue)
            {
                // Keep pumping the reliable acknowledgement without starting another connection.
            }
            else if (!_gateway.Connections.TryGetValue(ServerPeer, out var connection) || connection == TransportConnectionState.Disconnected)
            {
                Latency.Clear();
                if ((State is not null || _resumePlayer != 0) && Reconnect is not null && Failure.Length == 0)
                {
                    if (!_interruptedAt.HasValue)
                    {
                        _interruptedAt = _seconds;
                        _nextAttempt = _seconds + 1;
                        ResumeStatus = "Connection interrupted";
                        _pendingGameplay.Clear();
                    }

                    if (_seconds - _interruptedAt.Value >= (State?.GraceTicks ?? 1800) / 60.0)
                    {
                        Failure = "Grace expired. Resume is no longer available; choose a lobby to play again.";
                        ResumeStatus = "Grace expired";
                    }
                    else if (_seconds >= _nextAttempt)
                    {
                        ResumeStatus = "Reconnecting";
                        _nextAttempt = _seconds + 2;
                        try
                        {
                            ServerPeer = Reconnect();
                            _joined = false;
                            NeedsArenaCheckpoint = false;
                            _pendingGameplay.Clear();
                        }
                        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
                        {
                            ResumeStatus = "Session unavailable — retrying";
                        }
                    }
                }
                else
                {
                    Failure = "Host disconnected or refused admission. Leave and join a new lobby.";
                }
            }
            else if (!_joined && connection == TransportConnectionState.Connected)
            {
                byte[] request = Reconnecting
                    ? LobbyCodec.EncodeResume(State?.Session ?? _expectedSession, LocalPlayerId, Generation, _version.ToString())
                    : LobbyCodec.EncodeCommand(LobbyCommand.Join, null, name: _name, gameVersion: _version.ToString());
                Send(ServerPeer, request);
                _joined = true;
            }

            if (_interruptedAt.HasValue && _seconds - _interruptedAt.Value >= (State?.GraceTicks ?? 1800) / 60.0)
            {
                Failure = "Grace expired. Leave and choose a lobby.";
                ResumeStatus = "Grace expired";
                _gateway.Disconnect(ServerPeer);
            }

            if (State is null && !Reconnecting && _joiningSeconds > 15)
            {
                Failure = "Lobby admission timed out. Check connectivity and rejoin the session.";
                _gateway.Disconnect(ServerPeer);
            }
        }

        while (vehicleMessage is not null && _pendingGameplay.TryDequeue(out var pending))
        {
            RouteGameplay(pending, vehicleMessage);
        }

        while (_gateway.TryReceive(out TransportMessage message))
        {
            if (!_gateway.Connections.TryGetValue(message.RemotePeerId, out var connection) || connection != TransportConnectionState.Connected)
            {
                continue;
            }

            if (LobbyCodec.IsLobby(message.Payload.Span))
            {
                Receive(message);
            }
            else if (message.Payload.Length >= 3 && message.Payload.Span[0] == (byte)'T' && message.Payload.Span[1] == (byte)'E')
            {
                ReceiveEvents(message);
            }
            else if (PlayerLatency.IsLatency(message.Payload.Span))
            {
                if (Authority is not null || Reconnecting || message.RemotePeerId != ServerPeer || message.Delivery != TransportDelivery.Reliable || State is null || !Latency.Accept(message.Payload.Span, State))
                {
                    RejectedPackets++;
                }
            }
            else if (State?.Phase == SessionPhase.Arena)
            {
                if (vehicleMessage is not null)
                {
                    RouteGameplay(message, vehicleMessage);
                }
                else if (Authority is null && message.RemotePeerId == ServerPeer && _pendingGameplay.Count < 32)
                {
                    _pendingGameplay.Enqueue(message);
                }
            }
        }

        Publish();
        PublishEvents();
        if (_loggedResume != ResumeStatus)
        {
            _loggedResume = ResumeStatus;
            Events.Record(EventCategory.Network, "Recovery state", context: ResumeStatus is "Connection interrupted" or "Reconnecting" or "Resume succeeded" or "Grace expired" ? ResumeStatus : "Resume unavailable or rejected", local: Authority is null);
        }

        if (!_loggedFailure && Failure.Length > 0)
        {
            _loggedFailure = true;
            Events.Record(EventCategory.Network, "Session connection failed", cause: "admission, transport or recovery failed", local: Authority is null);
        }

        if (RejectedPackets != _loggedRejections && _seconds >= _nextRejectionReport)
        {
            Events.Record(EventCategory.Network, "Rejected requests", cause: "invalid, stale or unauthorized protocol", amount: RejectedPackets - _loggedRejections, local: Authority is null);
            _loggedRejections = RejectedPackets;
            _nextRejectionReport = _seconds + 1;
        }

        if (Authority is not null && _latencySeconds >= 1)
        {
            _latencySeconds = 0;
            byte[] payload = Latency.Sample(Authority.State, Authority.Peers, _gateway);
            foreach (ulong peer in Authority.Peers.Keys.ToArray())
            {
                Send(peer, payload);
            }
        }
    }

    /// <summary>Dispatches local user intent through the same authoritative rules as remote requests.</summary>
    /// <param name="command">Requested transition or readiness.</param>
    /// <param name="ready">Desired ready state.</param>
    /// <returns>Whether locally accepted or submitted to the host.</returns>
    internal bool Request(LobbyCommand command, bool ready = false)
    {
        if (Failure.Length > 0 || State is null || Reconnecting)
        {
            return false;
        }

        if (Authority is null)
        {
            Send(ServerPeer, LobbyCodec.EncodeCommand(command, State, ready));
            return true;
        }

        bool accepted = Apply(0, command, ready);
        Publish();
        return accepted;
    }

    /// <summary>Starts resume from a short-lived locator after process restart.</summary>
    /// <param name="player">Previous assignment.</param>
    /// <param name="generation">Last acknowledged generation.</param>
    internal void BeginResume(ulong player, ulong generation)
    {
        if (Authority is not null || State is not null || _expectedSession == 0 || player <= 1 || generation == 0)
        {
            throw new InvalidOperationException("Invalid initial resume boundary.");
        }

        _resumePlayer = player;
        _resumeGeneration = generation;
        _interruptedAt = _seconds;
        ResumeStatus = "Reconnecting";
    }

    /// <summary>Ends the bounded attempt only after the complete state boundary is installed.</summary>
    internal void CompleteResume()
    {
        _interruptedAt = null;
        NeedsArenaCheckpoint = false;
        ResumeStatus = "Resume succeeded";
    }

    /// <summary>Submits intentional departure and allows reliable delivery before the gateway is closed.</summary>
    /// <returns>Whether the owner should pump until LeaveComplete.</returns>
    internal bool BeginLeave()
    {
        if (Authority is not null || State is null || Reconnecting || Failure.Length > 0 || LeaveComplete)
        {
            return false;
        }

        if (!_leaveAt.HasValue)
        {
            _leaveAt = _seconds;
            Send(ServerPeer, LobbyCodec.EncodeCommand(LobbyCommand.Leave, State));
        }

        return true;
    }

    /// <summary>Wraps gameplay in the current recipient generation before transport submission.</summary>
    /// <param name="message">Existing gameplay payload.</param>
    internal void SendGameplay(TransportMessage message)
    {
        ulong player = Authority?.PlayerId(message.RemotePeerId) ?? LocalPlayerId;
        var record = State?.Players.SingleOrDefault(value => value.Id == player);
        if (record is null || !record.Connected || Reconnecting)
        {
            throw new InvalidOperationException("No active gameplay binding.");
        }

        _gateway.Send(new TransportMessage(message.RemotePeerId, ConnectionEnvelope.Encode(State!.Session, record.Generation, message.Payload.Span), message.Delivery));
    }

    private bool Apply(ulong peer, LobbyCommand command, bool ready) => Authority!.Execute(peer, command, State!.Session, State.Match, State.Phase, ready, ConnectedPeers());

    private IEnumerable<ulong> ConnectedPeers() => _gateway.Connections.Where(connection => connection.Value != TransportConnectionState.Disconnected).Select(connection => connection.Key);

    private void Receive(TransportMessage message)
    {
        try
        {
            if (message.Delivery != TransportDelivery.Reliable)
            {
                throw new ArgumentException("Lobby control requires reliable delivery.");
            }

            if (Authority is not null)
            {
                var intent = LobbyCodec.DecodeCommand(message.Payload.Span);
                if (_versionRejected.ContainsKey(message.RemotePeerId))
                {
                    return;
                }

                if (intent.Command is LobbyCommand.Join or LobbyCommand.Resume && !Authority.Version.IsCompatible(intent.GameVersion))
                {
                    Send(message.RemotePeerId, LobbyCodec.EncodeVersionMismatch(Authority.Version));
                    // Allow reliable rejection delivery before retiring the unauthorised connection.
                    _versionRejected[message.RemotePeerId] = _seconds + 1;
                    RejectedPackets++;
                    return;
                }

                if (intent.Command == LobbyCommand.Join)
                {
                    if ((_admission is not null && !_admission(message.RemotePeerId)) || Authority.Join(message.RemotePeerId, intent.GameVersion, intent.Name, _identity?.Invoke(message.RemotePeerId)) == 0)
                    {
                        _gateway.Disconnect(message.RemotePeerId);
                    }

                    return;
                }

                if (intent.Command == LobbyCommand.Resume)
                {
                    string? identity = _identity?.Invoke(message.RemotePeerId);
                    if (identity is null || (_admission is not null && !_admission(message.RemotePeerId)) ||
                        !Authority.Resume(message.RemotePeerId, intent.GameVersion, intent.Session, intent.Player, intent.Generation, identity))
                    {
                        Send(message.RemotePeerId, LobbyCodec.EncodeRejection("Resume rejected"));
                        RejectedPackets++;
                    }

                    return;
                }

                if (intent.Command == LobbyCommand.Leave && Authority.Execute(message.RemotePeerId, intent.Command, intent.Session, intent.Match, intent.Phase, false, ConnectedPeers()))
                {
                    Send(message.RemotePeerId, LobbyCodec.EncodeLeft());
                    return;
                }

                if (!Authority.Execute(message.RemotePeerId, intent.Command, intent.Session, intent.Match, intent.Phase, intent.Ready, ConnectedPeers()))
                {
                    throw new ArgumentException("Rejected stale or unauthorized lobby intent.");
                }
            }
            else if (message.RemotePeerId == ServerPeer)
            {
                if (_leaveAt.HasValue && LobbyCodec.IsLeft(message.Payload.Span))
                {
                    _left = true;
                    return;
                }

                if (Failure.Length > 0)
                {
                    return;
                }

                if (LobbyCodec.IsVersionMismatch(message.Payload.Span))
                {
                    Failure = _version.MismatchMessage(LobbyCodec.DecodeVersionMismatch(message.Payload.Span));
                    ResumeStatus = "Game version mismatch";
                    NeedsArenaCheckpoint = false;
                    _pendingGameplay.Clear();
                    Latency.Clear();
                    _gateway.Disconnect(ServerPeer);
                    return;
                }

                if (LobbyCodec.IsRejection(message.Payload.Span))
                {
                    ResumeStatus = LobbyCodec.DecodeRejection(message.Payload.Span);
                    Failure = ResumeStatus + ". Leave and choose a lobby.";
                    return;
                }

                var publication = LobbyCodec.DecodeState(message.Payload.Span);
                if (Reconnecting && !NeedsArenaCheckpoint && (publication.Player != LocalPlayerId || publication.State.Players.Single(player => player.Id == publication.Player).Generation != checked(Generation + 1)))
                {
                    throw new ArgumentException("Resume changed player or did not advance connection generation.");
                }

                if (_expectedSession != 0 && publication.State.Session != _expectedSession)
                {
                    throw new ArgumentException("Lobby publication does not match the discovered online session.");
                }

                if (!_replica.Accept(publication.State, publication.Player, message.RemotePeerId, ServerPeer))
                {
                    throw new ArgumentException("Rejected stale or reassigned session state.");
                }

                if (Reconnecting)
                {
                    NeedsArenaCheckpoint = publication.State.Phase == SessionPhase.Arena;
                    if (!NeedsArenaCheckpoint)
                    {
                        CompleteResume();
                    }
                }

            }
            else
            {
                throw new ArgumentException("Only the connected host can publish lobby state.");
            }
        }
        catch (ArgumentException)
        {
            RejectedPackets++;
        }
    }

    private void Publish()
    {
        if (Authority is null)
        {
            return;
        }

        LobbySnapshot state = Authority.State;
        if (_published == state.Revision)
        {
            return;
        }

        foreach (var peer in Authority.Peers)
        {
            Send(peer.Key, LobbyCodec.EncodeState(state, peer.Value));
        }

        _published = state.Revision;
    }

    private void Send(ulong peer, byte[] payload)
    {
        try
        {
            _gateway.Send(new TransportMessage(peer, payload, TransportDelivery.Reliable));
        }
        catch (InvalidOperationException)
        {
            _gateway.Disconnect(peer);
            Authority?.Disconnect(peer);
            if (Authority is null)
            {
                if (Reconnect is null || State is null)
                {
                    Failure = "Host connection could not accept lobby commands. Leave and reconnect.";
                }
            }
        }
    }

    private void PublishEvents()
    {
        if (Authority is null)
        {
            return;
        }

        foreach (ulong retired in _eventSent.Keys.Where(peer => !Authority.Peers.ContainsKey(peer)).ToArray())
        {
            _eventSent.Remove(retired);
        }

        foreach (var peer in Authority.Peers)
        {
            ulong sent = _eventSent.GetValueOrDefault(peer.Key);
            // New/resumed transport streams begin at their admission event, without replaying past gameplay.
            if (!_eventSent.ContainsKey(peer.Key))
            {
                var admission = Events.Entries.LastOrDefault(entry => entry.Actor == peer.Value && entry.Kind is "Joined" or "Reconnected");
                sent = admission is null ? Events.LastSequence : admission.Sequence - 1;
            }

            if (sent == Events.LastSequence)
            {
                _eventSent[peer.Key] = sent;
                continue;
            }

            var entries = Events.Entries.Where(entry => !entry.Local && entry.Sequence > sent).Take(EventCodec.MaximumEvents).ToArray();
            if (entries.Length > 0)
            {
                var record = State!.Players.Single(player => player.Id == peer.Value);
                byte[] packet = EventCodec.Encode(entries);
                // TE framing stays recognizable outside the fenced payload for the single receive dispatcher.
                Send(peer.Key, [(byte)'T', (byte)'E', 1, .. ConnectionEnvelope.Encode(State.Session, record.Generation, packet)]);
                sent = entries[^1].Sequence;
            }

            _eventSent[peer.Key] = sent;
        }
    }

    private void ReceiveEvents(TransportMessage message)
    {
        try
        {
            if (!EventCodec.IsEvent(message.Payload.Span) || Authority is not null || State is null || message.RemotePeerId != ServerPeer || message.Delivery != TransportDelivery.Reliable || (Reconnecting && !NeedsArenaCheckpoint))
            {
                throw new ArgumentException("Untrusted event publication.");
            }

            var payload = ConnectionEnvelope.Decode(message.Payload.Span[3..], State.Session, Generation);
            foreach (var entry in EventCodec.Decode(payload))
            {
                Events.Accept(entry);
            }
        }
        catch (ArgumentException)
        {
            RejectedPackets++;
        }
    }

    private void RouteGameplay(TransportMessage message, Action<TransportMessage> receive)
    {
        ulong player = Authority?.PlayerId(message.RemotePeerId) ?? (message.RemotePeerId == ServerPeer ? LocalPlayerId : 0);
        var record = State?.Players.SingleOrDefault(value => value.Id == player);
        if (record is null || !record.Connected || (Reconnecting && !NeedsArenaCheckpoint))
        {
            RejectedPackets++;
            return;
        }

        try
        {
            receive(new TransportMessage(message.RemotePeerId, ConnectionEnvelope.Decode(message.Payload.Span, State!.Session, record.Generation), message.Delivery));
        }
        catch (ArgumentException)
        {
            RejectedPackets++;
        }
    }
}
