using Trackstorm.Core.Input;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Networking;

/// <summary>Connects the transport's byte seam to Core authority/prediction at fixed tick boundaries.</summary>
internal sealed class VehicleNetworkDriver
{
    private readonly ITransportGateway _gateway;
    private readonly ulong _serverPeer;
    private readonly HashSet<ulong> _assigned = new();
    private readonly LobbyNetworkDriver? _lobby;
    private InputHistory? _inputs;
    private ulong _session;
    private double _snapshotAge;

    /// <summary>Creates a host or connects a client driver to an already-open transport.</summary>
    /// <param name="gateway">Caller-owned transport.</param>
    /// <param name="hostSession">Nonzero host generation, or zero for a client.</param>
    /// <param name="serverPeer">Client's actual transport server identity.</param>
    /// <param name="lobby">Optional authoritative lobby which has entered an arena.</param>
    internal VehicleNetworkDriver(ITransportGateway gateway, ulong hostSession, ulong serverPeer = 0, LobbyNetworkDriver? lobby = null)
    {
        _lobby = lobby;
        _gateway = gateway;
        _serverPeer = serverPeer;
        _session = hostSession;
        if (hostSession != 0)
        {
            Host = new HostVehicleSession(hostSession);
            LocalVehicleId = 1;
        }

        if (lobby is not null)
        {
            if (lobby.State?.Phase != SessionPhase.Arena)
            {
                throw new ArgumentException("Vehicle gameplay requires an active arena.");
            }

            _session = lobby.State.Match;
            LocalVehicleId = lobby.LocalPlayerId;
            if (Host is not null)
            {
                foreach (var peer in lobby.Authority!.Peers)
                {
                    Host.JoinPlayer(peer.Key, peer.Value);
                    _assigned.Add(peer.Key);
                }
            }
            else
            {
                History = new SnapshotHistory(_session);
                _inputs = new InputHistory();
            }
        }
    }

    /// <summary>Ensures native collision proxies exist before the synchronous Core step/replay.</summary>
    internal event Action<WorldSnapshot>? RosterChanged;
    /// <summary>Signals authoritative correction before the next local prediction.</summary>
    internal event Action<VehicleSnapshot>? LocalCorrected;
    /// <summary>Host gameplay owner, or null on clients.</summary>
    internal HostVehicleSession? Host { get; }
    /// <summary>Client prediction, created only after reliable assignment and a valid snapshot.</summary>
    internal PredictedVehicle? Prediction { get; private set; }
    /// <summary>Bounded remote snapshot data.</summary>
    internal SnapshotHistory? History { get; private set; }
    /// <summary>Last accepted host roster.</summary>
    internal WorldSnapshot? Latest { get; private set; }
    /// <summary>Host-assigned local vehicle identity.</summary>
    internal ulong LocalVehicleId { get; private set; }
    /// <summary>Seconds since a valid snapshot arrived on a client, or null when this host-owned metric is not applicable.</summary>
    internal double? SnapshotAge => Host is null ? _snapshotAge : null;
    /// <summary>Sequenced commands retained after assignment, including before prediction can be initialized.</summary>
    internal InputHistory? Inputs => Prediction?.History ?? _inputs;
    /// <summary>Observed protocol rejection count.</summary>
    internal int RejectedPackets { get; private set; }
    /// <summary>Accepted authoritative snapshot count.</summary>
    internal int ReceivedSnapshots { get; private set; }
    /// <summary>Explicit stopped-session diagnostic, empty during normal operation.</summary>
    internal string Failure { get; private set; } = string.Empty;
    /// <summary>Whether this arena generation still belongs to the live lobby.</summary>
    internal bool IsActive => _lobby is null || (_lobby.Failure.Length == 0 && _lobby.State?.Phase == SessionPhase.Arena && _lobby.State.Match == _session);
    /// <summary>Current local gameplay state, independent of render smoothing.</summary>
    internal VehicleSnapshot? LocalState => Host?.World.GetVehicle(1) ?? Prediction?.State;

