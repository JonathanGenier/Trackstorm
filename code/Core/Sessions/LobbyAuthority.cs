using Trackstorm.Core.Events;

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
    /// <param name="gameVersion">Hosted build; defaults to this runtime.</param>
    public LobbyAuthority(ulong session, string name, ulong graceTicks = 1800, GameVersion? gameVersion = null)
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

        Version = gameVersion ?? GameVersion.Current;
        GraceTicks = graceTicks;
        Events.PlayerName = id => State.Players.SingleOrDefault(player => player.Id == id)?.Name ?? $"Player {id}";
        Events.Record(EventCategory.Session, "Created", actor: 1);
        Events.Record(EventCategory.Session, "Joined", actor: 1);
    }

    /// <summary>Immutable version of this hosted session.</summary>
    public GameVersion Version { get; }

    /// <summary>Current immutable authority boundary.</summary>
    public LobbySnapshot State { get; private set; }
    /// <summary>Configured reservation in caller-supplied 60 Hz ticks.</summary>
    public ulong GraceTicks { get; }
    /// <summary>Authoritative session journal shared with arena gameplay.</summary>
    public EventStream Events { get; } = new();
    /// <summary>Copy of transport-to-player assignments for vehicle integration.</summary>
    public IReadOnlyDictionary<ulong, ulong> Peers => new Dictionary<ulong, ulong>(_peers);

    /// <summary>Assigns a fresh identity to a connected transport sender.</summary>
    /// <param name="peer">Actual nonzero transport sender.</param>
    /// <param name="gameVersion">Joining or returning runtime version.</param>
    /// <param name="name">Requested display name.</param>
    /// <returns>Accepted player ID, or zero for rejected admission.</returns>
    /// <param name="identity">Optional authenticated, provider-neutral subject supplied by trusted integration.</param>
    public ulong Join(ulong peer, string gameVersion, string name, string? identity = null)
    {
        if (!Version.IsCompatible(gameVersion))
        {
            return 0;
        }

        if (identity is not null && (identity.Length is 0 or > 256 || _identities.ContainsValue(identity)))
        {
            return 0;
        }

        ulong id = checked(_nextId + 1);
        if (!Add(peer, gameVersion, id, name))
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
    /// <param name="gameVersion">Joining or returning runtime version.</param>
    /// <param name="id">Host-assigned identity.</param>
    /// <param name="name">Untrusted name.</param>
    /// <returns>Whether admission succeeded.</returns>
    public bool Add(ulong peer, string gameVersion, ulong id, string name)
    {
        if (!Version.IsCompatible(gameVersion) || peer == 0 || id <= _nextId || id == ulong.MaxValue || _peers.ContainsKey(peer) || State.Phase != SessionPhase.Lobby || State.Players.Count == 8)
        {
            return false;
        }

        _peers.Add(peer, id);
        _nextId = Math.Max(_nextId, id);
        Publish(State.Players.Append(new SessionPlayer(id, PlayerName.Sanitize(name), false)));
        Events.Record(EventCategory.Session, "Joined", actor: id);
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

        State = new LobbySnapshot(State.Session, checked(State.Revision + 1), checked(State.Match + 1), SessionPhase.Arena, State.Players, GraceTicks);
        Events.Record(EventCategory.Session, "Arena started", actor: 1);
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

        State = new LobbySnapshot(State.Session, checked(State.Revision + 1), State.Match, SessionPhase.Lobby, State.Players.Select(player => player with { Ready = false }), GraceTicks);
        Events.Record(EventCategory.Session, "Returned to lobby", actor: 1);
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

        Events.Record(EventCategory.Session, "Left", actor: id);
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

        Events.Record(EventCategory.Network, "Disconnected", actor: id, cause: "connection lost");
        if (!_identities.ContainsKey(id))
        {
            RemovePlayer(id);
            return true;
        }

        Events.Record(EventCategory.Network, "Grace entered", actor: id);
        _deadlines.Add(id, checked(_tick + GraceTicks));
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
        Events.AdvanceTime(tick * 1000 / 60);
        foreach (ulong id in _deadlines.Where(pair => pair.Value <= tick).Select(pair => pair.Key).ToArray())
        {
            Events.Record(EventCategory.Network, "Grace expired; player removed", actor: id);
            RemovePlayer(id);
        }
    }

    /// <summary>Resolves a retained subject without exposing platform identity in gameplay records.</summary>
    /// <param name="identity">Trusted authenticated subject.</param>
    /// <returns>Reserved player, or zero when absent/expired.</returns>
    public ulong FindPlayer(string identity) => _identities.FirstOrDefault(pair => pair.Value == identity).Key;

    /// <summary>Atomically rebinds a disconnected player using trusted identity and the last connection generation.</summary>
    /// <param name="peer">Fresh actual peer; retired handles cannot be reused.</param>
    /// <param name="gameVersion">Joining or returning runtime version.</param>
    /// <param name="session">Expected logical session.</param>
    /// <param name="playerId">Previously assigned player.</param>
    /// <param name="generation">Last accepted connection generation.</param>
    /// <param name="identity">Subject authenticated outside Core, never a wire claim.</param>
    /// <returns>Whether the existing slot was rebound exactly once.</returns>
    public bool Resume(ulong peer, string gameVersion, ulong session, ulong playerId, ulong generation, string identity)
    {
        SessionPlayer? player = State.Players.SingleOrDefault(value => value.Id == playerId);
        if (!Version.IsCompatible(gameVersion) || peer == 0 || session != State.Session || player is null || player.Connected || player.Generation != generation ||
            generation == ulong.MaxValue || !_deadlines.TryGetValue(playerId, out ulong deadline) || deadline <= _tick ||
            !_identities.TryGetValue(playerId, out string? subject) || subject != identity || _peers.ContainsKey(peer) || peer <= _previousPeers.GetValueOrDefault(playerId))
        {
            return false;
        }

        _peers.Add(peer, playerId);
        Events.Record(EventCategory.Network, "Reconnected", actor: playerId);
        _deadlines.Remove(playerId);
        _previousPeers.Remove(playerId);
        Publish(State.Players.Select(value => value.Id == playerId ? value with { Connected = true, Ready = false, Generation = generation + 1 } : value));
        return true;
    }

    /// <summary>Resolves sender ownership without trusting a player claim.</summary>
    /// <param name="peer">Actual transport sender, zero for local host.</param>
    /// <returns>Assigned identity or zero if unknown.</returns>
    public ulong PlayerId(ulong peer) => peer == 0 ? 1 : _peers.GetValueOrDefault(peer);

    /// <summary>Rejects stale phase intents before dispatching sender-scoped lobby actions.</summary>
    /// <param name="peer">Actual sender; zero only for the local host.</param>
    /// <param name="command">Requested action.</param>
    /// <param name="session">Expected session lifetime.</param>
    /// <param name="match">Expected arena generation.</param>
    /// <param name="phase">Expected lifecycle phase.</param>
    /// <param name="ready">Requested ready state.</param>
    /// <param name="connectedPeers">Transport roster including pending admission.</param>
    /// <returns>Whether the intent is legal at the current boundary.</returns>
    public bool Execute(ulong peer, LobbyCommand command, ulong session, ulong match, SessionPhase phase, bool ready, IEnumerable<ulong> connectedPeers)
    {
        if (session != State.Session)
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

    private void Publish(IEnumerable<SessionPlayer> players) => State = new LobbySnapshot(State.Session, checked(State.Revision + 1), State.Match, State.Phase, players, GraceTicks);
}
