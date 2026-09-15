using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
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
    private ulong _itemPublication;
    private ulong _publishedItemRevision = ulong.MaxValue;
    private ulong _publishedSpawnRevision = ulong.MaxValue;
    private int _publishedPeerCount = -1;

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
    /// <summary>Reconstructs frozen client collision bodies before prediction.</summary>
    internal event Action<Trackstorm.Core.Arenas.ArenaPropSnapshot>? PropsReceived;
    /// <summary>Reliable item and outcome presentation callback.</summary>
    internal event Action<ItemPublication>? ItemsReceived;
    /// <summary>Authoritative native prop observation seam, absent in flat-ground vehicle unit tests.</summary>
    internal Func<IReadOnlyList<VehiclePhysicsState>>? ObserveProps { get; set; }
    /// <summary>Latest accepted complete prop publication.</summary>
    internal Trackstorm.Core.Arenas.ArenaPropSnapshot? PropSnapshot { get; private set; }
    /// <summary>Native swept collision query, host only.</summary>
    internal Func<MissileState, System.Numerics.Vector3, float?>? CollideMissile { get; set; }
    /// <summary>Host native proximity candidates; Core revalidates each contact.</summary>
    internal Func<IReadOnlyList<(string Spawn, ulong Vehicle)>>? ObservePickups { get; set; }
    /// <summary>Latest complete reliable item state.</summary>
    internal ItemPublication? ItemState { get; private set; }
    /// <summary>Current local slot; no predicted consumption.</summary>
    internal ItemSlot? LocalItem => (Host?.Items.Slots ?? ItemState?.Slots)?.SingleOrDefault(slot => slot.Vehicle == LocalVehicleId);
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
            if ((input.Pressed & InputButtons.UseItem) != 0)
            {
                RequestItemUse();
            }

            Host.Step(input, observe, CollideMissile);
            if (Host.Spawns is not null && ObservePickups is not null)
            {
                foreach (var contact in ObservePickups().Distinct().OrderBy(contact => contact.Spawn, StringComparer.Ordinal).ThenBy(contact => contact.Vehicle))
                {
                    Host.Spawns.TryPickup(Host.World, contact.Spawn, contact.Vehicle);
                }
            }

            Latest = Host.Snapshot();
            if (_publishedSpawnRevision != (Host.Spawns?.Revision ?? 0) || _publishedItemRevision != Host.Items.Revision || _publishedPeerCount != _assigned.Count)
            {
                ItemState = new ItemPublication(++_itemPublication, Latest, Host.Items.Slots, Host.Items.Missiles, Host.Items.Events, Host.Spawns?.States);
                byte[] items = ItemCodec.EncodeState(ItemState);
                foreach (ulong peer in _assigned)
                {
                    Send(new TransportMessage(peer, items, TransportDelivery.Reliable));
                }

                _publishedSpawnRevision = Host.Spawns?.Revision ?? 0;
                _publishedItemRevision = Host.Items.Revision;
                _publishedPeerCount = _assigned.Count;
                ItemsReceived?.Invoke(ItemState);
            }

            if (Host.World.State.Tick % HostVehicleSession.SnapshotInterval == 0)
            {
                byte[] payload = VehicleNetworkCodec.EncodeSnapshot(Latest);
                byte[]? props = null;
                if (ObserveProps is not null)
                {
                    PropSnapshot = new Trackstorm.Core.Arenas.ArenaPropSnapshot(_session, Host.World.State.Tick, ObserveProps());
                    props = VehicleNetworkCodec.EncodeProps(PropSnapshot);
                }

                foreach (ulong peer in _assigned)
                {
                    Send(new TransportMessage(peer, payload, TransportDelivery.Unreliable));
                    if (props is not null)
                    {
                        Send(new TransportMessage(peer, props, TransportDelivery.Unreliable));
                    }
                }
            }
        }
        else if (Inputs is InputHistory inputs)
        {
            if ((input.Pressed & InputButtons.UseItem) != 0)
            {
                RequestItemUse();
            }

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

            Send(new TransportMessage(_serverPeer, VehicleNetworkCodec.EncodeInputs(_session, inputs.GetRedundancy()), TransportDelivery.Unreliable));
        }
    }

    /// <summary>Submits the local slot capability reliably; never creates a predicted item effect.</summary>
    /// <returns>Whether queued locally or sent to the host.</returns>
    internal bool RequestItemUse()
    {
        ItemSlot? slot = LocalItem;
        if (!IsActive || Failure.Length > 0 || slot is null || slot.Item == HeldItem.None)
        {
            return false;
        }

        if (Host is not null)
        {
            return Host.UseItem(0, _session, slot.Life, slot.Token);
        }

        return Send(new TransportMessage(_serverPeer, ItemCodec.EncodeUse(_session, slot.Life, slot.Token), TransportDelivery.Reliable));
    }

    private bool Send(TransportMessage message)
    {
        if (!_gateway.Connections.TryGetValue(message.RemotePeerId, out var state) || state != TransportConnectionState.Connected)
        {
            return false;
        }

        try
        {
            _gateway.Send(message);
            return true;
        }
        catch (InvalidOperationException)
        {
            // Native closure can race the preceding Poll, including during a multi-packet publication.
            _gateway.Disconnect(message.RemotePeerId);
            if (Host is null)
            {
                Failure = "Host connection ended while sending; rejoin the lobby.";
            }

            return false;
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
            Send(new TransportMessage(peer, VehicleNetworkCodec.EncodeWelcome(_session, vehicle), TransportDelivery.Reliable));
        }
    }

    private bool AcceptSnapshot(WorldSnapshot snapshot, Func<VehicleSnapshot, VehicleObservation> observe)
    {
        ReplicatedVehicle? local = snapshot.Vehicles.SingleOrDefault(vehicle => vehicle.State.VehicleId == LocalVehicleId);
        if (local is null || Inputs is not InputHistory inputs || !inputs.CanAcknowledge(local.AcknowledgedInput) || History is null || !History.Add(snapshot))
        {
            return false;
        }

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
        return true;
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
            if (ItemCodec.IsItem(message.Payload.Span))
            {
                if (message.Delivery != TransportDelivery.Reliable)
                {
                    throw new ArgumentException("Items require reliable delivery.");
                }

                if (Host is not null)
                {
                    var request = ItemCodec.DecodeUse(message.Payload.Span);
                    if (!Host.UseItem(message.RemotePeerId, request.Session, request.Life, request.Token))
                    {
                        RejectedPackets++;
                    }

                    return;
                }

                if (message.RemotePeerId != _serverPeer || History is null)
                {
                    throw new ArgumentException("Only the assigned host can publish item outcomes.");
                }

                ItemPublication publication = ItemCodec.DecodeState(message.Payload.Span);
                if (publication.World.Session != _session || publication.Revision <= (ItemState?.Revision ?? 0))
                {
                    throw new ArgumentException("Stale item publication.");
                }

                ItemState = publication;
                AcceptSnapshot(publication.World, observe);
                ItemsReceived?.Invoke(publication);
                return;
            }

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
                if (kind == VehicleNetworkCodec.Props && message.Delivery == TransportDelivery.Unreliable && _session != 0)
                {
                    var props = VehicleNetworkCodec.DecodeProps(message.Payload.Span);
                    if (props.Session == _session && (PropSnapshot is null || props.Tick > PropSnapshot.Tick))
                    {
                        PropSnapshot = props;
                        PropsReceived?.Invoke(props);
                        return;
                    }
                }

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
                    if (AcceptSnapshot(snapshot, observe))
                    {
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
