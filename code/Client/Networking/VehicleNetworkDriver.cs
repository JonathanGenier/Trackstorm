using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Networking;

/// <summary>Connects the transport's byte seam to Core authority/prediction at fixed tick boundaries.</summary>
internal sealed class VehicleNetworkDriver : IDisposable
{
    private ulong _switchLife;
    private ulong _switchRevision;

    private readonly ITransportGateway _gateway;
    private readonly ulong _serverPeer;
    private readonly HashSet<ulong> _assigned = new();
    private readonly HashSet<ulong> _preparedJoins = new();
    private readonly LobbyNetworkDriver? _lobby;
    private readonly Trackstorm.Core.Arenas.ArenaConfiguration? _arena;
    private readonly bool _applicationEntry;
    private readonly HashSet<ulong> _entryPrepared = new();
    private readonly HashSet<ulong> _entrySynchronized = new();
    private InputHistory? _inputs;
    private ulong _session;
    private double _snapshotAge;
    private ulong _itemPublication;
    private ulong _publishedItemRevision = ulong.MaxValue;
    private bool _rosterChanged = true;
    private ulong? _lastLifecycleTick;
    private ulong _publishedSpawnRevision = ulong.MaxValue;
    private ulong _publishedMatchRevision = ulong.MaxValue;
    private ulong _generation;
    private bool _awaitingCheckpoint;
    private GameplayConfigurationState _configuration = new(0, new());
    private ulong? _publishedConfiguration;
    private bool _receivedConfiguration;
    private bool _entryReleased;
    private double _entrySeconds;
    private double _entryRequestSeconds;
    private bool _disposed;
    private Trackstorm.Core.Arenas.EnvironmentSnapshot? _publishedEnvironment;
    private readonly Func<bool>? _admissionOpen;
    private readonly Func<ulong, bool>? _activateJoin;

