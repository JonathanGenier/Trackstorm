namespace Trackstorm.Core.Sessions;

/// <summary>Sole owner of admission, sender identity, readiness and session transitions.</summary>
public sealed class LobbyAuthority
{
    private readonly Dictionary<ulong, ulong> _peers = new();
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
    }

    /// <summary>Current immutable authority boundary.</summary>
    public LobbySnapshot State { get; private set; }
    /// <summary>Copy of transport-to-player assignments for vehicle integration.</summary>
    public IReadOnlyDictionary<ulong, ulong> Peers => new Dictionary<ulong, ulong>(_peers);

    /// <summary>Assigns a fresh identity to a connected transport sender.</summary>
    /// <param name="peer">Actual nonzero transport sender.</param>
    /// <param name="name">Requested display name.</param>
    /// <returns>Accepted player ID, or zero for rejected admission.</returns>
    public ulong Join(ulong peer, string name)
    {
        ulong id = checked(_nextId + 1);
        return Add(peer, id, name) ? id : 0;
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

        State = new LobbySnapshot(State.Session, checked(State.Revision + 1), checked(State.Match + 1), SessionPhase.Arena, State.Players);
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

        State = new LobbySnapshot(State.Session, checked(State.Revision + 1), State.Match, SessionPhase.Lobby, State.Players.Select(player => player with { Ready = false }));
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

        Publish(State.Players.Where(player => player.Id != id));
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
        if (session != State.Session || match != State.Match || phase != State.Phase)
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

    private void Publish(IEnumerable<SessionPlayer> players) => State = new LobbySnapshot(State.Session, checked(State.Revision + 1), State.Match, State.Phase, players);
}
