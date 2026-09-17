namespace Trackstorm.Core.Sessions;

/// <summary>Sole owner of admission, sender identity, readiness and session transitions.</summary>
public sealed class LobbyAuthority
{
    private readonly Dictionary<ulong, ulong> _peers = new();
    private readonly Dictionary<ulong, string> _identities = new();
    private readonly Dictionary<ulong, ulong> _deadlines = new();
    private readonly Dictionary<ulong, ulong> _previousPeers = new();
    private ulong _tick;
    private ulong _nextId = 1;

    /// <summary>Creates the host's unready lobby.</summary>
    /// <param name="session">Nonzero lifetime leaving room for future match generations.</param>
    /// <param name="name">Untrusted host display name.</param>
    /// <param name="graceTicks">Reconnect reservation duration at the caller's fixed 60 Hz clock.</param>
    public LobbyAuthority(ulong session, string name, ulong graceTicks = 1800)
    {
        if (session == ulong.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(session));
        }

        State = new LobbySnapshot(session, 1, session, SessionPhase.Lobby, new[] { new SessionPlayer(1, PlayerName.Sanitize(name), false) }, graceTicks);
        if (graceTicks is 0 or > 216000)
        {
            throw new ArgumentOutOfRangeException(nameof(graceTicks));
        }

        GraceTicks = graceTicks;
    }

    /// <summary>Current immutable authority boundary.</summary>
    public LobbySnapshot State { get; private set; }
    /// <summary>Configured reservation in caller-supplied 60 Hz ticks.</summary>
    public ulong GraceTicks { get; }
    /// <summary>Copy of transport-to-player assignments for vehicle integration.</summary>
    public IReadOnlyDictionary<ulong, ulong> Peers => new Dictionary<ulong, ulong>(_peers);
    /// <summary>Session tuning; the successor restores this instead of loading its host-local preferences.</summary>
    public Development.GameplayConfigurationState Configuration { get; private set; } = new(0, new());

    /// <summary>Builds a replacement with all remote players reserved for authenticated fresh connections.</summary>
    /// <param name="checkpoint">Validated old authority boundary.</param>
    /// <param name="host">Elected stable identity.</param>
    /// <param name="epoch">Exactly the next authority epoch.</param>
    /// <returns>New authority with Ready cleared and no inherited transport handles.</returns>
    public static LobbyAuthority Restore(LobbyRestoreState checkpoint, ulong host, ulong epoch)
    {
        var previous = checkpoint.State;
        if (epoch != checked(previous.AuthorityEpoch + 1) || host == previous.CurrentHostId || !previous.Players.Any(player => player.Id == host && player.Connected))
        {
            throw new ArgumentException("Invalid authority transition.");
        }

        var result = new LobbyAuthority(previous.Session, previous.Players.Single(player => player.Id == host).Name, previous.GraceTicks)
        {
            _tick = checkpoint.Tick,
            _nextId = checkpoint.NextId,
            Configuration = checkpoint.Configuration,
            State = new LobbySnapshot(previous.Session, checked(previous.Revision + 1), previous.Match, previous.Phase, previous.Players.Select(player => player with { Ready = false, Connected = player.Id == host, RetainedHost = player.RetainedHost || player.Id == previous.CurrentHostId }), previous.GraceTicks, host, epoch),
        };
        foreach (var subject in checkpoint.Subjects)
        {
            result._identities.Add(subject.Key, subject.Value);
            if (subject.Key != host)
            {
                result._deadlines.Add(subject.Key, result.State.Players.Single(player => player.Id == subject.Key).RetainedHost ? ulong.MaxValue : checkpoint.Deadlines.GetValueOrDefault(subject.Key, checked(checkpoint.Tick + previous.GraceTicks)));
            }
        }

        return result;
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
        return new LobbyRestoreState(State, _tick, _nextId, subjects, _deadlines, Configuration);
    }

    /// <summary>Assigns a fresh identity to a connected transport sender.</summary>
    /// <param name="peer">Actual nonzero transport sender.</param>
    /// <param name="name">Requested display name.</param>
    /// <returns>Accepted player ID, or zero for rejected admission.</returns>
    /// <param name="identity">Optional authenticated, provider-neutral subject supplied by trusted integration.</param>
    public ulong Join(ulong peer, string name, string? identity = null)
    {
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
        if (peer == 0 || id <= _nextId || id == ulong.MaxValue || _peers.ContainsKey(peer) || State.Phase != SessionPhase.Lobby || State.Players.Count == 8)
        {
            return false;
        }

        _peers.Add(peer, id);
        _nextId = Math.Max(_nextId, id);
        Publish(State.Players.Append(new SessionPlayer(id, PlayerName.Sanitize(name), false)));
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

        State = new LobbySnapshot(State.Session, checked(State.Revision + 1), checked(State.Match + 1), SessionPhase.Arena, State.Players, GraceTicks, State.CurrentHostId, State.AuthorityEpoch);
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

        foreach (ulong id in State.Players.Where(player => player.RetainedHost && !player.Connected).Select(player => player.Id).ToArray())
        {
            RemovePlayer(id);
        }

        State = new LobbySnapshot(State.Session, checked(State.Revision + 1), State.Match, SessionPhase.Lobby, State.Players.Select(player => player with { Ready = false, RetainedHost = false }), GraceTicks, State.CurrentHostId, State.AuthorityEpoch);
        return true;
    }

    /// <summary>Removes a disconnected player's entire record in either phase.</summary>
    /// <param name="peer">Departed transport sender.</param>
    /// <returns>Whether an existing player was removed.</returns>
    public bool Remove(ulong peer)
    {
        if (!_peers.Remove(peer, out ulong id))
        {
            return false;
        }

        RemovePlayer(id);
        return true;
    }

    /// <summary>Retires a lost peer immediately and reserves only authenticated players for resume.</summary>
    /// <param name="peer">Actual lost connection.</param>
    /// <returns>Whether an active binding was retired.</returns>
    public bool Disconnect(ulong peer)
    {
        if (!_peers.Remove(peer, out ulong id))
        {
            return false;
        }

        if (!_identities.ContainsKey(id))
        {
            RemovePlayer(id);
            return true;
        }

        _deadlines.Add(id, State.Players.Single(player => player.Id == id).RetainedHost ? ulong.MaxValue : checked(_tick + GraceTicks));
        _previousPeers[id] = peer;
        Publish(State.Players.Select(player => player.Id == id ? player with { Ready = false, Connected = false } : player));
        return true;
    }

    /// <summary>Advances explicit monotonic time; an exact deadline expires before a resume may succeed.</summary>
    /// <param name="tick">Caller-owned 60 Hz session clock, including time spent in lobby.</param>
    public void AdvanceTime(ulong tick)
    {
        if (tick < _tick)
        {
            throw new ArgumentOutOfRangeException(nameof(tick));
        }

        _tick = tick;
        foreach (ulong id in _deadlines.Where(pair => pair.Value <= tick).Select(pair => pair.Key).ToArray())
        {
            RemovePlayer(id);
        }
    }

    /// <summary>Resolves a retained subject without exposing platform identity in gameplay records.</summary>
    /// <param name="identity">Trusted authenticated subject.</param>
    /// <returns>Reserved player, or zero when absent/expired.</returns>
    public ulong FindPlayer(string identity) => _identities.FirstOrDefault(pair => pair.Value == identity).Key;

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
        if (peer == 0 || session != State.Session || player is null || player.Connected || player.Generation != generation ||
            generation == ulong.MaxValue || !_deadlines.TryGetValue(playerId, out ulong deadline) || deadline <= _tick ||
            !_identities.TryGetValue(playerId, out string? subject) || subject != identity || _peers.ContainsKey(peer) || peer <= _previousPeers.GetValueOrDefault(playerId))
        {
            return false;
        }

        _peers.Add(peer, playerId);
        _deadlines.Remove(playerId);
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
        _deadlines.Remove(id);
        _previousPeers.Remove(id);
        Publish(State.Players.Where(player => player.Id != id));
    }

    private void Publish(IEnumerable<SessionPlayer> players) => State = new LobbySnapshot(State.Session, checked(State.Revision + 1), State.Match, State.Phase, players, GraceTicks, State.CurrentHostId, State.AuthorityEpoch);
}