    /// <summary>Creates a host or connects a client driver to an already-open transport.</summary>
    /// <param name="gateway">Caller-owned transport.</param>
    /// <param name="hostSession">Nonzero host generation, or zero for a client.</param>
    /// <param name="serverPeer">Client's actual transport server identity.</param>
    /// <param name="lobby">Optional authoritative lobby which has entered an arena.</param>
    /// <param name="damageConfiguration">Optional arena vehicle capacity.</param>
    /// <param name="configuration">Validated effective gameplay tuning.</param>
    /// <param name="arena">Active map's validated player and optional pickup markers.</param>
    /// <param name="applicationEntry">Require the application loading barrier before simulation.</param>
    internal VehicleNetworkDriver(ITransportGateway gateway, ulong hostSession, ulong serverPeer = 0, LobbyNetworkDriver? lobby = null, DamageConfiguration? damageConfiguration = null, GameplayConfiguration? configuration = null, Trackstorm.Core.Arenas.ArenaConfiguration? arena = null, bool applicationEntry = false)
    {
        _applicationEntry = applicationEntry;
        _entryReleased = !applicationEntry || (hostSession == 0 && lobby?.NeedsArenaCheckpoint == true);
        _arena = arena;
        _lobby = lobby;
        _gateway = gateway;
        _serverPeer = serverPeer;
        _session = hostSession;
        if (hostSession != 0)
        {
            var sessionConfiguration = lobby?.Authority?.Configuration;
            ulong revision = sessionConfiguration?.Revision ?? 0;
            if (configuration is not null && sessionConfiguration is not null && configuration != sessionConfiguration.Configuration)
            {
                revision = checked(revision + 1);
            }

            Host = new HostVehicleSession(hostSession, damageConfiguration: damageConfiguration, configuration: configuration ?? sessionConfiguration?.Configuration, hostPlayerId: lobby?.LocalPlayerId ?? 1, configurationRevision: revision, events: lobby?.Authority?.Events, arena: _arena, requireActiveMatch: applicationEntry, randomizeItemSeed: true);
            lobby?.Authority?.RetainConfiguration(Host.Configuration);
            LocalVehicleId = Host.HostPlayerId;
        }

        if (lobby is not null)
        {
            _admissionOpen = () => Host?.CanJoin == true && EntryReady && IsActive;
            lobby.ArenaAdmissionOpen = _admissionOpen;
            _activateJoin = peer =>
            {
                if (Host is null || !_preparedJoins.Contains(peer) || !Host.ActivateJoin(peer, lobby.Authority!.PlayerId(peer)))
                {
                    return false;
                }

                _preparedJoins.Remove(peer);
                _entrySynchronized.Add(peer);
                _assigned.Add(peer);
                _rosterChanged = true;
                return true;
            };
            lobby.ActivateJoin = _activateJoin;
            if (lobby.Migration is not null)
            {
                lobby.Migration.CaptureArena = CaptureMigration;
                lobby.Migration.MapConfiguration = _arena;
                lobby.Migration.RestoreArena = RestoreMigration;
                lobby.Migration.ObservedTick = GetObservedTick;
            }

            if (lobby.State?.Phase != SessionPhase.Arena)
            {
                throw new ArgumentException("Vehicle gameplay requires an active arena.");
            }

            _session = lobby.State.Match;
            LocalVehicleId = lobby.LocalPlayerId;
            _generation = lobby.Generation;
            _awaitingCheckpoint = Host is null && (applicationEntry || lobby.NeedsArenaCheckpoint);
            if (Host is not null)
            {
                foreach (var peer in lobby.Authority!.Peers)
                {
                    if (lobby.Authority.IsPendingJoin(peer.Key))
                    {
                        continue;
                    }

                    Host.JoinPlayer(peer.Key, peer.Value);
                    _assigned.Add(peer.Key);
                }

                foreach (var player in lobby.State.Players.Where(player => !player.Connected))
                {
                    Host.ReservePlayer(player.Id);
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
    internal event Action<Trackstorm.Core.Arenas.EnvironmentSnapshot>? EnvironmentReceived;
    internal Trackstorm.Core.Arenas.EnvironmentSnapshot? EnvironmentState { get; private set; }
    /// <summary>Ordered reliable lifecycle boundaries, including ones superseded by newer movement snapshots.</summary>
    internal event Action<WorldSnapshot>? LifecycleReceived;
    /// <summary>Once-per-revision authoritative totals, score deltas and phase/winner changes.</summary>
    internal event Action<MatchState>? MatchReceived;
    /// <summary>Atomic reset boundary for interpolation, native bodies and one-shot presentation baselines.</summary>
    internal event Action<WorldSnapshot>? Resynchronized;
    /// <summary>Refreshes native observers after a complete validated tuning transaction.</summary>
    internal event Action<GameplayConfiguration>? ConfigurationChanged;
    /// <summary>Current authority-owned tuning; clients never load local persisted gameplay values.</summary>
    internal GameplayConfigurationState Configuration => Host?.Configuration ?? _configuration;
    /// <summary>Latest reliable match state, independent of movement snapshot ordering.</summary>
    internal MatchState? Match { get; private set; }
    /// <summary>Core-completed results for this arena generation; no presentation-side ranking or scoring.</summary>
    internal FinalMatchResults? FinalResults => Match?.FinalResults;
    /// <summary>Authoritative native prop observation seam, absent in flat-ground vehicle unit tests.</summary>
    internal Func<IReadOnlyList<VehiclePhysicsState>>? ObserveProps { get; set; }
    /// <summary>Latest accepted complete prop publication.</summary>
    internal Trackstorm.Core.Arenas.ArenaPropSnapshot? PropSnapshot { get; private set; }
    /// <summary>Native swept collision query, host only.</summary>
    internal Func<ulong, System.Numerics.Vector3, System.Numerics.Vector3, WeaponRayHit?>? RaycastWeapon { get; set; }
    internal Func<MissileState, System.Numerics.Vector3, float?>? CollideMissile { get; set; }
    /// <summary>Host-only ground projection for persistent oil deployment.</summary>
    internal Func<ItemSlot, VehiclePhysicsState, OilPatch?>? PlaceOil { get; set; }
    internal Func<System.Numerics.Vector3, System.Numerics.Vector3?>? ProjectSalvoGround { get; set; }
    internal Func<ItemSlot, VehiclePhysicsState, ProxyMineState?>? PlaceMine { get; set; }
    internal Func<ProxyMineState, ProxyMineState, ProxyMineMotion>? MoveMine { get; set; }
    /// <summary>Latest complete reliable item state.</summary>
    internal ItemPublication? ItemState { get; private set; }
    /// <summary>Current local slot; no predicted consumption.</summary>
    internal ItemSlot? LocalItem => (Host?.Items.Slots ?? ItemState?.Slots)?.SingleOrDefault(slot => slot.Vehicle == LocalVehicleId);

    /// <summary>Host gameplay owner, or null on clients.</summary>
    internal HostVehicleSession? Host { get; private set; }
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
    internal bool IsActive => !_disposed && (_lobby is null || ((!_applicationEntry || _entryReleased) && _lobby.Failure.Length == 0 && !_lobby.Reconnecting && !_lobby.JoiningArena && _lobby.Migration?.Frozen != true && !_awaitingCheckpoint && _lobby.State?.Phase == SessionPhase.Arena && _lobby.State.Match == _session));
    /// <summary>Current local gameplay state, independent of render smoothing.</summary>
    internal VehicleSnapshot? LocalState => Host?.World.GetVehicle(LocalVehicleId) ?? Prediction?.State;
    /// <summary>Completed application handoff; absent throughout local loading or authoritative recovery.</summary>
    internal SynchronizedMatchContext? EntryContext { get; private set; }
    /// <summary>Monotonic initial entry release for this generation; temporary resynchronization never clears it.</summary>
    internal bool InitialEntryReleased => _entryReleased;
    /// <summary>Current synchronized entry readiness, including temporary recovery gates.</summary>
    internal bool EntryReady => !_disposed && (!_applicationEntry || (EntryContext is not null && IsActive));

    /// <summary>Local controls require synchronization and the accepted authoritative phase, never a local countdown.</summary>
    internal bool AllowsParticipation => Failure.Length == 0 && IsActive && EntryReady &&
        (!_applicationEntry || (Host?.World.State.Match ?? Match)?.Lifecycle.AllowsGameplay == true);

    private ulong ServerPeer => _lobby?.ServerPeer ?? _serverPeer;

    /// <summary>Pumps transport, consumes authority updates, predicts immediately, and emits rate-limited snapshots.</summary>
    /// <param name="input">This fixed tick's local input.</param>
    /// <param name="observe">Synchronous native or deterministic test collision seam.</param>
    internal void Advance(InputFrame input, Func<VehicleSnapshot, VehicleObservation> observe)
    {
        if (_disposed)
        {
            return;
        }

        if (_lobby is not null)
        {
            _lobby.Pump(1.0 / HostVehicleSession.TickRate, message => Receive(message, observe));
            if (_lobby.Failure.Length > 0 || _lobby.State?.Phase != SessionPhase.Arena || _lobby.State.Match != _session)
            {
                return;
            }

            if (_lobby.Reconnecting || _lobby.Migration?.Frozen == true)
            {
                if (Host is not null)
                {
                    SynchronizePeers();
                }

                EntryContext = null;
                _awaitingCheckpoint = Host is null && (_awaitingCheckpoint || _lobby.Reconnecting || _lobby.NeedsArenaCheckpoint);
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
            if (!_gateway.Connections.TryGetValue(ServerPeer, out var state) || state == TransportConnectionState.Disconnected)
            {
                if (_lobby?.Reconnect is null)
                {
                    Failure = "Host disconnected; reconnect to start a new session.";
                }

                return;
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

        if (_applicationEntry && !AdvanceEntry())
        {
            return;
        }

        if (Host is not null)
        {
            if (_rosterChanged || _publishedConfiguration != Host.Configuration.Revision)
            {
                byte[] configuration = GameplayConfigurationCodec.Encode(_session, Host.Configuration);
                foreach (ulong peer in _assigned)
                {
                    Send(new TransportMessage(peer, configuration, TransportDelivery.Reliable));
                }

                _publishedConfiguration = Host.Configuration.Revision;
                _rosterChanged = true;
            }

            RosterChanged?.Invoke(Host.Snapshot());
            if ((input.Pressed & InputButtons.SwitchItem) != 0) { RequestItemSwitch(); }
            if ((input.Pressed & InputButtons.UseItem) != 0)
            {
                RequestItemUse();
            }

            Host.Step(input, observe, CollideMissile, PlaceOil, PlaceMine, MoveMine, ProjectSalvoGround, RaycastWeapon);
            Host.CollectPickups();
            if (Host.Environment is { } environment)
            {
                EnvironmentState = environment.Snapshot(_session, Host.World.State.Tick);
                EnvironmentReceived?.Invoke(EnvironmentState);
                if (Host.World.State.Tick % HostVehicleSession.SnapshotInterval == 0 &&
                    (_rosterChanged || _publishedEnvironment is null || Host.World.State.Tick % 60 == 0 ||
                     !EnvironmentState.Rocks.SequenceEqual(_publishedEnvironment.Rocks) || !EnvironmentState.Plants.SequenceEqual(_publishedEnvironment.Plants)))
                {
                    byte[] state = Trackstorm.Core.Arenas.EnvironmentCodec.Encode(EnvironmentState);
                    foreach (ulong peer in _assigned) { Send(new TransportMessage(peer, state, TransportDelivery.Unreliable)); }
                    _publishedEnvironment = EnvironmentState;
                }
            }

            Latest = Host.Snapshot();
            MatchState match = Host.World.State.Match!;
            if (_rosterChanged || match.Revision != _publishedMatchRevision)
            {
                byte[] payload = MatchCodec.Encode(_session, match);
                foreach (ulong peer in _assigned)
                {
                    Send(new TransportMessage(peer, payload, TransportDelivery.Reliable));
                }

                if (Match is null || Match.Revision != match.Revision)
                {
                    Match = match;
                    MatchReceived?.Invoke(match);
                }

                _publishedMatchRevision = match.Revision;
            }

            if (_rosterChanged || Host.World.LifecycleChanges.Count > 0)
            {
                byte[] lifecycle = VehicleNetworkCodec.EncodeSnapshot(Latest);
                foreach (ulong peer in _assigned)
                {
                    Send(new TransportMessage(peer, lifecycle, TransportDelivery.Reliable));
                }

                LifecycleReceived?.Invoke(Latest);
            }

            if (_publishedSpawnRevision != (Host.Spawns?.Revision ?? 0) || _publishedItemRevision != Host.Items.Revision || _rosterChanged)
            {
                ItemState = new ItemPublication(++_itemPublication, Latest, Host.Items.Slots, Host.Items.Missiles, Host.Items.Events, Host.Spawns?.States, Host.Items.Patches, Host.Items.OilContacts, Host.Spawns?.Balances, Host.Items.Mines);
                byte[] items = ItemCodec.EncodeState(ItemState);
                foreach (ulong peer in _assigned)
                {
                    Send(new TransportMessage(peer, items, TransportDelivery.Reliable));
                }

                _publishedSpawnRevision = Host.Spawns?.Revision ?? 0;
                _publishedItemRevision = Host.Items.Revision;
                ItemsReceived?.Invoke(ItemState);
            }

            _rosterChanged = false;
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
        else if ((!_applicationEntry || EntryReady) && !_awaitingCheckpoint && _lobby?.JoiningArena != true && Inputs is InputHistory inputs)
        {
            if (!AllowsParticipation)
            {
                input = default;
                inputs.NeutralizePending();
            }

            if ((input.Pressed & InputButtons.SwitchItem) != 0) { RequestItemSwitch(); }
            if ((input.Pressed & InputButtons.UseItem) != 0)
            {
                RequestItemUse(inputs.NextSequence);
            }

            if (inputs.IsFull)
            {
                if (_lobby?.Reconnect is null)
                {
                    Failure = "Host acknowledgements stalled; reconnect to resynchronize.";
                }

                _gateway.Disconnect(ServerPeer);
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

            Send(new TransportMessage(ServerPeer, VehicleNetworkCodec.EncodeInputs(_session, inputs.GetRedundancy(), LocalState?.LifeId ?? 1), TransportDelivery.Unreliable));
        }
    }

    /// <summary>Commits local host edits; there is deliberately no client-to-host tuning message.</summary>
    /// <returns>Whether the operation was accepted.</returns>
    /// <param name="edits">Stable gameplay keys and requested values.</param>
    /// <param name="error">Safe validation feedback.</param>
    internal bool TryConfigure(IReadOnlyDictionary<string, double> edits, out string error)
    {
        error = "Only the authoritative host may change gameplay tuning.";
        if (Host is null || !IsActive || !Host.TryConfigure(0, edits, out error))
        {
            return false;
        }

        _lobby?.Authority?.RetainConfiguration(Host.Configuration);
        ConfigurationChanged?.Invoke(Configuration.Configuration);
        return true;
    }

    /// <summary>Grants through the current active authority only, including migration lease fencing.</summary>
    /// <param name="item">Requested supported item.</param>
    /// <returns>Whether the current host granted its own item.</returns>
    internal bool GiveDeveloperItem(HeldItem item) => IsActive && Host?.GiveItem(0, item) == true;

    /// <summary>Uses the normal match authority only while the current epoch may advance.</summary>
    /// <returns>Whether the authoritative countdown override was accepted.</returns>
    internal bool ForceDeveloperStart() => IsActive && Host?.ForceStart(0) == true;

    /// <summary>Sends ordered selection intent; presentation continues to use confirmed ownership.</summary>
    internal bool RequestItemSwitch()
    {
        if (!AllowsParticipation || LocalState is not { CanInteract: true } state) { return false; }
        var inventory = LocalItem;
        if (_switchLife != state.LifeId) { _switchLife = state.LifeId; _switchRevision = 0; }
        ulong revision = checked(Math.Max(_switchRevision, inventory?.SelectionRevision ?? 0) + 1);
        bool accepted = Host is not null ? Host.SwitchItem(0, _session, state.LifeId, revision)
            : Send(new TransportMessage(ServerPeer, ItemCodec.EncodeSwitch(_session, state.LifeId, revision), TransportDelivery.Reliable));
        if (accepted) { _switchRevision = revision; }
        return accepted;
    }
    /// <summary>Submits the local slot capability reliably; never creates a predicted item effect.</summary>
    /// <returns>Whether queued locally or sent to the host.</returns>
    /// <param name="inputSequence">Originating captured frame when called from the remote input loop.</param>
    internal bool RequestItemUse(uint? inputSequence = null)
    {
        ItemSlot? inventory = LocalItem;
        // Ordered reliable intent can use the requested slot before its confirmation arrives.
        // This chooses a known capability only; ownership and outcomes remain host-authoritative.
        if (Host is null && inventory is not null && inventory.Life == _switchLife && _switchRevision > inventory.SelectionRevision)
        {
            inventory = inventory with { ActiveSlot = (byte)(inventory.ActiveSlot ^ ((_switchRevision - inventory.SelectionRevision) & 1)) };
        }
        ItemSlot? slot = inventory?.Active;
        if (!AllowsParticipation || LocalState?.CanInteract != true || slot is null || slot.Item == HeldItem.None)
        {
            return false;
        }

        if (Host is not null)
        {
            return Host.UseItem(0, _session, slot.Life, slot.Token);
        }

        return Send(new TransportMessage(ServerPeer, ItemCodec.EncodeUse(_session, slot.Life, slot.Token, inputSequence), TransportDelivery.Reliable));
    }

    private bool Send(TransportMessage message)
    {
        if (!_gateway.Connections.TryGetValue(message.RemotePeerId, out var state) || state != TransportConnectionState.Connected)
        {
            return false;
        }

        try
        {
            if (_lobby is null)
            {
                _gateway.Send(message);
            }
            else
            {
                _lobby.SendGameplay(message);
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            // Native closure can race the preceding Poll, including during a multi-packet publication.
            _gateway.Disconnect(message.RemotePeerId);
            if (Host is null && _lobby?.Reconnect is null)
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

        _preparedJoins.IntersectWith(connected);
        _entryPrepared.IntersectWith(connected);
        _entrySynchronized.IntersectWith(connected);

        foreach (ulong peer in _assigned.Except(connected).ToArray())
        {
            if (_lobby is null)
            {
                Host!.Leave(peer);
            }
            else
            {
                Host!.Suspend(peer);
            }

            _assigned.Remove(peer);
            _rosterChanged = true;
        }

        if (_lobby is not null)
        {
            foreach (var vehicle in Host!.World.State.Vehicles.Where(vehicle => !_lobby.State!.Players.Any(player => player.Id == vehicle.VehicleId)).ToArray())
            {
                Host.ExpirePlayer(vehicle.VehicleId);
                _rosterChanged = true;
            }
        }

        foreach (ulong peer in connected.Except(_assigned))
        {
            if (_lobby is not null)
            {
                ulong player = _lobby.Authority!.PlayerId(peer);
                if (_lobby.Authority.IsPendingJoin(peer))
                {
                    if (_applicationEntry && !_entryPrepared.Contains(peer))
                    {
                        continue;
                    }

                    if (_preparedJoins.Add(peer))
                    {
                        var props = ObserveProps is null ? null : new Trackstorm.Core.Arenas.ArenaPropSnapshot(_session, Host!.World.State.Tick, ObserveProps());
                        var checkpoint = Host!.PrepareJoin(player, ++_itemPublication, props);
                        if (checkpoint is null || !Send(new TransportMessage(peer, ResumeCheckpointCodec.Encode(checkpoint), TransportDelivery.Reliable)))
                        {
                            _preparedJoins.Remove(peer);
                            _lobby.RejectJoin(peer, "Join bootstrap failed");
                        }
                    }

                    continue;
                }

                if (Host!.ResumePlayer(peer, player))
                {
                    _assigned.Add(peer);
                    _entryPrepared.Add(peer);
                    SendCheckpoint(peer);
                }

                continue;
            }

            ulong vehicle = Host!.Join(peer);
            if (vehicle == 0)
            {
                _gateway.Disconnect(peer);
                continue;
            }

            _assigned.Add(peer);
            _rosterChanged = true;
            Send(new TransportMessage(peer, VehicleNetworkCodec.EncodeWelcome(_session, vehicle), TransportDelivery.Reliable));
        }
    }

    /// <summary>Releases match-scoped state and callbacks without closing transport or changing session reservations.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_lobby is not null)
        {
            if (_lobby.ActivateJoin == _activateJoin)
            {
                _lobby.ActivateJoin = null;
            }

            if (_lobby.ArenaAdmissionOpen == _admissionOpen)
            {
                _lobby.ArenaAdmissionOpen = null;
            }

            if (_lobby.Migration is { } migration && migration.CaptureArena == CaptureMigration)
            {
                migration.CaptureArena = null;
                migration.RestoreArena = null;
                migration.ObservedTick = null;
                migration.MapConfiguration = null;
            }
        }

        Host = null;
        Match = null;
        EntryContext = null;
        Prediction = null;
        History = null;
        Latest = null;
        ItemState = null;
        PropSnapshot = null;
        _inputs = null;
        _assigned.Clear();
        _preparedJoins.Clear();
        _entryPrepared.Clear();
        _entrySynchronized.Clear();
        ObserveProps = null;
        CollideMissile = null;
        RaycastWeapon = null;
        PlaceOil = null;
        ProjectSalvoGround = null;
        RosterChanged = null;
        LocalCorrected = null;
        PropsReceived = null;
        ItemsReceived = null;
        EnvironmentReceived = null;
        LifecycleReceived = null;
        MatchReceived = null;
        Resynchronized = null;
        ConfigurationChanged = null;
    }

    private ulong GetObservedTick() => Host?.World.State.Tick ?? Latest?.Tick ?? 0;

    private (ResumeCheckpoint Arena, HostRestoreState Host) CaptureMigration()
    {
        _lobby!.Authority!.RetainConfiguration(Host!.Configuration);
        WorldSnapshot world = Host!.Snapshot();
        var items = new ItemPublication(Math.Max(1, _itemPublication), world, Host.Items.Slots, Host.Items.Missiles, [], Host.Spawns?.States, Host.Items.Patches, Host.Items.OilContacts, Host.Spawns?.Balances, Host.Items.Mines);
        var state = Host.World.State.Match!;
        var match = new MatchState(state.Tick, state.Revision, state.KillTarget, state.Phase, state.CountdownAtTick, state.Winner, state.Players, mode: state.Mode);
        var props = ObserveProps is null ? null : new Trackstorm.Core.Arenas.ArenaPropSnapshot(_session, world.Tick, ObserveProps());
        return (new ResumeCheckpoint(items, match, props, Host.Configuration, Host.Environment?.Snapshot(_session, world.Tick)), Host.CaptureAuthority());
    }

    private void RestoreMigration(MigrationCheckpoint checkpoint, bool host)
    {
        if (checkpoint.Arena is null)
        {
            return;
        }

        _assigned.Clear();
        _preparedJoins.Clear();
        _entryPrepared.Clear();
        _entrySynchronized.Clear();
        _awaitingCheckpoint = true;
        // A new authority epoch may restore an older complete configuration boundary.
        _receivedConfiguration = false;
        _publishedConfiguration = null;
        Host = host ? HostVehicleSession.Restore(checkpoint.Arena, checkpoint.Host!, _lobby!.LocalPlayerId, _lobby.Authority!.Events, _arena, requireActiveMatch: _applicationEntry) : null;
        _itemPublication = checkpoint.Arena.Items.Revision;
        _publishedItemRevision = ulong.MaxValue;
        _publishedSpawnRevision = ulong.MaxValue;
        _publishedMatchRevision = ulong.MaxValue;
        ApplyCheckpoint(checkpoint.Arena);
        if (!host)
        {
            // The replacement must independently authorize this fresh connection before gameplay resumes.
            _awaitingCheckpoint = true;
            _lobby!.BeginMigrationResume();
        }
    }

    private void SendCheckpoint(ulong peer)
    {
        WorldSnapshot world = Host!.Snapshot();
        var items = new ItemPublication(++_itemPublication, world, Host.Items.Slots, Host.Items.Missiles, [], Host.Spawns?.States, Host.Items.Patches, Host.Items.OilContacts, Host.Spawns?.Balances, Host.Items.Mines);
        var state = Host.World.State.Match!;
        var match = new MatchState(state.Tick, state.Revision, state.KillTarget, state.Phase, state.CountdownAtTick, state.Winner, state.Players, mode: state.Mode);
        var props = ObserveProps is null ? null : new Trackstorm.Core.Arenas.ArenaPropSnapshot(_session, world.Tick, ObserveProps());
        Send(new TransportMessage(peer, ResumeCheckpointCodec.Encode(new ResumeCheckpoint(items, match, props, Host.Configuration, Host.Environment?.Snapshot(_session, world.Tick))), TransportDelivery.Reliable));
    }

    private void ApplyCheckpoint(ResumeCheckpoint checkpoint)
    {
        if ((_arena?.Environment is null) != (checkpoint.Environment is null) ||
            (checkpoint.Environment is { } environment && (environment.Rocks.Count != _arena!.Environment!.Rocks.Count || environment.Plants.Count != _arena.Environment.Plants.Count)))
        {
            throw new ArgumentException("Checkpoint environment layout mismatch.");
        }
        WorldSnapshot world = checkpoint.Items.World;
        var local = world.Vehicles.SingleOrDefault(vehicle => vehicle.State.VehicleId == LocalVehicleId);
        if (world.Session != _session || local is null || _lobby is null || (_generation == _lobby.Generation && !_awaitingCheckpoint))
        {
            throw new ArgumentException("Unsolicited or mismatched resume checkpoint.");
        }

        var history = new SnapshotHistory(_session);
        history.Add(world);
        if (_receivedConfiguration && !checkpoint.Configuration.CanReplace(_configuration))
        {
            throw new ArgumentException("Stale checkpoint configuration.");
        }

        var prediction = new PredictedVehicle(local, checkpoint.Configuration.Configuration);
        _configuration = checkpoint.Configuration;
        _receivedConfiguration = true;
        ConfigurationChanged?.Invoke(_configuration.Configuration);
        History = history;
        Prediction = prediction;
        _inputs = null;
        Latest = world;
        ItemState = checkpoint.Items;
        _switchLife = _switchRevision = 0;
        Match = checkpoint.Match;
        PropSnapshot = checkpoint.Props;
        EnvironmentState = checkpoint.Environment;
        if (EnvironmentState is not null) { EnvironmentReceived?.Invoke(EnvironmentState); }
        _lastLifecycleTick = world.Tick;
        _snapshotAge = 0;
        _generation = _lobby.Generation;
        _awaitingCheckpoint = false;
        EntryContext = null;
        Failure = string.Empty;
        RosterChanged?.Invoke(world);
        Resynchronized?.Invoke(world);
        LocalCorrected?.Invoke(local.State);
        ItemsReceived?.Invoke(ItemState);
        MatchReceived?.Invoke(Match);
        if (PropSnapshot is not null)
        {
            PropsReceived?.Invoke(PropSnapshot);
        }

        _lobby.CompleteResume();
        if (_applicationEntry && Host is null)
        {
            Send(new TransportMessage(ServerPeer, MatchEntryCodec.Encode(_session, MatchEntryCodec.Synchronized), TransportDelivery.Reliable));
        }
    }

    private bool AdvanceEntry()
    {
        if (EntryContext is not null && IsActive)
        {
            return true;
        }

        _entrySeconds += 1.0 / HostVehicleSession.TickRate;
        if (_entrySeconds > 30)
        {
            Failure = "Match loading or synchronization timed out. Return to the browser and retry.";
            return false;
        }

        if (Host is not null && !_entryReleased)
        {
            var peers = _lobby!.Authority!.Peers.Keys.ToArray();
            if (peers.Any(peer => !_entrySynchronized.Contains(peer)))
            {
                return false;
            }

            var context = new SynchronizedMatchContext(_session, Host.World.State.Tick, Host.World.State.Vehicles.Select(vehicle => vehicle.VehicleId));
            if (!Host.World.InitializeMatch(context))
            {
                Failure = "The Game Loop rejected match initialization.";
                return false;
            }

            EntryContext = context;
            _entryReleased = true;
            foreach (ulong peer in peers)
            {
                Send(new TransportMessage(peer, MatchEntryCodec.Encode(_session, MatchEntryCodec.Released), TransportDelivery.Reliable));
            }
        }

        if (Host is null && _awaitingCheckpoint)
        {
            _entryRequestSeconds -= 1.0 / HostVehicleSession.TickRate;
            if (_entryRequestSeconds <= 0)
            {
                _entryRequestSeconds = 0.5;
                Send(new TransportMessage(ServerPeer, MatchEntryCodec.Encode(_session, MatchEntryCodec.Loaded), TransportDelivery.Reliable));
            }

            return false;
        }

        if (!_entryReleased || !IsActive)
        {
            return false;
        }

        var world = Host?.Snapshot() ?? Latest;
        if (world is null || (Host is null && (Match is null || ItemState is null || !_receivedConfiguration || Prediction is null)))
        {
            return false;
        }

        EntryContext ??= new SynchronizedMatchContext(_session, world.Tick, world.Vehicles.Select(vehicle => vehicle.State.VehicleId));
        _entrySeconds = 0;
        return true;
    }

    private void ReceiveEntry(TransportMessage message)
    {
        if (!_applicationEntry || _lobby is null || message.Delivery != TransportDelivery.Reliable)
        {
            throw new ArgumentException("Match entry requires an admitted reliable session.");
        }

        byte kind = MatchEntryCodec.Decode(message.Payload.Span, _session);
        if (Host is not null)
        {
            ulong peer = message.RemotePeerId;
            if (!_lobby.Authority!.Peers.ContainsKey(peer))
            {
                throw new ArgumentException("Unknown loading participant.");
            }

            if (kind == MatchEntryCodec.Loaded)
            {
                if (_entryPrepared.Add(peer) && !_lobby.Authority.IsPendingJoin(peer) && _assigned.Contains(peer))
                {
                    SendCheckpoint(peer);
                }
            }
            else if (kind == MatchEntryCodec.Synchronized && _entryPrepared.Contains(peer))
            {
                _entrySynchronized.Add(peer);
            }
            else
            {
                throw new ArgumentException("Unauthorized loading acknowledgement.");
            }
        }
        else if (message.RemotePeerId == ServerPeer && kind == MatchEntryCodec.Released && !_awaitingCheckpoint)
        {
            _entryReleased = true;
        }
        else
        {
            throw new ArgumentException("Only the established host can release synchronization.");
        }
    }

    private bool AcceptSnapshot(WorldSnapshot snapshot, Func<VehicleSnapshot, VehicleObservation> observe)
    {
        ReplicatedVehicle? local = snapshot.Vehicles.SingleOrDefault(vehicle => vehicle.State.VehicleId == LocalVehicleId);
        if (!_receivedConfiguration || snapshot.ConfigurationRevision != Configuration.Revision || local is null ||
            local.State.Damage.MaxHP != Configuration.Configuration.Damage.MaxHP ||
            Math.Abs(local.State.Movement.SteeringAngle) > Configuration.Configuration.Vehicle.SteeringAngle ||
            Inputs is not InputHistory inputs || !inputs.CanAcknowledge(local.AcknowledgedInput) || History is null || !History.Add(snapshot))
        {
            return false;
        }

        Latest = snapshot;
        RosterChanged?.Invoke(snapshot);
        if (Prediction is null)
        {
            Prediction = new PredictedVehicle(local, inputs, observe, Configuration.Configuration);
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
            if (MatchEntryCodec.IsEntry(message.Payload.Span))
            {
                ReceiveEntry(message);
                return;
            }

            if (_applicationEntry && Host is not null && (!_entryReleased || !_entrySynchronized.Contains(message.RemotePeerId)))
            {
                throw new ArgumentException("Gameplay is unavailable during match synchronization.");
            }

            if (GameplayConfigurationCodec.IsConfiguration(message.Payload.Span))
            {
                if (Host is not null || message.RemotePeerId != ServerPeer || message.Delivery != TransportDelivery.Reliable)
                {
                    throw new ArgumentException("Only the assigned host may publish configuration.");
                }

                var publication = GameplayConfigurationCodec.Decode(message.Payload.Span);
                if (publication.Session != _session || (_receivedConfiguration && !publication.State.CanReplace(_configuration)))
                {
                    throw new ArgumentException("Stale or mismatched configuration publication.");
                }

                if (!_receivedConfiguration || publication.State.Revision != _configuration.Revision)
                {
                    Prediction?.ApplyConfiguration(publication.State.Configuration);
                    _configuration = publication.State;
                    ConfigurationChanged?.Invoke(_configuration.Configuration);
                }

                _receivedConfiguration = true;
                return;
            }

            if (Host is null && _lobby is not null && (_lobby.Generation != _generation || _lobby.Reconnecting))
            {
                _awaitingCheckpoint = true;
            }

            if (ResumeCheckpointCodec.IsCheckpoint(message.Payload.Span))
            {
                if (Host is not null || message.RemotePeerId != ServerPeer || message.Delivery != TransportDelivery.Reliable)
                {
                    throw new ArgumentException("Only the established host may publish a resume boundary.");
                }

                ApplyCheckpoint(ResumeCheckpointCodec.Decode(message.Payload.Span));
                return;
            }

            if (_awaitingCheckpoint)
            {
                RejectedPackets++;
                return;
            }

            if (MatchCodec.IsMatch(message.Payload.Span))
            {
                if (Host is not null || message.RemotePeerId != ServerPeer || History is null || message.Delivery != TransportDelivery.Reliable)
                {
                    throw new ArgumentException("Only the assigned host can publish reliable match state.");
                }

                var publication = MatchCodec.Decode(message.Payload.Span);
                if (publication.Session != _session || (Match is not null &&
                    (publication.State.Revision <= Match.Revision || publication.State.Tick < Match.Tick || Match.Phase == MatchPhase.Finished)))
                {
                    throw new ArgumentException("Stale or terminal match publication.");
                }

                Match = publication.State;
                if (_applicationEntry && !Match.Lifecycle.AllowsGameplay)
                {
                    Inputs?.NeutralizePending();
                }

                MatchReceived?.Invoke(Match);
                return;
            }

            if (Trackstorm.Core.Arenas.EnvironmentCodec.IsEnvironment(message.Payload.Span))
            {
                if (Host is not null || message.RemotePeerId != ServerPeer) { throw new ArgumentException("Only current host may publish destruction."); }
                var state = Trackstorm.Core.Arenas.EnvironmentCodec.Decode(message.Payload.Span);
                if (state.Session != _session || state.Tick <= (EnvironmentState?.Tick ?? 0) || _arena?.Environment is not { } layout || state.Rocks.Count != layout.Rocks.Count || state.Plants.Count != layout.Plants.Count) { return; }
                EnvironmentState = state;
                EnvironmentReceived?.Invoke(state);
                return;
            }

            if (ItemCodec.IsItem(message.Payload.Span))
            {
                if (message.Delivery != TransportDelivery.Reliable)
                {
                    throw new ArgumentException("Items require reliable delivery.");
                }

                if (Host is not null)
                {
                    if (ItemCodec.IsSwitch(message.Payload.Span))
                    {
                        var selection = ItemCodec.DecodeSwitch(message.Payload.Span);
                        if (!Host.SwitchItem(message.RemotePeerId, selection.Session, selection.Life, selection.Revision)) { RejectedPackets++; }
                        return;
                    }
                    var request = ItemCodec.DecodeUse(message.Payload.Span);
                    if (!Host.UseItem(message.RemotePeerId, request.Session, request.Life, request.Token, request.InputSequence))
                    {
                        RejectedPackets++;
                    }

                    return;
                }

                if (message.RemotePeerId != ServerPeer || History is null)
                {
                    throw new ArgumentException("Only the assigned host can publish item outcomes.");
                }

                ItemPublication publication = ItemCodec.DecodeState(message.Payload.Span);
                if (!_receivedConfiguration || publication.World.Session != _session || publication.World.ConfigurationRevision != Configuration.Revision || publication.Revision <= (ItemState?.Revision ?? 0))
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
                if (!Host.Receive(message.RemotePeerId, inputs.Session, inputs.Inputs, inputs.Life))
                {
                    RejectedPackets++;
                }

                return;
            }

            if (Host is null && message.RemotePeerId == ServerPeer)
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

                if (kind == VehicleNetworkCodec.Snapshot && History is not null)
                {
                    WorldSnapshot snapshot = VehicleNetworkCodec.DecodeSnapshot(message.Payload.Span);
                    if (!_receivedConfiguration || snapshot.ConfigurationRevision != Configuration.Revision)
                    {
                        throw new ArgumentException("Snapshot requires a different configuration revision.");
                    }

                    if (message.Delivery == TransportDelivery.Reliable && snapshot.Session == _session &&
                        (!_lastLifecycleTick.HasValue || snapshot.Tick > _lastLifecycleTick.Value))
                    {
                        _lastLifecycleTick = snapshot.Tick;
                        AcceptSnapshot(snapshot, observe);
                        LifecycleReceived?.Invoke(snapshot);
                        return;
                    }

                    if (message.Delivery != TransportDelivery.Unreliable)
                    {
                        throw new ArgumentException("Stale lifecycle publication.");
                    }

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
