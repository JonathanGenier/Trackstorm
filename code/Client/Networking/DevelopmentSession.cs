using Godot;
using Trackstorm.Client.Online;
using Trackstorm.Core.Input;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Networking;

/// <summary>Development Host/Join and lobby presentation reconstructed from authoritative Core state.</summary>
internal sealed partial class DevelopmentSession : CanvasLayer
{
    private readonly Queue<Core.Matches.MatchState> _matchPresentation = new();
    private readonly LineEdit _name = new() { Name = "PlayerName", Text = "Player", PlaceholderText = "Display name", MaxLength = 96 };
    private readonly LineEdit _address = new() { Name = "DirectAddress", Text = "127.0.0.1:27020", PlaceholderText = "IP:port or [IPv6]:port" };
    private readonly Label _status = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _arenaStatus = new();
    private readonly Button _host = new() { Text = "Host Game" };
    private readonly Button _join = new() { Text = "Join Game by address" };
    private readonly CheckButton _debug = new() { Text = "Developer fallback: Direct-IP / LAN" };
    private readonly Button _back = new() { Text = "Back to Main Menu" };
    private readonly Label _loadingText = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Label _admission = new() { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private NetworkTransportNode? _transport;
    private ITransportGateway? _gateway;
    private bool _onlineTransport;
    private EosP2pTransport? _ownedOnline;
    private string? _transportFailure;
    private InputButtons _standingsHeld;
    private LobbyNetworkDriver? _lobby;
    private NetworkVehicleArena? _arena;
    private VBoxContainer _browserContent = null!;
    private PanelContainer _browserPanel = null!;
    private string _message = "Choose or host a game. Up to 8 players; non-host players must be ready.";
    private ulong _arenaGeneration;
    private bool _leaving;
    private bool _logoutAfterLeave;
    private bool _forceStart;
    private double _eventMilliseconds;
    private int _eventRejected;
    private double _rejectionSeconds;
    private Control _root = null!;
    private bool _frontendVisible = true;
    private float _frontendAlpha = 1;
    private PanelContainer _loadingPanel = null!;
    private bool _browsing;
    private MatchResourceLoader? _matchLoader;
    private readonly List<MatchResourceLoader> _retiringLoads = new();
    private string _entryFailure = string.Empty;
    private double _entryFailureSeconds;
    internal Func<MatchMap, MatchResourceLoader> CreateMatchLoader { get; set; } = map => new(map);
    internal string LobbyNotice { get; private set; } = string.Empty;
    private double _loadSeconds;
    private string? _failureOutcome;
    private Frontend.HangingMainMenu _mainMenu = null!;
    private Frontend.HangingPlayMenu _playMenu = null!;
    private Label _directVersion = null!;
    private bool _menuTransition;
    private bool _mainHoisting;
    private bool _playHoisting;
    internal Frontend.HangingPlayMenu PlayMenu => _playMenu;
    internal Frontend.JoinedLobby JoinedLobby { get; private set; } = null!;
    private bool RetainedPresentation => OnlineCoordinator()?.ShowsRetainedDecision == true;

    /// <summary>Existing Settings destination supplied by the bootstrap.</summary>
    internal Action OpenSettings { get; set; } = () => { };
    internal Frontend.HangingMainMenu MainMenu => _mainMenu;

    /// <summary>Retains the last bounded session history after departure.</summary>
    internal Core.Events.EventStream Events { get; private set; } = new();

    /// <summary>Host-local tuning supplied by composition; never consulted for a joining client.</summary>
    internal Development.DeveloperSettingsStore? DeveloperSettings { get; set; }
    /// <summary>Local camera settings retained across arena reconstruction.</summary>
    internal Settings.PlayerSettingsController? CameraSettings { get; set; }
    /// <summary>Current local authority; no host controls exist before hosting or after authority is lost.</summary>
    internal bool IsDeveloperHost => Development.DeveloperTools.Enabled && !_leaving && _lobby is { Authority: not null, Failure.Length: 0, Reconnecting: false } && _lobby.Migration?.Frozen != true && (_arena is null || _arena.Driver.IsActive);
    /// <summary>Active provider capability boundary for local network simulation.</summary>
    internal ITransportGateway? Gateway => _gateway;
    /// <summary>Current host tuning for lobby or arena editing.</summary>
    internal Core.Development.GameplayConfiguration DeveloperConfiguration => _arena?.Driver.Configuration.Configuration ?? _lobby?.Configuration?.Configuration ?? Core.Development.GameplayConfiguration.HostedDefaults;

    /// <summary>Authenticated online coordinator supplied by application composition.</summary>
    internal Func<OnlineLobbyCoordinator?> OnlineCoordinator { get; set; } = () => null;

    /// <summary>Authentication state supplied by application composition.</summary>
    internal Func<EosLobbyStatus> OnlineStatus { get; set; } = () => EosLobbyStatus.Unavailable;
    /// <summary>Explicit login/retry action supplied by the identity owner.</summary>
    internal Action OnlineLogin { get; set; } = () => { };
    /// <summary>Identity-owner logout, invoked only after session and membership cleanup.</summary>
    internal Action OnlineLogout { get; set; } = () => { };
    internal bool CanLogoutOnline => !_logoutAfterLeave && OnlineStatus().CanLogout;

    internal void LogoutOnline()
    {
        if (!CanLogoutOnline) return;
        _logoutAfterLeave = true;
        ReturnToMainMenu();
    }

    /// <summary>Production lobby exposed for runtime integration verification.</summary>
    internal LobbyNetworkDriver? Lobby => _lobby;
    /// <summary>Shared projection used by both standings and the existing HUD badge.</summary>
    internal Hud.MatchStandingsView Standings => Hud.MatchStandingsView.From(_lobby?.State, _arena?.Driver.Match, _lobby?.LocalPlayerId ?? 0, _standingsHeld, id => _lobby?.State is { } state ? _lobby.Latency.Get(state, id) : null);

    /// <summary>Drains every accepted authoritative match revision for transient presentation consumers.</summary>
    internal IReadOnlyList<Core.Matches.MatchState> DrainMatchPresentation()
    {
        var states = new List<Core.Matches.MatchState>(_matchPresentation.Count);
        while (_matchPresentation.TryDequeue(out var state))
        {
            states.Add(state);
        }

        return states;
    }

    /// <summary>Active arena, absent while assembling the lobby.</summary>
    internal NetworkVehicleArena? Arena => _arena;
    /// <summary>Application Flow's completed results handoff; may be retained before disposing the arena.</summary>
    internal Core.Matches.FinalMatchResults? FinalResults => _arena?.Driver.EntryReady == true ? _arena.Driver.FinalResults : null;
    /// <summary>MenuShell remains active for Main Menu, browser and pending admission only.</summary>
    internal bool InFrontend => _lobby?.State is null && !(_exitToMenu && !LeaveComplete);
    /// <summary>Visible application match-entry state, including reconnection synchronization.</summary>
    internal ApplicationStage Stage => _exitToMenu && !LeaveComplete ? ApplicationStage.Leaving
        : LoadingMatch ? ApplicationStage.MatchLoader
        : PostMatch is not null ? ApplicationStage.Podium
        : _arena is not null ? ApplicationStage.GameLoop
        : _lobby?.State is not null ? ApplicationStage.Lobby
        : _lobby is not null || OnlineCoordinator()?.Active is not null || OnlineCoordinator()?.Busy == true ? ApplicationStage.Admission
        : _browsing || _debug.ButtonPressed ? ApplicationStage.LobbyBrowser : ApplicationStage.MainMenu;
    /// <summary>Whether map loading or network synchronization owns presentation.</summary>
    internal bool LoadingMatch => _lobby?.State?.Phase == SessionPhase.Arena && (_arena is null || !_arena.Driver.EntryReady);
    /// <summary>Cleanup completion is owned by the session and its online coordinator.</summary>
    internal bool LeaveComplete => _lobby is null && _gateway is null && OnlineCoordinator()?.CanLeave != true;
    /// <summary>Current sampled peer latency.</summary>
    internal ConnectionDiagnostic Diagnostics => TransportDiagnostics.Capture(_gateway, _lobby);

    /// <inheritdoc/>
    public override void _Ready()
    {
        Layer = 1;
        _root = new Control { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = _frontendVisible, Modulate = new Color(1, 1, 1, _frontendAlpha) };
        AddChild(_root);
        _mainMenu = new Frontend.HangingMainMenu
        {
            Name = "MainMenu", NavigationInput = NavigationInput,
            Active = () => _mainHoisting || (!_playHoisting && Stage == ApplicationStage.MainMenu && !RetainedPresentation),
            Blocked = () => OverlayOpen() || _menuTransition,
        };
        _root.AddChild(_mainMenu);
        _mainMenu.SetEntries([
            new("Play", "Play", 0, true, string.Empty, () => SetBrowser(true)),
            new("Garage", "Garage", 1, false, "Coming Soon", () => { }),
            new("Settings", "Settings", 2, true, string.Empty, () => OpenSettings()),
            new("Quit", "Quit", 3, true, string.Empty, () => QuitApplication()),
        ]);
        _playMenu = new Frontend.HangingPlayMenu
        {
            Name = "PlayMenu", Visible = false, NavigationInput = NavigationInput,
            Coordinator = () => OnlineCoordinator(), IdentityStatus = () => OnlineStatus(),
            Login = () => OnlineLogin(), Back = () => SetBrowser(false),
            CancelAdmission = Leave,
            PlayerNameChanged = name => _name.Text = name,
            Blocked = () => OverlayOpen() || _menuTransition,
            Direct = () => { if (OnlineCoordinator() is { CanStartFreshSession: false }) return; _debug.SetPressedNoSignal(true); OnlineCoordinator()?.Leave(); Render(); },
        };
        _root.AddChild(_playMenu);
        _directVersion = Frontend.HangingPlayMenu.CreateVersionLabel();
        _directVersion.Visible = false;
        _root.AddChild(_directVersion);
        var panel = new PanelContainer { Name = "LobbyBrowser", Visible = false, AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f, OffsetLeft = -300, OffsetRight = 300, OffsetTop = -330, OffsetBottom = 330 };
        _browserPanel = panel;
        _root.Resized += LayoutDirectPanel;
        LayoutDirectPanel();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("172235"), ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 18, ContentMarginBottom = 18 });
        _root.AddChild(panel);
        _browserContent = new VBoxContainer();
        _browserContent.AddThemeConstantOverride("separation", 8);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(scroll);
        _browserContent.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(_browserContent);
        _browserContent.AddChild(_name);
        _browserContent.AddChild(_back);
        _back.Pressed += () => SetBrowser(false);
        _browserContent.AddChild(_debug);
        _debug.Toggled += enabled =>
        {
            if (enabled && OnlineCoordinator() is { CanStartFreshSession: false })
            {
                _debug.SetPressedNoSignal(false);
                return;
            }
            if (enabled)
            {
                OnlineCoordinator()?.Leave();
            }
        };
        _browserContent.AddChild(_address);
        _browserContent.AddChild(_host);
        _browserContent.AddChild(_join);
        _browserContent.AddChild(_status);
        _browserContent.AddChild(_admission);
        JoinedLobby = new Frontend.JoinedLobby { Name = "JoinedLobby", Session = this };
        _root.AddChild(JoinedLobby);
        _loadingPanel = new PanelContainer { Name = "MatchLoader", AnchorRight = 1, AnchorBottom = 1 };
        _loadingPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("101826") });
        var loadingLayer = new CanvasLayer { Layer = 8 };
        AddChild(loadingLayer);
        loadingLayer.AddChild(_loadingPanel);
        var loadingCenter = new CenterContainer();
        _loadingPanel.AddChild(loadingCenter);
        var cleanup = new VBoxContainer();
        loadingCenter.AddChild(cleanup);
        cleanup.AddChild(_loadingText);
        cleanup.AddChild(_retryExit);
        _retryExit.Pressed += BeginMenuExit;
        AddChild(new PostMatch.PodiumScene { Name = "PodiumScene", Session = this });
        var matchBar = new HBoxContainer { Position = new Vector2(24, 72) };
        _root.AddChild(matchBar);
        matchBar.AddChild(_arenaStatus);
        _host.Pressed += () => Open(true, _address.Text, _name.Text);
        _join.Pressed += () => Open(false, _address.Text, _name.Text);
        Render();
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        _retiringLoads.RemoveAll(loader => loader.DiscardPending());
        if (_lobby is null)
        {
            _eventMilliseconds = Math.Max(_eventMilliseconds, Events.Milliseconds) + (delta * 1000);
            Events.AdvanceTime((ulong)_eventMilliseconds);
        }
        else
        {
            _eventMilliseconds = 0;
            Events = _lobby.Events;
        }

        _rejectionSeconds += delta;
        if (_arena is not null && _arena.Driver.RejectedPackets != _eventRejected && _rejectionSeconds >= 1)
        {
            Events.Record(Core.Events.EventCategory.Network, "Arena requests rejected", cause: "invalid, stale or unauthorized gameplay protocol", amount: Math.Max(0, _arena.Driver.RejectedPackets - _eventRejected), local: _arena.Driver.Host is null);
            _eventRejected = _arena.Driver.RejectedPackets;
            _rejectionSeconds = 0;
        }

        if (OnlineCoordinator() is { } online)
        {
            online.EventLog = Events;
        }

        Render();
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        _matchLoader?.DiscardPending(true);
        foreach (var loader in _retiringLoads) loader.DiscardPending(true);
        _retiringLoads.Clear();
        if (_onlineTransport)
        {
            OnlineCoordinator()?.PreserveResumeOnShutdown();
            OnlineCoordinator()?.Leave();
        }

        _ownedOnline?.Dispose();
        _ownedOnline = null;
        _gateway = null;
    }

    /// <summary>Controls main-menu presentation while startup owns the foreground.</summary>
    /// <param name="visible">Whether controls participate in presentation and input.</param>
    /// <param name="alpha">Initial presentation opacity.</param>
    internal void SetFrontendPresentation(bool visible, float alpha)
    {
        _frontendVisible = visible;
        _frontendAlpha = alpha;
        if (IsInsideTree())
        {
            _root.Visible = visible;
            _root.Modulate = new Color(1, 1, 1, alpha);
        }
    }

    /// <summary>Reveals the hanging menu over the persistent MenuShell at successful startup.</summary>
    internal void FadeFrontendIn()
    {
        _root.Modulate = Colors.White;
        _mainMenu.BeginEntrance();
    }

    /// <summary>Creates a listener or connects through the existing production transport.</summary>
    /// <param name="host">Whether to host.</param>
    /// <param name="address">Numeric IP and port.</param>
    /// <param name="name">Requested local name.</param>
    internal void Open(bool host, string address, string name)
    {
        if (OnlineCoordinator() is { CheckingSavedSession: true } or { NeedsRetainedValidation: true }) return;
        _failureOutcome = null;
        _browsing = true;
        _debug.ButtonPressed = true;
        Leave();
        try
        {
            _transport = new NetworkTransportNode { Name = "SessionTransport" };
            AddChild(_transport);
            _gateway = _transport.Gateway;
            ulong peer = 0;
            ulong session = 0;
            if (host)
            {
                _transport.Gateway.Listen(TransportEndpoint.DirectIp(address));
                session = (BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)) >> 1) | 1;
            }
            else
            {
                peer = _transport.Gateway.Connect(TransportEndpoint.DirectIp(address));
            }

            BindLobby(_transport.Gateway, new LobbyNetworkDriver(_transport.Gateway, session, peer, name));
            InitializeHostConfiguration();
            _message = host ? $"Hosting {address}. Non-host players must be ready to start." : $"Joining {address}…";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            Leave();
            _message = $"Could not open session: {exception.Message}";
        }

        Render();
    }

    /// <summary>Advances lobby networking or the active TS vehicle driver, then reconciles presentation phase.</summary>
    /// <param name="input">Captured local input.</param>
    internal void Advance(InputFrame input)
    {
        if (_logoutAfterLeave && LeaveComplete)
        {
            _logoutAfterLeave = false;
            OnlineLogout();
        }

        if (_exitToMenu && LeaveComplete)
        {
            _exitToMenu = false;
            _browsing = false;
            _debug.SetPressedNoSignal(false);
        }

        _standingsHeld = input.Held;
        if (_leaving && _lobby is not null)
        {
            _lobby.Pump(1.0 / 60);
            if (_lobby.LeaveComplete)
            {
                CloseSession();
            }

            return;
        }

        if (!_exitToMenu && _lobby is null && !_debug.ButtonPressed && OnlineCoordinator() is { Active: not null, TransportFactory: not null } coordinator)
        {
            try
            {
                var lobby = coordinator.Active;
                _ownedOnline = coordinator.CreateTransport();
                ulong peer = 0;
                if (coordinator.StartsGameplayAuthority)
                {
                    _ownedOnline.Listen(EosP2pTransport.Endpoint(lobby, coordinator.Identity));
                }
                else
                {
                    peer = _ownedOnline.Connect(EosP2pTransport.Endpoint(lobby, lobby.HostIdentity));
                }

                var binding = OpenOnline(_ownedOnline, peer, _name.Text);
                _ownedOnline.Authorize = binding.AuthorizePeer;
                if (!coordinator.StartsGameplayAuthority)
                {
                    binding.Driver.Reconnect = () =>
                    {
                        _ownedOnline.Stop();
                        return _ownedOnline.Connect(EosP2pTransport.Endpoint(coordinator.Active ?? throw new InvalidOperationException("Session unavailable"), _ownedOnline.GameplayHost ?? lobby.HostIdentity));
                    };
                }

                _ownedOnline.ConnectionChanged += change =>
                {
                    _message = change.Detail;
                    if (change.State == TransportConnectionState.Disconnected && change.RemotePeerId == 0)
                    {
                        _transportFailure = change.Detail;
                    }
                };
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                if (coordinator.HasRetainedDecision)
                {
                    coordinator.FailRetainedConnection();
                    CloseSession(false);
                }
                else
                {
                    Leave();
                    coordinator.Leave();
                }

                _message = exception.Message;
            }
        }

        if (_onlineTransport && OnlineCoordinator()?.Active is null)
        {
            CloseSession(false);
        }

        if (_lobby is null)
        {
            return;
        }

        if (_arena is null)
        {
            _lobby.Pump(1.0 / 60);
        }
        else
        {
            _arena.Advance(_arena.Driver.EntryReady && PostMatch is null ? input : default);
            _arena.Visible = _arena.Driver.EntryReady && PostMatch is null;
        }

        if (_arena?.Driver is { InitialEntryReleased: false, Failure.Length: > 0 } failed && _lobby.Failure.Length == 0 && _transportFailure is null)
        {
            FailEntry(failed.Failure);
        }

        if (_transportFailure is not null || _lobby.Failure.Length > 0 || _arena?.Driver.Failure.Length > 0)
        {
            Events.Record(Core.Events.EventCategory.Network, "Session failed", cause: _transportFailure is not null ? "transport failure" : "admission or arena synchronization failure", local: _lobby.Authority is null);
            string failure = _transportFailure ?? (_lobby.Failure.Length > 0 ? _lobby.Failure : _arena!.Driver.Failure);
            if (_onlineTransport && OnlineCoordinator() is { HasRetainedDecision: true } retained)
            {
                retained.FailRetainedConnection();
                CloseSession(false);
                _message = failure;
                return;
            }

            _failureOutcome = failure;
            Leave();
            _message = failure;
            return;
        }

        if (_lobby.State?.Phase != SessionPhase.Arena || _arenaGeneration != _lobby.State.Match)
        {
            RemoveArena();
            _entryFailure = string.Empty;
            _entryFailureSeconds = 0;
        }

        if (_entryFailure.Length > 0)
        {
            _entryFailureSeconds += 1.0 / 60;
            if (_lobby.Authority is not null) RecoverEntry(_entryFailure);
            else if (!_lobby.Reconnecting && _lobby.Migration?.Frozen != true && (int)(_entryFailureSeconds * 60) % 30 == 1)
            {
                try
                {
                    _lobby.SendGameplay(new TransportMessage(_lobby.ServerPeer, MatchEntryCodec.Encode(_lobby.State!.Match, MatchEntryCodec.Failed), TransportDelivery.Reliable));
                }
                catch (InvalidOperationException)
                {
                    // A lost connection is handled by the existing reconnect/failure owner on its next pump.
                }
            }
            if (_entryFailureSeconds > 35) { _failureOutcome = _entryFailure; Leave(); }
            return;
        }

        // A recovered coherent checkpoint may precede the previously presented finish.
        // Follow accepted authority after synchronization rather than pinning stale scene state.
        if (_arena?.Driver.EntryReady == true && PostMatch is not null && FinalResults is null)
        {
            PostMatch = null;
            PostMatchStatus = string.Empty;
        }

        if (_lobby.State is { Phase: SessionPhase.Arena } completed && FinalResults is { } results &&
            (PostMatch is null || PostMatch.Results.Tick != results.Tick || PostMatch.Results.Outcome != results.Outcome ||
             !PostMatch.Results.Standings.SequenceEqual(results.Standings)))
        {
            PostMatch = new PostMatchContext(completed, results);
            _standingsHeld = 0;
            _arena!.Visible = false;
        }

        if (_lobby.State?.Phase == SessionPhase.Arena && _arena is null)
        {
            try
            {
                if (_matchLoader is null)
                {
                    _arenaGeneration = _lobby.State.Match;
                    LobbyNotice = string.Empty;
                    _matchLoader = CreateMatchLoader(_lobby.State.Map);
                    Render();
                    return;
                }

                _loadSeconds += 1.0 / 60;
                if (_loadSeconds > 30)
                {
                    throw new InvalidOperationException("Match resource loading timed out.");
                }

                _matchLoader.Advance();
                if (!_matchLoader.Complete)
                {
                    return;
                }

                _eventRejected = 0;
                _arena = new NetworkVehicleArena { Name = "SessionArena", PreparedMap = _matchLoader.MapScene, ApplicationEntry = true, Visible = false, CameraInput = NavigationInput, CameraSettings = CameraSettings };
                _arena.Initialize(_gateway!, _lobby.Authority is null ? 0 : _arenaGeneration, _lobby.ServerPeer, _lobby, _lobby.Authority?.Configuration.Configuration);
                _arena.Driver.MatchReceived += QueueMatchPresentation;
                AddChild(_arena);
                if (_forceStart && _arena.Driver.Host is not null)
                {
                    _arena.Driver.Host.ForceStart(0);
                }

                _forceStart = false;
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                FailEntry("Match loading failed: " + exception.Message);
            }
        }
    }

    // Recovery reuses the existing authoritative Arena -> Lobby transition. A failed client
    // waits for that snapshot; it never invents a replacement lobby or admission record.
    private bool RecoverEntry(string reason)
    {
        if (_lobby is not { Authority: not null, State.Phase: SessionPhase.Arena } || _arena?.Driver.InitialEntryReleased == true) return false;
        if (!_lobby.Request(LobbyCommand.Return)) return false;
        LobbyNotice = reason + " Ready up to retry.";
        return true;
    }

    private void FailEntry(string reason)
    {
        if (RecoverEntry(reason)) { RemoveArena(); return; }
        _entryFailure = reason;
        LobbyNotice = reason + " Ready up to retry.";
        RemoveArena();
    }

    internal bool ConfigureLobbyOptions(IReadOnlyDictionary<string, double> edits, out string error)
    {
        error = "Only the current host can edit settings in the lobby.";
        if (Stage != ApplicationStage.Lobby || OverlayOpen() || _lobby is not { Authority: not null, Reconnecting: false, Failure.Length: 0 } || _lobby.Migration?.Frozen == true || edits.Keys.Any(key => key is not ("match.mode" or "match.kill_target"))) return false;
        if (!_lobby.Authority.TryConfigure(0, edits, out error)) return false;

        return true;
    }

    /// <summary>Uses the existing host Return or individual client Leave action from final results.</summary>
    internal void LeaveResults()
    {
        if (_lobby?.Authority is not null)
        {
            _lobby.Request(LobbyCommand.Return);
        }
        else
        {
            Leave();
        }
    }

    /// <summary>Explicitly leaves or closes the session and releases its native transport.</summary>
    internal void Leave()
    {
        if (_leaving)
        {
            return;
        }

        if (_lobby?.BeginLeave() == true)
        {
            _leaving = true;
            _message = "Leaving session…";
            return;
        }

        CloseSession();
    }

    /// <summary>Enters the existing lobby and arena presentation using separately established online transport.</summary>
    /// <returns>The admission binding used by the authenticated transport adapter.</returns>
    /// <param name="gateway">Caller-owned authenticated gateway supplied by the transport integration.</param>
    /// <param name="serverPeer">Connected server peer for a client, zero for a host.</param>
    /// <param name="name">Requested gameplay display name.</param>
    internal OnlineSessionBinding OpenOnline(ITransportGateway gateway, ulong serverPeer, string name)
    {
        if (_lobby is not null || _transport is not null)
        {
            throw new InvalidOperationException("Leave the current gameplay session before attaching online transport.");
        }

        var coordinator = OnlineCoordinator() ?? throw new InvalidOperationException("Online services unavailable.");
        var binding = coordinator.AttachTransport(gateway, serverPeer, name);
        BindLobby(gateway, binding.Driver);
        InitializeHostConfiguration();
        _onlineTransport = true;
        _debug.ButtonPressed = false;
        _message = "Online transport connected. Non-host players must be ready to start.";
        return binding;
    }

    /// <summary>Validates and applies a host edit, then persists the accepted effective configuration.</summary>
    /// <returns>Whether the operation was accepted.</returns>
    /// <param name="edits">Stable gameplay keys and requested values.</param>
    /// <param name="error">Safe validation feedback.</param>
    internal bool ConfigureDeveloperOptions(IReadOnlyDictionary<string, double> edits, out string error)
    {
        error = "Shared configuration is unavailable.";
        return CanConfigureDeveloperOptions && _lobby!.RequestConfiguration(edits, out error);
    }

    internal bool CanConfigureDeveloperOptions => Development.DeveloperTools.Enabled && !_leaving &&
        _lobby?.CanConfigure == true && (_arena is null || _arena.Driver.IsActive);

    /// <summary>Uses existing inventory authority; a joined client never sends a grant request.</summary>
    /// <returns>Whether the operation was accepted.</returns>
    /// <param name="item">Implemented item to grant.</param>
    internal bool GiveDeveloperItem(Core.Items.HeldItem item) => IsDeveloperHost && _arena?.Driver.GiveDeveloperItem(item) == true;

    /// <summary>Enters a normal arena and arms only its authoritative match countdown override.</summary>
    /// <returns>Whether the operation was accepted.</returns>
    internal bool ForceDeveloperStart()
    {
        if (!IsDeveloperHost)
        {
            return false;
        }

        if (_arena is not null)
        {
            return _arena.Driver.ForceDeveloperStart();
        }

        _lobby!.Authority!.SetReady(0, true);
        _forceStart = _lobby.Request(LobbyCommand.Start);
        return _forceStart;
    }

    private void SetBrowser(bool visible)
    {
        if (_menuTransition) return;
        _menuTransition = true;
        if (visible)
        {
            _mainHoisting = true;
            _mainMenu.Hoist(() =>
            {
                _mainHoisting = false;
                _browsing = true;
                _menuTransition = false;
                _playMenu.BeginEntrance();
                Render();
            });
        }
        else
        {
            _debug.SetPressedNoSignal(false);
            _playHoisting = true;
            _playMenu.Hoist(() =>
            {
                _playHoisting = false;
                _browsing = false;
                _menuTransition = false;
                _mainMenu.BeginEntrance();
                Render();
            });
        }
        Render();
    }

    /// <summary>Binds the existing transport/session owner to the reconstructable application presentation.</summary>
    internal void BindLobby(ITransportGateway gateway, LobbyNetworkDriver lobby)
    {
        _gateway = gateway;
        _lobby = lobby;
        _lobby.RecoverMatchEntry = () => RecoverEntry("A participant could not enter the match.");
        Events = lobby.Events;
    }

    private void InitializeHostConfiguration()
    {
        if (_lobby?.Authority is { } authority && authority.State.AuthorityEpoch == 1)
        {
            var initial = DeveloperSettings?.LoadForHost() ?? Core.Development.GameplayConfiguration.HostedDefaults;
            authority.TryConfigure(0, Core.Development.GameplayOptions.All.ToDictionary(option => option.Key, option => option.Read(initial)), out _);
        }
    }

    private void CloseSession(bool leaveOnline = true)
    {
        if (_lobby is not null)
        {
            Events.Record(Core.Events.EventCategory.Session, _lobby.Authority is null ? (_leaving ? "Left session" : "Session ended") : "Session closed", actor: _lobby.LocalPlayerId, local: _lobby.Authority is null);
        }

        _entryFailure = string.Empty;
        _entryFailureSeconds = 0;
        LobbyNotice = string.Empty;
        _forceStart = false;
        _leaving = false;
        RemoveArena();
        _lobby = null;
        Events.PlayerName = null;
        if (leaveOnline && (_onlineTransport || OnlineCoordinator()?.CanLeave == true))
        {
            OnlineCoordinator()?.Leave();
            _onlineTransport = false;
        }

        _gateway = null;
        _onlineTransport = false;
        _ownedOnline?.Dispose();
        _ownedOnline = null;
        _transportFailure = null;
        if (_transport is not null)
        {
            RemoveChild(_transport);
            _transport.QueueFree();
            _transport = null;
        }

        _message = _failureOutcome ?? "Choose or host a game. Non-host players must be ready before starting.";
    }

    private void RemoveArena()
    {
        PostMatch = null;
        PostMatchStatus = string.Empty;
        if (_matchLoader is not null && !_matchLoader.DiscardPending()) _retiringLoads.Add(_matchLoader);
        _matchLoader = null;
        _loadSeconds = 0;
        _standingsHeld = 0;
        if (_arena is not null)
        {
            _arena.Driver.MatchReceived -= QueueMatchPresentation;
            _arena.Driver.Dispose();
            _forceStart = false;

            if (_arena.GetParent() == this)
            {
                RemoveChild(_arena);
            }

            _arena.QueueFree();
            _arena = null;
        }

        _matchPresentation.Clear();
    }

    private void QueueMatchPresentation(Core.Matches.MatchState state)
    {
        if (_matchPresentation.Count == 256)
        {
            _matchPresentation.Dequeue();
        }

        _matchPresentation.Enqueue(state);
    }

    /// <summary>Host Start uses authoritative non-host readiness and immediately enters the loader stage.</summary>
    internal bool StartFromLobby()
    {
        if (_leaving || OverlayOpen() || _lobby is not { Authority: not null, State.Phase: SessionPhase.Lobby }) return false;
        bool accepted = _lobby.Request(LobbyCommand.Start);
        if (accepted) { LobbyNotice = string.Empty; Render(); }
        return accepted;
    }

    private void LayoutDirectPanel()
    {
        // Keep the legacy fallback's existing scrollable controls above the version footer.
        float width = Math.Min(600, Math.Max(1, _root.Size.X - 32));
        float height = Math.Min(660, Math.Max(1, _root.Size.Y - 76));
        _browserPanel.OffsetLeft = -width / 2;
        _browserPanel.OffsetRight = width / 2;
        _browserPanel.OffsetTop = -height / 2 - 22;
        _browserPanel.OffsetBottom = height / 2 - 22;
    }

    private void Render()
    {
        bool active = _lobby?.State is not null;
        bool pending = !active && (_lobby is not null || OnlineCoordinator()?.Active is not null || OnlineCoordinator()?.Busy == true);
        bool browsing = _browsing || _debug.ButtonPressed || RetainedPresentation;
        _admission.Visible = pending && !RetainedPresentation;
        _admission.Text = "JOINING / CREATING LOBBY\n" + (OnlineCoordinator()?.Busy == true ? OnlineCoordinator()!.Status : "Waiting for authoritative admission…");
        _back.Visible = !active && browsing && !pending;
        JoinedLobby.Refresh();
        bool exiting = Stage == ApplicationStage.Leaving;
        _loadingPanel.Visible = LoadingMatch || exiting;
        _retryExit.Visible = exiting && OnlineCoordinator() is { CanLeave: true, Busy: false };
        _loadingText.Text = exiting ? "LEAVING SESSION\n" + (OnlineCoordinator()?.Status ?? "Completing session cleanup…")
            : _arena is null ? $"MATCH LOADER\nLoading selected map and resources… {_matchLoader?.Progress * 100:0}%" : "MATCH SYNC\nWaiting for authoritative synchronization…";
        bool arena = _arena is not null || _lobby?.State?.Phase == SessionPhase.Arena;
        bool decision = RetainedPresentation;
        _browserPanel.Visible = !exiting && !active && (browsing || pending) && _debug.ButtonPressed;
        _directVersion.Visible = _browserPanel.Visible;
        _playMenu.Visible = !exiting && !active && !_mainHoisting && !_debug.ButtonPressed && (browsing || pending || _playHoisting);
        _mainMenu.RefreshPresentation();
        ((Control)_arenaStatus.GetParent()).Visible = arena;
        _debug.Visible = !active && browsing && !decision && !pending;
        _status.Visible = !decision && (browsing || pending);
        _name.Visible = !active && browsing && !decision && !pending;
        _address.Visible = !active && _debug.ButtonPressed;
        _host.Visible = !active && _debug.ButtonPressed && !pending;
        _join.Visible = !active && _debug.ButtonPressed && !pending;
        bool detecting = OnlineCoordinator() is { CheckingSavedSession: true } or { NeedsRetainedValidation: true };
        _debug.Disabled = detecting;
        _host.Disabled = detecting;
        _join.Disabled = detecting;
        _arenaStatus.Text = _leaving ? "Leaving session…" : (_lobby?.Migration?.Frozen == true ? _lobby.Migration.Status : _lobby?.ResumeStatus) ?? string.Empty;
        _status.Text = _gateway is null ? _message : $"{_gateway.Name}: {_gateway.ConnectionState}\n{(_lobby?.Migration?.Frozen == true ? _lobby.Migration.Status : _lobby?.ResumeStatus.Length > 0 ? _lobby.ResumeStatus : _message)}";
    }
}
