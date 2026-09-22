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
    private readonly ulong _expectedEpoch;
    private readonly Queue<TransportMessage> _pendingGameplay = new();
    private readonly EventStream _clientEvents = new();
    private readonly Dictionary<ulong, ulong> _eventSent = new();
    private readonly Dictionary<ulong, double> _versionRejected = new();
    private readonly Dictionary<ulong, double> _rejectedJoins = new();
    private readonly Dictionary<ulong, (ulong Player, ulong Generation)> _abandonedReplies = new();
    private LobbyReplica _replica = new();
    private bool _joined;
    private bool _joinCheckpointInstalled;
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
    private LobbyCommand? _reservationCommand;

    /// <summary>Creates a host lobby or a client waiting for admission.</summary>
    /// <param name="gateway">Existing transport.</param>
    /// <param name="session">Host lifetime, zero on a client.</param>
    /// <param name="serverPeer">Client's actual server connection.</param>
    /// <param name="name">Local name request.</param>
    /// <param name="admission">Optional online admission gate; direct-IP retains development admission.</param>
    /// <param name="expectedSession">Online clients require this discovered session lifetime; zero retains development behavior.</param>
    /// <param name="identity">Trusted authenticated subject resolver, absent for unauthenticated Direct-IP.</param>
    /// <param name="expectedEpoch">Current advertised authority fence for a restarted resume.</param>
    /// <param name="gameVersion">Runtime build identity; defaults to the canonical provider.</param>
    internal LobbyNetworkDriver(ITransportGateway gateway, ulong session, ulong serverPeer, string name, Func<ulong, bool>? admission = null, ulong expectedSession = 0, Func<ulong, string?>? identity = null, ulong expectedEpoch = 1, GameVersion? gameVersion = null)
    {
        _gateway = gateway;
        _name = name;
        _version = gameVersion ?? GameVersion.Current;
        _admission = admission;
        _identity = identity;
        _expectedSession = expectedSession;
        _expectedEpoch = expectedEpoch;
        ServerPeer = serverPeer;
        _clientEvents.PlayerName = id => State?.Players.SingleOrDefault(player => player.Id == id)?.Name ?? $"Player {id}";
        if (session != 0)
        {
            Authority = new LobbyAuthority(session, name, _version);
        }
    }

    /// <summary>Host-owned rules; absent on clients.</summary>
    internal LobbyAuthority? Authority { get; private set; }
    /// <summary>Optional authenticated migration coordinator on this same receive stream.</summary>
    internal SessionMigration? Migration { get; set; }
    /// <summary>Structured current-session history; replicated outcomes never originate on a client.</summary>
    internal EventStream Events => Authority?.Events ?? _clientEvents;

    /// <summary>Latest complete authoritative state.</summary>
    internal LobbySnapshot? State => Authority?.State ?? _replica.State;
    /// <summary>Session-stable local identity, zero before admission.</summary>
    internal ulong LocalPlayerId => Authority is null ? (_replica.PlayerId == 0 ? _resumePlayer : _replica.PlayerId) : Authority.State.CurrentHostId;
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
    /// <summary>Fresh arena admission remains distinct from retained-identity recovery.</summary>
    internal bool JoiningArena { get; private set; }
    /// <summary>A submitted complete checkpoint acknowledgement may already have committed a retained slot remotely.</summary>
    internal bool CanResume => !JoiningArena || _joinCheckpointInstalled;
    /// <summary>Existing vehicle authority commits the acknowledged fresh participant.</summary>
    internal Func<ulong, bool>? ActivateJoin { get; set; }
    /// <summary>Current gameplay eligibility supplied by the active arena owner.</summary>
    internal Func<bool>? ArenaAdmissionOpen { get; set; }
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
    /// <summary>Read/release response bound to the authenticated startup reservation request.</summary>
    internal ReservationResult? Reservation { get; private set; }

    /// <summary>Installs the agreed epoch without carrying transport ownership across the boundary.</summary>
    /// <param name="checkpoint">Agreed old authority checkpoint.</param>
    /// <param name="host">Deterministically elected replacement.</param>
    /// <param name="server">Replacement transport peer, zero on the new host.</param>
    /// <param name="survivorPeers">Active player-to-peer bindings used only for uninterrupted lobby migration.</param>
    internal void InstallMigration(MigrationCheckpoint checkpoint, ulong host, ulong server, IReadOnlyDictionary<ulong, ulong>? survivorPeers = null)
    {
        ulong local = LocalPlayerId;
        var restored = LobbyAuthority.Restore(checkpoint.Lobby, host, checked(checkpoint.Lobby.State.AuthorityEpoch + 1), survivorPeers);
        Authority = local == host ? restored : null;
        _clientEvents.ResetAuthority(checkpoint.Lobby.Tick * 1000 / 60);
        _eventSent.Clear();
        _replica = new LobbyReplica();
        if (Authority is null)
        {
            _replica.Accept(restored.State, local, server, server);
        }

        ServerPeer = server;
        _seconds = checkpoint.Lobby.Tick / 60.0;
        _published = 0;
        _pendingGameplay.Clear();
        Latency.Clear();
        bool lobbyContinuity = restored.State.ReconnectPolicy == SessionReconnectPolicy.FreshJoin;
        _joined = lobbyContinuity;
        _interruptedAt = Authority is null && !lobbyContinuity ? _seconds : null;
        NeedsArenaCheckpoint = Authority is null && restored.State.Phase == SessionPhase.Arena;
        Failure = string.Empty;
        ResumeStatus = "Host migrated — resynchronizing";
    }

    /// <summary>Stops all gameplay after bounded migration failure.</summary>
    /// <param name="reason">Presentation-safe failure reason.</param>
    internal void FailMigration(string reason)
    {
        Failure = "Host migration failed: " + reason + ". Leave and choose a lobby.";
        _gateway.Stop();
    }

    /// <summary>Keeps restored clients frozen until the replacement authenticates their rebind.</summary>
    internal void BeginMigrationResume()
    {
        _interruptedAt = _seconds;
        _joined = false;
        NeedsArenaCheckpoint = false;
    }

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
        if (Authority is not null)
        {
            Authority.AdmissionOpen = _leaveAt is null && Migration?.Frozen != true && (State!.Phase == SessionPhase.Lobby || ArenaAdmissionOpen?.Invoke() == true);
            var pending = Authority.Peers.Keys.Where(Authority.IsPendingJoin).ToArray();
            Authority.AdvanceTime((ulong)(_seconds * 60));
            foreach (ulong peer in pending.Where(peer => Authority.PlayerId(peer) == 0))
            {
                RejectJoin(peer, "Join bootstrap failed");
            }
        }

        _gateway.Poll();
        foreach (ulong peer in _abandonedReplies.Keys.Where(peer => !_gateway.Connections.TryGetValue(peer, out var state) || state == TransportConnectionState.Disconnected).ToArray())
        {
            _abandonedReplies.Remove(peer);
        }

        foreach (ulong peer in _rejectedJoins.Where(pair => _seconds >= pair.Value).Select(pair => pair.Key).ToArray())
        {
            _gateway.Disconnect(peer);
            _rejectedJoins.Remove(peer);
        }

        Migration?.Advance(seconds);
        if (Failure.Length > 0)
        {
            return;
        }

        if (Migration?.Negotiating == true)
        {
            while (_gateway.TryReceive(out var frozenMessage))
            {
                Migration.Receive(frozenMessage);
            }

            return;
        }

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
                if (_reservationCommand is not null)
                {
                    Failure = "Could not verify the retained match. Retry from the menu.";
                }
                else if (!CanResume)
                {
                    Failure = "Join interrupted before arena activation. Leave and join again.";
                }
                else if (State?.ReconnectPolicy == SessionReconnectPolicy.FreshJoin)
                {
                    ResumeStatus = Migration is null ? "Lobby departure requires a fresh join" : Migration.Status;
                    if (Migration is null)
                    {
                        Failure = "Disconnected from lobby. Join again to receive a fresh player assignment.";
                    }
                }
                else if ((State is not null || _resumePlayer != 0) && Reconnect is not null && Failure.Length == 0)
                {
                    if (!_interruptedAt.HasValue)
                    {
                        JoiningArena = false;
                        _interruptedAt = _seconds;
                        _nextAttempt = _seconds + 1;
                        ResumeStatus = "Connection interrupted";
                        _pendingGameplay.Clear();
                        Latency.Clear();
                    }

                    if (_seconds >= _nextAttempt)
                    {
                        ResumeStatus = "Reconnecting";
                        _nextAttempt = _seconds + 2;
                        try
                        {
                            ServerPeer = Reconnect();
                            _joined = false;
                            NeedsArenaCheckpoint = false;
                            _pendingGameplay.Clear();
                            Latency.Clear();
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
                byte[] request = _reservationCommand is { } reservationCommand
                    ? LobbyCodec.EncodeResume(_expectedSession, _resumePlayer, _resumeGeneration, _expectedEpoch, _version.ToString(), reservationCommand)
                    : Reconnecting
                    ? LobbyCodec.EncodeResume(State?.Session ?? _expectedSession, LocalPlayerId, Generation, State?.AuthorityEpoch ?? _expectedEpoch, _version.ToString())
                    : LobbyCodec.EncodeCommand(LobbyCommand.Join, null, name: _name, gameVersion: _version.ToString());
                Send(ServerPeer, request);
                _joined = true;
            }

            if ((State is null || JoiningArena) && !Reconnecting && Reservation != ReservationResult.Available && _joiningSeconds > 15)
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
            if (Migration?.Receive(message) == true)
            {
                continue;
            }

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
            Events.Record(EventCategory.Network, "Recovery state", context: ResumeStatus is "Connection interrupted" or "Reconnecting" or "Resume succeeded" ? ResumeStatus : "Resume unavailable or rejected", local: Authority is null);
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
            byte[] payload = Latency.Sample(Authority.SnapshotFor(Authority.State.CurrentHostId), Authority.Peers, _gateway);
            foreach (ulong peer in Authority.Peers.Keys.Where(peer => !Authority.IsPendingJoin(peer)).ToArray())
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
        if (Failure.Length > 0 || State is null || Reconnecting || Migration?.Frozen == true)
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

    /// <summary>Publishes an authoritative restart; no remote intent can invoke this local host API.</summary>
    internal bool Restart()
    {
        if (Authority is null || Failure.Length > 0 || Reconnecting || Migration?.Frozen == true ||
            !Authority.Restart(0, ConnectedPeers()))
        {
            return false;
        }

        Publish();
        return true;
    }

    /// <summary>Publishes a local host map edit through Core authority.</summary>
    /// <param name="map">Supported map selection.</param>
    /// <returns>Whether authority accepted the edit.</returns>
    internal bool SelectMap(MatchMap map)
    {
        if (Authority is null || Reconnecting || Migration?.Frozen == true || Failure.Length > 0 || !Authority.SelectMap(0, map))
        {
            return false;
        }

        Publish();
        return true;
    }

    /// <summary>Starts resume from a short-lived locator after process restart.</summary>
    /// <param name="player">Previous assignment.</param>
    /// <param name="generation">Last acknowledged generation.</param>
    internal void BeginResume(ulong player, ulong generation)
    {
        if (Authority is not null || State is not null || _expectedSession == 0 || player == 0 || generation == 0)
        {
            throw new InvalidOperationException("Invalid initial resume boundary.");
        }

        _resumePlayer = player;
        _resumeGeneration = generation;
        _interruptedAt = _seconds;
        ResumeStatus = "Reconnecting";
    }

    /// <summary>Queries existing authority before startup can offer Reconnect or Leave Match.</summary>
    /// <param name="player">Saved player assignment.</param>
    /// <param name="generation">Last acknowledged connection generation.</param>
    internal void InspectReservation(ulong player, ulong generation)
    {
        if (Authority is not null || State is not null || _joined || _expectedSession == 0 || player == 0 || generation == 0)
        {
            throw new InvalidOperationException("Invalid reservation inspection boundary.");
        }

        _resumePlayer = player;
        _resumeGeneration = generation;
        _reservationCommand = LobbyCommand.InspectReservation;
    }

    /// <summary>Commits one explicit decision after an authoritative reservation response.</summary>
    /// <param name="reconnect">True to use ordinary resume; false to permanently release the reservation.</param>
    /// <returns>Whether a new operation was submitted.</returns>
    internal bool DecideReservation(bool reconnect)
    {
        if (_reservationCommand != LobbyCommand.InspectReservation || Reservation != ReservationResult.Available || Failure.Length > 0)
        {
            return false;
        }

        Reservation = null;
        _joined = false;
        _joiningSeconds = 0;
        _reservationCommand = reconnect ? null : LobbyCommand.Abandon;
        if (reconnect)
        {
            BeginResume(_resumePlayer, _resumeGeneration);
        }

        return true;
    }

    /// <summary>Ends the bounded attempt only after the complete state boundary is installed.</summary>
    internal void CompleteResume()
    {
        if (JoiningArena)
        {
            Send(ServerPeer, LobbyCodec.EncodeCommand(LobbyCommand.Activate, State));
            _joinCheckpointInstalled = true;
            NeedsArenaCheckpoint = false;
            return;
        }

        _interruptedAt = null;
        NeedsArenaCheckpoint = false;
        ResumeStatus = "Resume succeeded";
    }

    /// <summary>Submits intentional departure and allows reliable delivery before the gateway is closed.</summary>
    /// <returns>Whether the owner should pump until LeaveComplete.</returns>
    internal bool BeginLeave()
    {
        if (Authority is not null && Migration?.Subjects is not null)
        {
            _leaveAt ??= _seconds;
            Migration.AnnounceDeparture();
            return true;
        }

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

        _gateway.Send(new TransportMessage(message.RemotePeerId, ConnectionEnvelope.Encode(State!.Session, record.Generation, message.Payload.Span, State.AuthorityEpoch), message.Delivery));
    }

    /// <summary>Rolls back provisional admission and reports a bounded recoverable failure.</summary>
    /// <param name="peer">Rejected transport sender.</param>
    /// <param name="reason">Allowlisted public diagnostic.</param>
    internal void RejectJoin(ulong peer, string reason)
    {
        if (Authority?.IsPendingJoin(peer) == true)
        {
            Authority.Disconnect(peer);
        }

        Send(peer, LobbyCodec.EncodeRejection(reason));
        _rejectedJoins.TryAdd(peer, _seconds + 1);
    }

    private bool Apply(ulong peer, LobbyCommand command, bool ready) => Authority!.Execute(peer, command, State!.Session, State.Match, State.Phase, ready, ConnectedPeers(), State.AuthorityEpoch);

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

                if (intent.Command is LobbyCommand.Join or LobbyCommand.Resume or LobbyCommand.InspectReservation or LobbyCommand.Abandon && !Authority.Version.IsCompatible(intent.GameVersion))
                {
                    Send(message.RemotePeerId, LobbyCodec.EncodeVersionMismatch(Authority.Version));
                    // Allow reliable rejection delivery before retiring the unauthorised connection.
                    _versionRejected[message.RemotePeerId] = _seconds + 1;
                    RejectedPackets++;
                    return;
                }

                if (Migration?.Frozen == true && intent.Command is not (LobbyCommand.Resume or LobbyCommand.Leave))
                {
                    throw new ArgumentException("Authority is frozen pending recovery.");
                }

                if (intent.Command == LobbyCommand.Join)
                {
                    if (_rejectedJoins.ContainsKey(message.RemotePeerId))
                    {
                        return;
                    }

                    if (_admission is not null && !_admission(message.RemotePeerId))
                    {
                        RejectJoin(message.RemotePeerId, "Access denied");
                    }
                    else if (Authority.Join(message.RemotePeerId, intent.GameVersion, intent.Name, _identity?.Invoke(message.RemotePeerId)) == 0)
                    {
                        RejectJoin(message.RemotePeerId, State!.Players.Count >= 8 ? "Session full" : "Session unavailable");
                    }

                    return;
                }

                if (intent.Command is LobbyCommand.InspectReservation or LobbyCommand.Abandon)
                {
                    string? subject = _identity?.Invoke(message.RemotePeerId);
                    if (subject is null || intent.Session != State!.Session || intent.AuthorityEpoch != State.AuthorityEpoch)
                    {
                        throw new ArgumentException("Unauthenticated or stale reservation request.");
                    }

                    bool available = Authority.HasReservation(intent.Session, intent.Player, intent.Generation, subject);
                    ReservationResult result = available ? ReservationResult.Available : ReservationResult.Missing;
                    if (intent.Command == LobbyCommand.Abandon)
                    {
                        if ((_abandonedReplies.TryGetValue(message.RemotePeerId, out var released) && released == (intent.Player, intent.Generation)) || Authority.Abandon(intent.Session, intent.Player, intent.Generation, subject))
                        {
                            _abandonedReplies[message.RemotePeerId] = (intent.Player, intent.Generation);
                            result = ReservationResult.Abandoned;
                        }
                    }

                    Send(message.RemotePeerId, LobbyCodec.EncodeReservation(intent.Session, intent.Player, intent.Generation, intent.AuthorityEpoch, result));
                    return;
                }

                if (intent.Command == LobbyCommand.Activate && intent.Session == State!.Session && intent.Match == State.Match && intent.AuthorityEpoch == State.AuthorityEpoch)
                {
                    if (Authority.IsPendingJoin(message.RemotePeerId))
                    {
                        if (!Authority.AdmissionOpen || ActivateJoin?.Invoke(message.RemotePeerId) != true || !Authority.CompleteJoin(message.RemotePeerId))
                        {
                            RejectJoin(message.RemotePeerId, "Join bootstrap failed");
                        }
                    }

                    return;
                }

                if (intent.Command == LobbyCommand.Resume)
                {
                    string? identity = _identity?.Invoke(message.RemotePeerId);
                    if (intent.AuthorityEpoch != State!.AuthorityEpoch || identity is null || (_admission is not null && !_admission(message.RemotePeerId)) ||
                        !Authority.Resume(message.RemotePeerId, intent.GameVersion, intent.Session, intent.Player, intent.Generation, identity))
                    {
                        Send(message.RemotePeerId, LobbyCodec.EncodeRejection("Resume rejected"));
                        RejectedPackets++;
                    }

                    return;
                }

                if (intent.Command == LobbyCommand.Leave && Authority.Execute(message.RemotePeerId, intent.Command, intent.Session, intent.Match, intent.Phase, false, ConnectedPeers(), intent.AuthorityEpoch))
                {
                    Send(message.RemotePeerId, LobbyCodec.EncodeLeft());
                    return;
                }

                if (!Authority.Execute(message.RemotePeerId, intent.Command, intent.Session, intent.Match, intent.Phase, intent.Ready, ConnectedPeers(), intent.AuthorityEpoch))
                {
                    throw new ArgumentException("Rejected stale or unauthorized lobby intent.");
                }
            }
            else if (message.RemotePeerId == ServerPeer)
            {
                if (LobbyCodec.IsReservation(message.Payload.Span))
                {
                    if (_reservationCommand is null || Reservation is not null)
                    {
                        return;
                    }

                    ReservationResult result = LobbyCodec.DecodeReservation(message.Payload.Span, _expectedSession, _resumePlayer, _resumeGeneration, _expectedEpoch);
                    if ((_reservationCommand == LobbyCommand.InspectReservation && result == ReservationResult.Abandoned) || (_reservationCommand == LobbyCommand.Abandon && result == ReservationResult.Available))
                    {
                        throw new ArgumentException("Unexpected reservation result.");
                    }

                    Reservation = result;
                    return;
                }

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
                    if (JoiningArena)
                    {
                        _joinCheckpointInstalled = false;
                    }

                    ResumeStatus = LobbyCodec.DecodeRejection(message.Payload.Span);
                    Failure = ResumeStatus + ". Leave and choose a lobby.";
                    return;
                }

                if (_reservationCommand is not null)
                {
                    throw new ArgumentException("Reservation inspection cannot accept a gameplay assignment.");
                }

                var publication = LobbyCodec.DecodeState(message.Payload.Span);
                if (State is null && publication.State.AuthorityEpoch != _expectedEpoch)
                {
                    throw new ArgumentException("Lobby publication has a stale authority epoch.");
                }

                if (Reconnecting && !NeedsArenaCheckpoint && (publication.Player != LocalPlayerId || publication.State.Players.Single(player => player.Id == publication.Player).Generation != checked(Generation + 1)))
                {
                    throw new ArgumentException("Resume changed player or did not advance connection generation.");
                }

                if (_expectedSession != 0 && publication.State.Session != _expectedSession)
                {
                    throw new ArgumentException("Lobby publication does not match the discovered online session.");
                }

                bool freshArena = State is null && !Reconnecting && publication.State.Phase == SessionPhase.Arena;
                if (!_replica.Accept(publication.State, publication.Player, message.RemotePeerId, ServerPeer))
                {
                    throw new ArgumentException("Rejected stale or reassigned session state.");
                }

                if (freshArena)
                {
                    JoiningArena = true;
                    NeedsArenaCheckpoint = true;
                    _joiningSeconds = 0;
                }

                if (JoiningArena && _joinCheckpointInstalled && publication.Activated)
                {
                    JoiningArena = false;
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
            Send(peer.Key, LobbyCodec.EncodeState(Authority.SnapshotFor(peer.Value), peer.Value, !Authority.IsPendingJoin(peer.Key)));
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
            if (Authority.IsPendingJoin(peer.Key))
            {
                _eventSent[peer.Key] = Events.LastSequence;
                continue;
            }

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
                Send(peer.Key, [(byte)'T', (byte)'E', 1, .. ConnectionEnvelope.Encode(State.Session, record.Generation, packet, State.AuthorityEpoch)]);
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

            var payload = ConnectionEnvelope.Decode(message.Payload.Span[3..], State.Session, Generation, State.AuthorityEpoch);
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
        if (Authority is not null && Migration?.Frozen == true)
        {
            return;
        }

        ulong player = Authority?.PlayerId(message.RemotePeerId) ?? (message.RemotePeerId == ServerPeer ? LocalPlayerId : 0);
        var record = State?.Players.SingleOrDefault(value => value.Id == player);
        if (record is null || !record.Connected || (Reconnecting && !NeedsArenaCheckpoint))
        {
            RejectedPackets++;
            return;
        }

        try
        {
            var payload = ConnectionEnvelope.Decode(message.Payload.Span, State!.Session, record.Generation, State.AuthorityEpoch);
            if (Authority?.IsPendingJoin(message.RemotePeerId) == true &&
                (!MatchEntryCodec.IsEntry(payload) || MatchEntryCodec.Decode(payload, State.Match) != MatchEntryCodec.Loaded || message.Delivery != TransportDelivery.Reliable))
            {
                throw new ArgumentException("Pending participants may only announce completed resource loading.");
            }

            receive(new TransportMessage(message.RemotePeerId, payload, message.Delivery));
        }
        catch (ArgumentException)
        {
            RejectedPackets++;
        }
    }
}
