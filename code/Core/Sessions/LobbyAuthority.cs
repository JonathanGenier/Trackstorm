using Trackstorm.Core.Events;

namespace Trackstorm.Core.Sessions;

/// <summary>Sole owner of admission, sender identity, readiness and session transitions.</summary>
public sealed class LobbyAuthority
{
    private readonly Dictionary<ulong, ulong> _peers = new();
    private readonly Dictionary<ulong, string> _identities = new();
    private readonly Dictionary<ulong, ulong> _previousPeers = new();
    private readonly Dictionary<ulong, ulong> _pendingJoins = new();
    private ulong _tick;
    private ulong _nextId = 1;

    /// <summary>Creates the host's unready lobby.</summary>
    /// <param name="session">Nonzero lifetime leaving room for future match generations.</param>
    /// <param name="name">Untrusted host display name.</param>
    public LobbyAuthority(ulong session, string name)
    {
        if (session == ulong.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(session));
        }

        State = new LobbySnapshot(session, 1, session, SessionPhase.Lobby, new[] { new SessionPlayer(1, PlayerName.Sanitize(name), false) });
        Events.PlayerName = id => State.Players.SingleOrDefault(player => player.Id == id)?.Name ?? State.Departed.SingleOrDefault(player => player.Id == id)?.Name ?? $"Player {id}";
        Events.Record(EventCategory.Session, "Created", actor: 1);
        Events.Record(EventCategory.Session, "Joined", actor: 1);
    }

    /// <summary>Current immutable authority boundary.</summary>
    public LobbySnapshot State { get; private set; }
    /// <summary>Authoritative session journal shared with arena gameplay.</summary>
    public EventStream Events { get; } = new();
    /// <summary>Copy of transport-to-player assignments for vehicle integration.</summary>
    public IReadOnlyDictionary<ulong, ulong> Peers => new Dictionary<ulong, ulong>(_peers);
    /// <summary>Host gameplay eligibility, excluding the independently enforced participant limit.</summary>
    public bool AdmissionOpen { get; set; } = true;
    /// <summary>Fresh admission counts every roster slot, including pending and retained participants.</summary>
    public bool CanJoin => AdmissionOpen && State.Players.Count < 8 && State.Players.Count + State.Departed.Count < Matches.MatchState.MaximumPlayers;

    /// <summary>Session tuning; the successor restores this instead of loading its host-local preferences.</summary>
    public Development.GameplayConfigurationState Configuration { get; private set; } = new(0, new());

    /// <summary>Builds a replacement with all remote players reserved for authenticated fresh connections.</summary>
    /// <param name="checkpoint">Validated old authority boundary.</param>
    /// <param name="host">Elected stable identity.</param>
    /// <param name="epoch">Exactly the next authority epoch.</param>
    /// <param name="survivorPeers">Active replacement-host transport bindings for every continuing lobby survivor.</param>
    /// <returns>New authority with Ready cleared and no inherited transport handles.</returns>
    public static LobbyAuthority Restore(LobbyRestoreState checkpoint, ulong host, ulong epoch, IReadOnlyDictionary<ulong, ulong>? survivorPeers = null)
    {
        var previous = checkpoint.State;
        if (epoch != checked(previous.AuthorityEpoch + 1) || host == previous.CurrentHostId || !previous.Players.Any(player => player.Id == host && player.Connected))
        {
            throw new ArgumentException("Invalid authority transition.");
        }

        SessionPlayer[] restoredPlayers = previous.ReconnectPolicy == SessionReconnectPolicy.FreshJoin
            ? previous.Players.Where(player => player.Id != previous.CurrentHostId).Select(player => player with { Ready = false, Connected = true, RetainedHost = false }).ToArray()
            : previous.Players.Select(player => player with { Ready = false, Connected = player.Id == host, RetainedHost = player.RetainedHost || player.Id == previous.CurrentHostId }).ToArray();
        var result = new LobbyAuthority(previous.Session, previous.Players.Single(player => player.Id == host).Name)
        {
            _tick = checkpoint.Tick,
            _nextId = checkpoint.NextId,
            Configuration = checkpoint.Configuration,
            State = new LobbySnapshot(previous.Session, checked(previous.Revision + 1), previous.Match, previous.Phase, restoredPlayers, host, epoch, previous.Departed),
        };

        foreach (var player in result.State.Players)
        {
            result._identities.Add(player.Id, checkpoint.Subjects[player.Id]);
        }

        if (previous.ReconnectPolicy == SessionReconnectPolicy.FreshJoin)
        {
            ulong[] expected = result.State.Players.Where(player => player.Id != host).Select(player => player.Id).ToArray();
            if (survivorPeers is not null && (!expected.ToHashSet().SetEquals(survivorPeers.Keys) || survivorPeers.Values.Any(peer => peer == 0) || survivorPeers.Values.Distinct().Count() != survivorPeers.Count))
            {
                throw new ArgumentException($"Lobby migration requires active transport bindings for {expected.Length} continuing survivors; received {survivorPeers.Count}.");
            }

            foreach (var survivor in survivorPeers ?? new Dictionary<ulong, ulong>())
            {
                result._peers.Add(survivor.Value, survivor.Key);
            }
        }

        result.Events.ResetAuthority(checkpoint.Tick * 1000 / 60);
        result.Events.Record(EventCategory.Session, "Authority migrated", actor: host, amount: epoch);

        return result;
    }

    /// <summary>Whether this connection still owes its complete bootstrap acknowledgement.</summary>
    /// <param name="peer">Actual transport sender.</param>
    /// <returns>Whether admission is provisional.</returns>
    public bool IsPendingJoin(ulong peer) => _pendingJoins.ContainsKey(peer);

    /// <summary>Commits fresh participation after the complete bootstrap has been acknowledged.</summary>
    /// <param name="peer">Actual transport sender.</param>
    /// <returns>Whether this pending admission was completed once.</returns>
    public bool CompleteJoin(ulong peer)
    {
        if (!AdmissionOpen || !_pendingJoins.Remove(peer))
        {
            return false;
        }

        Events.Record(EventCategory.Session, "Joined", actor: PlayerId(peer));
        Publish(State.Players);
        return true;
    }

    /// <summary>Publishes committed participants plus the recipient's own provisional assignment.</summary>
    /// <param name="recipient">Authoritative recipient identity.</param>
    /// <returns>A detached roster excluding other unfinished admissions.</returns>
    public LobbySnapshot SnapshotFor(ulong recipient)
    {
        var pending = _pendingJoins.Keys.Select(PlayerId).Where(player => player != recipient).ToHashSet();
        return pending.Count == 0 ? State : new LobbySnapshot(State.Session, State.Revision, State.Match, State.Phase, State.Players.Where(player => !pending.Contains(player.Id)), State.CurrentHostId, State.AuthorityEpoch, State.Departed);
    }

    /// <summary>Retains a validated host-owned tuning boundary for the next checkpoint and arena.</summary>
    /// <param name="configuration">Current arena or explicitly edited lobby configuration.</param>
    public void RetainConfiguration(Development.GameplayConfigurationState configuration)
    {
        if (!configuration.CanReplace(Configuration))
        {
            throw new ArgumentException("Session configuration cannot regress within one authority epoch.");
        }

        Configuration = configuration;
    }

    /// <summary>Applies an authenticated local host's lobby tuning edit through the existing configuration rules.</summary>
    /// <param name="peer">Actual sender; zero denotes the local authority.</param>
    /// <param name="edits">Allowlisted tuning transaction.</param>
    /// <param name="error">Safe validation feedback.</param>
    /// <returns>Whether the transaction is accepted.</returns>
    public bool TryConfigure(ulong peer, IReadOnlyDictionary<string, double> edits, out string error)
    {
        error = "Only the lobby authority may change session tuning.";
        if (peer != 0 || State.Phase != SessionPhase.Lobby || !Development.GameplayOptions.TryApply(Configuration.Configuration, edits, out var candidate, out error))
        {
            return false;
        }

        if (candidate != Configuration.Configuration)
        {
            if (Configuration.Revision == ulong.MaxValue)
            {
                error = "Configuration revision exhausted.";
                return false;
            }

            Configuration = new(checked(Configuration.Revision + 1), candidate);
        }

        return true;
    }

    /// <summary>Captures authenticated authority, including the local host's trusted subject.</summary>
    /// <param name="hostSubject">Authenticated local identity from the adapter.</param>
    /// <returns>Detached continuation state.</returns>
    public LobbyRestoreState Capture(string hostSubject)
    {
        var subjects = new Dictionary<ulong, string>(_identities) { [State.CurrentHostId] = hostSubject };
        var pending = _pendingJoins.Keys.Select(PlayerId).ToHashSet();
        foreach (ulong player in pending)
        {
            subjects.Remove(player);
        }

        return new LobbyRestoreState(SnapshotFor(State.CurrentHostId), _tick, _nextId, subjects, Configuration);
    }

    /// <summary>Assigns a fresh identity to a connected transport sender.</summary>
    /// <param name="peer">Actual nonzero transport sender.</param>
    /// <param name="name">Requested display name.</param>
    /// <returns>Accepted player ID, or zero for rejected admission.</returns>
    /// <param name="identity">Optional authenticated, provider-neutral subject supplied by trusted integration.</param>
    public ulong Join(ulong peer, string name, string? identity = null)
    {
        if (_peers.TryGetValue(peer, out ulong existing))
        {
            return _identities.GetValueOrDefault(existing) == identity ? existing : 0;
        }

        if (identity is not null && (identity.Length is 0 or > 256 || _identities.ContainsValue(identity)))
        {
            return 0;
        }

        ulong id = checked(_nextId + 1);
        if (!Add(peer, id, name))
        {
            return 0;
        }

        if (identity is not null)
        {
            _identities.Add(id, identity);
        }

        return id;
    }

    /// <summary>Admits an explicitly assigned identity, rejecting duplicates and retired identities.</summary>
    /// <param name="peer">Actual connected sender.</param>
    /// <param name="id">Host-assigned identity.</param>
    /// <param name="name">Untrusted name.</param>
    /// <returns>Whether admission succeeded.</returns>
    public bool Add(ulong peer, ulong id, string name)
    {
        if (peer == 0 || id <= _nextId || id == ulong.MaxValue || _peers.ContainsKey(peer) || !CanJoin)
        {
            return false;
        }

        _peers.Add(peer, id);
        _nextId = Math.Max(_nextId, id);
        Publish(State.Players.Append(new SessionPlayer(id, PlayerName.Sanitize(name), false)));
        if (State.Phase == SessionPhase.Arena)
        {
            _pendingJoins.Add(peer, _tick);
        }
        else
        {
            Events.Record(EventCategory.Session, "Joined", actor: id);
        }

        return true;
    }

    /// <summary>Changes only the actual sender's readiness; zero denotes the local host.</summary>
    /// <param name="peer">Actual sender or local host zero.</param>
    /// <param name="ready">Desired readiness.</param>
    /// <returns>Whether the request is legal.</returns>
    public bool SetReady(ulong peer, bool ready)
    {
        ulong id = PlayerId(peer);
        if (id == 0 || State.Phase != SessionPhase.Lobby)
        {
            return false;
        }

        if (State.Players.Single(player => player.Id == id).Ready != ready)
        {
            Publish(State.Players.Select(player => player.Id == id ? player with { Ready = ready } : player));
        }

        return true;
    }

    /// <summary>Accepts a start only from the host with every connected roster member ready.</summary>
    /// <param name="peer">Actual sender; zero is the local host.</param>
    /// <returns>Whether all peers should enter a fresh arena generation.</returns>
    /// <param name="connectedPeers">Optional transport admission boundary, including pending connections.</param>
    public bool Start(ulong peer, IEnumerable<ulong>? connectedPeers = null)
    {
        if (peer != 0 || !State.CanStart || (connectedPeers is not null && !_peers.Keys.ToHashSet().SetEquals(connectedPeers)))
        {
            return false;
        }

        State = new LobbySnapshot(State.Session, checked(State.Revision + 1), checked(State.Match + 1), SessionPhase.Arena, State.Players, State.CurrentHostId, State.AuthorityEpoch);
        Events.Record(EventCategory.Session, "Arena started", actor: State.CurrentHostId);
        return true;
    }

    /// <summary>Ends the development arena and clears readiness while retaining connected identities.</summary>
    /// <param name="peer">Actual sender; only local host zero can end the arena.</param>
    /// <returns>Whether a return transition was committed.</returns>
    public bool Return(ulong peer)
    {
        if (peer != 0 || State.Phase != SessionPhase.Arena)
        {
            return false;
        }

        foreach (ulong pending in _pendingJoins.Keys.ToArray())
        {
            Disconnect(pending);
        }

        AdmissionOpen = true;
        foreach (ulong id in State.Players.Where(player => !player.Connected).Select(player => player.Id).ToArray())
        {
            Events.Record(EventCategory.Session, "Match reservation ended", actor: id);
            RemovePlayer(id);
        }

        State = new LobbySnapshot(State.Session, checked(State.Revision + 1), State.Match, SessionPhase.Lobby, State.Players.Select(player => player with { Ready = false, RetainedHost = false }), State.CurrentHostId, State.AuthorityEpoch);
        Events.Record(EventCategory.Session, "Returned to lobby", actor: State.CurrentHostId);
        return true;
    }

    /// <summary>Applies the same phase lifecycle to an intentional departure.</summary>
    /// <param name="peer">Departed transport sender.</param>
    /// <returns>Whether an existing player was removed.</returns>
    public bool Remove(ulong peer)
    {
        if (State.ReconnectPolicy == SessionReconnectPolicy.RetainedResume)
        {
            return Disconnect(peer);
        }

        if (!_peers.Remove(peer, out ulong id))
        {
            return false;
        }

        Events.Record(EventCategory.Session, "Left", actor: id);
        RemovePlayer(id);
        return true;
    }

    /// <summary>Applies the current phase's explicit fresh-join or retained-resume disconnect policy.</summary>
    /// <param name="peer">Actual lost connection.</param>
    /// <returns>Whether an active binding was retired.</returns>
    public bool Disconnect(ulong peer)
    {
        if (!_peers.Remove(peer, out ulong id))
        {
            return false;
        }

        if (_pendingJoins.Remove(peer))
        {
            RemovePlayer(id);
            return true;
        }

        Events.Record(EventCategory.Network, "Disconnected", actor: id, cause: "connection lost");
        if (State.ReconnectPolicy == SessionReconnectPolicy.FreshJoin)
        {
            RemovePlayer(id);
            return true;
        }

        Events.Record(EventCategory.Network, "Match reservation entered", actor: id);
        _previousPeers[id] = peer;
        Publish(State.Players.Select(player => player.Id == id ? player with { Ready = false, Connected = false } : player));
        return true;
    }

    /// <summary>Advances the journal clock; only explicit abandonment or Return ends retained reservations.</summary>
    /// <param name="tick">Caller-owned 60 Hz session clock, including time spent in lobby.</param>
    public void AdvanceTime(ulong tick)
    {
        if (tick < _tick)
        {
            throw new ArgumentOutOfRangeException(nameof(tick));
        }

        _tick = tick;
        foreach (ulong peer in _pendingJoins.Where(pair => tick - pair.Value >= 900).Select(pair => pair.Key).ToArray())
        {
            Disconnect(peer);
        }

        Events.AdvanceTime(tick * 1000 / 60);
    }

    /// <summary>Resolves a retained subject without exposing platform identity in gameplay records.</summary>
    /// <param name="identity">Trusted authenticated subject.</param>
    /// <returns>Reserved player, or zero when absent/expired.</returns>
    public ulong FindPlayer(string identity) => _identities.FirstOrDefault(pair => pair.Value == identity).Key;

    /// <summary>Validates a retained reservation without rebinding or changing gameplay state.</summary>
    /// <param name="session">Expected session lifetime.</param>
    /// <param name="playerId">Previous player assignment.</param>
    /// <param name="generation">Last acknowledged connection generation.</param>
    /// <param name="identity">Transport-authenticated subject.</param>
    /// <returns>Whether this exact disconnected reservation is owned by the subject.</returns>
    public bool HasReservation(ulong session, ulong playerId, ulong generation, string identity) =>
        session == State.Session && State.ReconnectPolicy == SessionReconnectPolicy.RetainedResume &&
        _identities.TryGetValue(playerId, out string? subject) && subject == identity &&
        State.Players.Any(player => player.Id == playerId && !player.Connected && player.Generation == generation);

    /// <summary>Permanently releases a disconnected slot while preserving its match display identity and scores.</summary>
    /// <param name="session">Expected session lifetime.</param>
    /// <param name="playerId">Previous player assignment.</param>
    /// <param name="generation">Last acknowledged connection generation.</param>
    /// <param name="identity">Transport-authenticated owner.</param>
    /// <returns>Whether this reservation was released once.</returns>
    public bool Abandon(ulong session, ulong playerId, ulong generation, string identity)
    {
        if (!HasReservation(session, playerId, generation, identity))
        {
            return false;
        }

        SessionPlayer player = State.Players.Single(player => player.Id == playerId);
        _identities.Remove(playerId);
        _previousPeers.Remove(playerId);
        State = new LobbySnapshot(State.Session, checked(State.Revision + 1), State.Match, State.Phase, State.Players.Where(player => player.Id != playerId), State.CurrentHostId, State.AuthorityEpoch, State.Departed.Append(new MatchParticipant(playerId, player.Name)));
        Events.Record(EventCategory.Session, "Match reservation abandoned", actor: playerId);
        return true;
    }

    /// <summary>Atomically rebinds a disconnected player using trusted identity and the last connection generation.</summary>
    /// <param name="peer">Fresh actual peer; retired handles cannot be reused.</param>
    /// <param name="session">Expected logical session.</param>
    /// <param name="playerId">Previously assigned player.</param>
    /// <param name="generation">Last accepted connection generation.</param>
    /// <param name="identity">Subject authenticated outside Core, never a wire claim.</param>
    /// <returns>Whether the existing slot was rebound exactly once.</returns>
    public bool Resume(ulong peer, ulong session, ulong playerId, ulong generation, string identity)
    {
        SessionPlayer? player = State.Players.SingleOrDefault(value => value.Id == playerId);
        if (State.ReconnectPolicy != SessionReconnectPolicy.RetainedResume || peer == 0 || session != State.Session || player is null || player.Connected || player.Generation != generation ||
            generation == ulong.MaxValue ||
            !_identities.TryGetValue(playerId, out string? subject) || subject != identity || _peers.ContainsKey(peer) || peer <= _previousPeers.GetValueOrDefault(playerId))
        {
            return false;
        }

        _peers.Add(peer, playerId);
        Events.Record(EventCategory.Network, "Reconnected", actor: playerId);
        _previousPeers.Remove(playerId);
        Publish(State.Players.Select(value => value.Id == playerId ? value with { Connected = true, Ready = false, Generation = generation + 1 } : value));
        return true;
    }

    /// <summary>Resolves sender ownership without trusting a player claim.</summary>
    /// <param name="peer">Actual transport sender, zero for local host.</param>
    /// <returns>Assigned identity or zero if unknown.</returns>
    public ulong PlayerId(ulong peer) => peer == 0 ? State.CurrentHostId : _peers.GetValueOrDefault(peer);

    /// <summary>Rejects stale phase intents before dispatching sender-scoped lobby actions.</summary>
    /// <param name="peer">Actual sender; zero only for the local host.</param>
    /// <param name="command">Requested action.</param>
    /// <param name="session">Expected session lifetime.</param>
    /// <param name="match">Expected arena generation.</param>
    /// <param name="phase">Expected lifecycle phase.</param>
    /// <param name="ready">Requested ready state.</param>
    /// <param name="connectedPeers">Transport roster including pending admission.</param>
    /// <param name="authorityEpoch">Expected current authority fence.</param>
    /// <returns>Whether the intent is legal at the current boundary.</returns>
    public bool Execute(ulong peer, LobbyCommand command, ulong session, ulong match, SessionPhase phase, bool ready, IEnumerable<ulong> connectedPeers, ulong authorityEpoch = 1)
    {
        if (session != State.Session || authorityEpoch != State.AuthorityEpoch)
        {
            return false;
        }

        if (command == LobbyCommand.Leave)
        {
            return Remove(peer);
        }

        if (match != State.Match || phase != State.Phase)
        {
            return false;
        }

        return command switch
        {
            LobbyCommand.Ready => SetReady(peer, ready),
            LobbyCommand.Start => Start(peer, connectedPeers),
            LobbyCommand.Return => Return(peer),
            _ => false,
        };
    }

    private void RemovePlayer(ulong id)
    {
        _identities.Remove(id);
        _previousPeers.Remove(id);
        Publish(State.Players.Where(player => player.Id != id));
    }

    private void Publish(IEnumerable<SessionPlayer> players) => State = new LobbySnapshot(State.Session, checked(State.Revision + 1), State.Match, State.Phase, players, State.CurrentHostId, State.AuthorityEpoch, State.Departed);
}
