namespace Trackstorm.Core.Networking.Transport;

/// <summary>Owns admission and valid peer lifecycle transitions, independent of sockets and entity state.</summary>
public sealed class TransportConnections
{
    /// <summary>The host occupies one of the eight player slots.</summary>
    public const int MaximumRemotePlayers = 7;

    private readonly Dictionary<ulong, TransportConnectionState> _states = [];
    private ulong _nextId = 1;

    /// <summary>Gets the number of reserved and connected peer slots.</summary>
    public int Count => _states.Count;

    /// <summary>Gets a detached snapshot of active peer identities and states.</summary>
    public IReadOnlyDictionary<ulong, TransportConnectionState> Snapshot => new Dictionary<ulong, TransportConnectionState>(_states);

    /// <summary>Reserves a slot before accepting a connection; rejected admission never consumes an ID.</summary>
    /// <param name="peerId">The assigned peer identity, or zero on rejection.</param>
    /// <returns>Whether a slot was reserved.</returns>
    public bool TryAdmit(out ulong peerId)
    {
        peerId = 0;
        if (_states.Count >= MaximumRemotePlayers || _nextId == ulong.MaxValue)
        {
            return false;
        }

        peerId = _nextId++;
        _states.Add(peerId, TransportConnectionState.Connecting);
        return true;
    }

    /// <summary>Applies only connecting-to-connected or active-to-disconnected transitions. Closed IDs never revive.</summary>
    /// <param name="peerId">The peer whose state changes.</param>
    /// <param name="next">The requested state.</param>
    /// <returns>Whether the transition was valid and applied.</returns>
    public bool TryTransition(ulong peerId, TransportConnectionState next)
    {
        if (!_states.TryGetValue(peerId, out TransportConnectionState current))
        {
            return false;
        }

        if (next == TransportConnectionState.Disconnected)
        {
            return _states.Remove(peerId);
        }

        if (current != TransportConnectionState.Connecting || next != TransportConnectionState.Connected)
        {
            return false;
        }

        _states[peerId] = next;
        return true;
    }

    /// <summary>Reports whether a peer can currently carry messages.</summary>
    /// <param name="peerId">The local peer identity.</param>
    /// <returns>Whether the peer is connected.</returns>
    public bool IsConnected(ulong peerId) => _states.GetValueOrDefault(peerId) == TransportConnectionState.Connected;
}
