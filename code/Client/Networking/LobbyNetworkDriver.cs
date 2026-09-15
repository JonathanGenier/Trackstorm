using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Networking;

/// <summary>Routes reliable session intents and publications over the existing caller-owned gateway.</summary>
internal sealed class LobbyNetworkDriver
{
    private readonly ITransportGateway _gateway;
    private readonly string _name;
    private readonly Func<ulong, bool>? _admission;
    private readonly ulong _expectedSession;
    private readonly LobbyReplica _replica = new();
    private bool _joined;
    private ulong _published;
    private double _joiningSeconds;

    /// <summary>Creates a host lobby or a client waiting for admission.</summary>
    /// <param name="gateway">Existing transport.</param>
    /// <param name="session">Host lifetime, zero on a client.</param>
    /// <param name="serverPeer">Client's actual server connection.</param>
    /// <param name="name">Local name request.</param>
    /// <param name="admission">Optional online admission gate; direct-IP retains development admission.</param>
    /// <param name="expectedSession">Online clients require this discovered session lifetime; zero retains development behavior.</param>
    internal LobbyNetworkDriver(ITransportGateway gateway, ulong session, ulong serverPeer, string name, Func<ulong, bool>? admission = null, ulong expectedSession = 0)
    {
        _gateway = gateway;
        _name = name;
        _admission = admission;
        _expectedSession = expectedSession;
        ServerPeer = serverPeer;
        if (session != 0)
        {
            Authority = new LobbyAuthority(session, name);
        }
    }

    /// <summary>Host-owned rules; absent on clients.</summary>
    internal LobbyAuthority? Authority { get; }
    /// <summary>Latest complete authoritative state.</summary>
    internal LobbySnapshot? State => Authority?.State ?? _replica.State;
    /// <summary>Session-stable local identity, zero before admission.</summary>
    internal ulong LocalPlayerId => Authority is null ? _replica.PlayerId : 1;
    /// <summary>Actual host transport peer on a client.</summary>
    internal ulong ServerPeer { get; }
    /// <summary>Terminal connection failure, rendered by the session UI.</summary>
    internal string Failure { get; private set; } = string.Empty;
    /// <summary>Rejected malformed or unauthorized intents/publications.</summary>
    internal int RejectedPackets { get; private set; }

    /// <summary>Pumps the single gateway and optionally routes non-lobby packets into the active vehicle driver.</summary>
    /// <param name="seconds">Elapsed monotonic time for admission timeout.</param>
    /// <param name="vehicleMessage">Consumer for an active arena only.</param>
    internal void Pump(double seconds, Action<TransportMessage>? vehicleMessage = null)
    {
        _gateway.Poll();
        if (Authority is not null)
        {
            foreach (ulong peer in Authority.Peers.Keys)
            {
                if (!_gateway.Connections.TryGetValue(peer, out var connection) || connection != TransportConnectionState.Connected)
                {
                    Authority.Remove(peer);
                }
            }
        }
        else
        {
            _joiningSeconds += seconds;
            if (!_gateway.Connections.TryGetValue(ServerPeer, out var connection) || connection == TransportConnectionState.Disconnected)
            {
                Failure = "Host disconnected or refused admission. Leave and join a new lobby.";
            }
            else if (!_joined && connection == TransportConnectionState.Connected)
            {
                Send(ServerPeer, LobbyCodec.EncodeCommand(LobbyCommand.Join, null, name: _name));
                _joined = true;
            }

            if (State is null && _joiningSeconds > 15)
            {
                Failure = "Lobby admission timed out. Check connectivity and rejoin the session.";
                _gateway.Disconnect(ServerPeer);
            }
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
            else if (State?.Phase == SessionPhase.Arena)
            {
                vehicleMessage?.Invoke(message);
            }
        }

        Publish();
    }

    /// <summary>Dispatches local user intent through the same authoritative rules as remote requests.</summary>
    /// <param name="command">Requested transition or readiness.</param>
    /// <param name="ready">Desired ready state.</param>
    /// <returns>Whether locally accepted or submitted to the host.</returns>
    internal bool Request(LobbyCommand command, bool ready = false)
    {
        if (Failure.Length > 0 || State is null)
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
                if (intent.Command == LobbyCommand.Join)
                {
                    if ((_admission is not null && !_admission(message.RemotePeerId)) || Authority.Join(message.RemotePeerId, intent.Name) == 0)
                    {
                        _gateway.Disconnect(message.RemotePeerId);
                    }

                    return;
                }

                if (!Authority.Execute(message.RemotePeerId, intent.Command, intent.Session, intent.Match, intent.Phase, intent.Ready, ConnectedPeers()))
                {
                    throw new ArgumentException("Rejected stale or unauthorized lobby intent.");
                }
            }
            else if (message.RemotePeerId == ServerPeer)
            {
                var publication = LobbyCodec.DecodeState(message.Payload.Span);
                if (_expectedSession != 0 && publication.State.Session != _expectedSession)
                {
                    throw new ArgumentException("Lobby publication does not match the discovered online session.");
                }

                if (!_replica.Accept(publication.State, publication.Player, message.RemotePeerId, ServerPeer))
                {
                    throw new ArgumentException("Rejected stale or reassigned session state.");
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
            Authority?.Remove(peer);
            if (Authority is null)
            {
                Failure = "Host connection could not accept lobby commands. Leave and reconnect.";
            }
        }
    }
}