    /// <summary>Pumps transport, consumes authority updates, predicts immediately, and emits rate-limited snapshots.</summary>
    /// <param name="input">This fixed tick's local input.</param>
    /// <param name="observe">Synchronous native or deterministic test collision seam.</param>
    internal void Advance(InputFrame input, Func<VehicleSnapshot, VehicleObservation> observe)
    {
        if (_lobby is not null)
        {
            _lobby.Pump(1.0 / HostVehicleSession.TickRate, message => Receive(message, observe));
            if (_lobby.Failure.Length > 0 || _lobby.State?.Phase != SessionPhase.Arena || _lobby.State.Match != _session)
            {
                return;
            }
        }
        else
        {
            _gateway.Poll();
        }

        if (Host is not null)
        {
            SynchronizePeers();
        }
        else
        {
            _snapshotAge += 1.0 / HostVehicleSession.TickRate;
            if (!_gateway.Connections.TryGetValue(_serverPeer, out var state) || state == TransportConnectionState.Disconnected)
            {
                Failure = "Host disconnected; reconnect to start a new session.";
            }
        }

        while (_lobby is null && _gateway.TryReceive(out TransportMessage message))
        {
            Receive(message, observe);
        }

        if (Failure.Length > 0)
        {
            return;
        }

        if (Host is not null)
        {
            RosterChanged?.Invoke(Host.Snapshot());
            Host.Step(input, observe);
            Latest = Host.Snapshot();
            if (Host.World.State.Tick % HostVehicleSession.SnapshotInterval == 0)
            {
                byte[] payload = VehicleNetworkCodec.EncodeSnapshot(Latest);
                foreach (ulong peer in _assigned)
                {
                    _gateway.Send(new TransportMessage(peer, payload, TransportDelivery.Unreliable));
                }
            }
        }
        else if (Inputs is InputHistory inputs)
        {
            if (inputs.IsFull)
            {
                Failure = "Host acknowledgements stalled; reconnect to resynchronize.";
                _gateway.Disconnect(_serverPeer);
                return;
            }

            if (Prediction is null)
            {
                inputs.Add(input);
            }
            else
            {
                Prediction.Predict(input, observe);
            }

            _gateway.Send(new TransportMessage(_serverPeer, VehicleNetworkCodec.EncodeInputs(_session, inputs.GetRedundancy()), TransportDelivery.Unreliable));
        }
    }

    private void SynchronizePeers()
    {
        var connected = _gateway.Connections.Where(peer => peer.Value == TransportConnectionState.Connected).Select(peer => peer.Key).ToHashSet();
        if (_lobby is not null)
        {
            connected.IntersectWith(_lobby.Authority!.Peers.Keys);
        }

        foreach (ulong peer in _assigned.Except(connected).ToArray())
        {
            Host!.Leave(peer);
            _assigned.Remove(peer);
        }

        foreach (ulong peer in connected.Except(_assigned))
        {
            if (_lobby is not null)
            {
                continue;
            }

            ulong vehicle = Host!.Join(peer);
            if (vehicle == 0)
            {
                _gateway.Disconnect(peer);
                continue;
            }

            _assigned.Add(peer);
            _gateway.Send(new TransportMessage(peer, VehicleNetworkCodec.EncodeWelcome(_session, vehicle), TransportDelivery.Reliable));
        }
    }

    private void Receive(TransportMessage message, Func<VehicleSnapshot, VehicleObservation> observe)
    {
        if (!_gateway.Connections.TryGetValue(message.RemotePeerId, out var connection) || connection != TransportConnectionState.Connected)
        {
            RejectedPackets++;
            return;
        }

        try
        {
            byte kind = VehicleNetworkCodec.Kind(message.Payload.Span);
            if (Host is not null && kind == VehicleNetworkCodec.Inputs && message.Delivery == TransportDelivery.Unreliable)
            {
                var inputs = VehicleNetworkCodec.DecodeInputs(message.Payload.Span);
                if (!Host.Receive(message.RemotePeerId, inputs.Session, inputs.Inputs))
                {
                    RejectedPackets++;
                }

                return;
            }

            if (Host is null && message.RemotePeerId == _serverPeer)
            {
                if (kind == VehicleNetworkCodec.Welcome && message.Delivery == TransportDelivery.Reliable && _session == 0)
                {
                    var assignment = VehicleNetworkCodec.DecodeWelcome(message.Payload.Span);
                    _session = assignment.Session;
                    LocalVehicleId = assignment.Vehicle;
                    History = new SnapshotHistory(_session);
                    _inputs = new InputHistory();
                    return;
                }

                if (kind == VehicleNetworkCodec.Snapshot && message.Delivery == TransportDelivery.Unreliable && History is not null)
                {
                    WorldSnapshot snapshot = VehicleNetworkCodec.DecodeSnapshot(message.Payload.Span);
                    ReplicatedVehicle? local = snapshot.Vehicles.SingleOrDefault(vehicle => vehicle.State.VehicleId == LocalVehicleId);
                    if (local is not null && Inputs is InputHistory inputs && inputs.CanAcknowledge(local.AcknowledgedInput) && History.Add(snapshot))
                    {
                        Latest = snapshot;
                        RosterChanged?.Invoke(snapshot);
                        if (Prediction is null)
                        {
                            Prediction = new PredictedVehicle(local, inputs, observe);
                            _inputs = null;
                        }
                        else if (Prediction.Reconcile(local, observe))
                        {
                            LocalCorrected?.Invoke(Prediction.State);
                        }
                        else
                        {
                            RejectedPackets++;
                        }

                        _snapshotAge = 0;
                        ReceivedSnapshots++;
                        return;
                    }
                }
            }
        }
        catch (ArgumentException)
        {
            // A malformed or unsupported gameplay payload cannot partially reach the simulation.
        }

        RejectedPackets++;
    }
}
