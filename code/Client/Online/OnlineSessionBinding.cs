using Trackstorm.Client.Networking;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Client.Online;

/// <summary>Maps transport-authenticated online members to authority-assigned PlayerIds after credential admission.</summary>
internal sealed class OnlineSessionBinding : IDisposable
{
    private readonly ITransportGateway _gateway;
    private readonly OnlineLobbyCoordinator _coordinator;
    private readonly Dictionary<ulong, OnlineProductUserId> _authorized = new();
    private bool _disposed;

    /// <summary>Binds a caller-owned authenticated transport to the existing authoritative lobby driver.</summary>
    /// <param name="coordinator">Owner of the active online membership.</param>
    /// <param name="gateway">Separately supplied authenticated packet gateway.</param>
    /// <param name="serverPeer">Established server peer on clients, zero for a host.</param>
    /// <param name="playerName">Requested local gameplay display name.</param>
    internal OnlineSessionBinding(OnlineLobbyCoordinator coordinator, ITransportGateway gateway, ulong serverPeer, string playerName)
    {
        _coordinator = coordinator;
        _gateway = gateway;
        _gateway.ConnectionChanged += OnConnectionChanged;
        var lobby = coordinator.Active ?? throw new InvalidOperationException("An online lobby is required before transport attachment.");
        Driver = new LobbyNetworkDriver(gateway, coordinator.IsHost ? lobby.Session : 0, serverPeer, playerName, peer => !_disposed && _authorized.ContainsKey(peer), lobby.Session, peer => _authorized.TryGetValue(peer, out var identity) ? identity.Value : null);
    }

    /// <summary>Existing driver which remains the sole route for lobby commands and roster admission.</summary>
    internal LobbyNetworkDriver Driver { get; }

    /// <summary>Online identities mapped to authority-assigned gameplay identities.</summary>
    internal IReadOnlyDictionary<OnlineProductUserId, ulong> PlayerIds
    {
        get
        {
            var result = new Dictionary<OnlineProductUserId, ulong>();
            if (_disposed)
            {
                return result;
            }

            if (Driver.LocalPlayerId != 0)
            {
                result[_coordinator.Identity] = Driver.LocalPlayerId;
            }

            if (Driver.Authority is not null)
            {
                foreach (var identity in _coordinator.Active?.MemberIds ?? [])
                {
                    ulong player = Driver.Authority.FindPlayer(identity.Value);
                    if (player != 0)
                    {
                        result[identity] = player;
                    }
                }
            }

            return result;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gateway.ConnectionChanged -= OnConnectionChanged;
        _authorized.Clear();
        _gateway.Stop();
    }

    /// <summary>Called by the separately supplied authenticated transport adapter, never from a claimed wire PUID.</summary>
    /// <param name="peer">Actual transport peer, never a user-supplied identity claim.</param>
    /// <param name="authenticatedIdentity">Online identity established by the transport adapter.</param>
    /// <param name="credential">Transient access code; never retained or logged.</param>
    /// <returns>The validated result, or an explicit failure/absence.</returns>
    internal bool AuthorizePeer(ulong peer, OnlineProductUserId authenticatedIdentity, string? credential)
    {
        var lobby = _coordinator.Active;
        ulong retained = Driver.Authority?.FindPlayer(authenticatedIdentity.Value) ?? 0;
        bool resumable = retained != 0 && Driver.Authority!.State.Players.Any(player => player.Id == retained && !player.Connected);
        bool accepted = !_disposed && _coordinator.IsHost && peer != 0 && lobby is not null && lobby.MemberIds.Contains(authenticatedIdentity)
            && !authenticatedIdentity.Equals(lobby.Owner) && !_authorized.Values.Contains(authenticatedIdentity)
            && !_authorized.ContainsKey(peer) && (resumable || (retained == 0 && (lobby.Access == LobbyAccess.Public || lobby.Credential!.Verify(credential))));
        if (!accepted)
        {
            _gateway.Disconnect(peer);
            return false;
        }

        _authorized.Add(peer, authenticatedIdentity);
        return true;
    }

    /// <summary>Disconnects departed online members; the normal driver removes their Core records.</summary>
    /// <param name="lobby">Current client-only lobby metadata.</param>
    internal void MembershipChanged(OnlineLobby lobby)
    {
        foreach (var peer in _authorized.ToArray())
        {
            if (!lobby.MemberIds.Contains(peer.Value))
            {
                _gateway.Disconnect(peer.Key);
                _authorized.Remove(peer.Key);
            }
        }
    }

    private void OnConnectionChanged(TransportConnectionChange change)
    {
        if (change.State == TransportConnectionState.Disconnected)
        {
            Driver.Authority?.Disconnect(change.RemotePeerId);
            _authorized.Remove(change.RemotePeerId);
        }
    }

}
