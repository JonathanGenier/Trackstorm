using Godot;
using Trackstorm.Client.Online;
using Trackstorm.Core.Input;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Networking;

/// <summary>Development Host/Join and lobby presentation reconstructed from authoritative Core state.</summary>
internal sealed partial class DevelopmentSession : CanvasLayer
{
    private readonly LineEdit _name = new() { Name = "PlayerName", Text = "Player", PlaceholderText = "Display name", MaxLength = 96 };
    private readonly LineEdit _address = new() { Name = "DirectAddress", Text = "127.0.0.1:27020", PlaceholderText = "IP:port or [IPv6]:port" };
    private readonly Label _status = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _roster = new();
    private readonly Label _arenaStatus = new();
    private readonly Button _host = new() { Text = "Host Game" };
    private readonly Button _join = new() { Text = "Join Game by address" };
    private readonly Button _ready = new() { Text = "Ready" };
    private readonly Button _start = new() { Text = "Start Match (host only)" };
    private readonly Button _leave = new() { Text = "Leave session" };
    private readonly CheckButton _debug = new() { Text = "Developer fallback: Direct-IP / LAN" };
    private NetworkTransportNode? _transport;
    private ITransportGateway? _gateway;
    private bool _onlineTransport;
    private EosP2pTransport? _ownedOnline;
    private string? _transportFailure;
    private InputButtons _standingsHeld;
    private LobbyNetworkDriver? _lobby;
    private NetworkVehicleArena? _arena;
    private VBoxContainer _menu = null!;
    private string _message = "Choose or host a game. Up to 8 players; everyone must be ready.";
    private ulong _arenaGeneration;
    private OnlineLobbyPanel _online = null!;
    private bool _leaving;
    private bool _logoutAfterLeave;

    /// <summary>Authenticated online coordinator supplied by application composition.</summary>
    internal Func<OnlineLobbyCoordinator?> OnlineCoordinator { get; set; } = () => null;

    /// <summary>Authentication state supplied by application composition.</summary>
    internal Func<EosLobbyStatus> OnlineStatus { get; set; } = () => EosLobbyStatus.Unavailable;
    /// <summary>Explicit login/retry action supplied by the identity owner.</summary>
    internal Action OnlineLogin { get; set; } = () => { };
    /// <summary>Explicit logout action supplied by the identity owner.</summary>
    internal Action OnlineLogout { get; set; } = () => { };

    /// <summary>Production lobby exposed for runtime integration verification.</summary>
    internal LobbyNetworkDriver? Lobby => _lobby;
    /// <summary>Shared projection used by both standings and the existing HUD badge.</summary>
    internal Hud.MatchStandingsView Standings => Hud.MatchStandingsView.From(_lobby?.State, _arena?.Driver.Match, _lobby?.LocalPlayerId ?? 0, _standingsHeld, id => _lobby?.State is { } state ? _lobby.Latency.Get(state, id) : null);

    /// <summary>Active arena, absent while assembling the lobby.</summary>
    internal NetworkVehicleArena? Arena => _arena;
    /// <summary>Cleanup completion is owned by the session and its online coordinator.</summary>
    internal bool LeaveComplete => _lobby is null && _gateway is null && OnlineCoordinator()?.CanLeave != true;
    /// <summary>Current sampled peer latency.</summary>
    internal ConnectionDiagnostic Diagnostics => TransportDiagnostics.Capture(_gateway, _lobby);

    /// <inheritdoc/>
    public override void _Ready()
    {
        Layer = 1;
        var root = new Control { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(root);
        var panel = new PanelContainer { AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f, OffsetLeft = -300, OffsetRight = 300, OffsetTop = -330, OffsetBottom = 330 };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("172235"), ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 18, ContentMarginBottom = 18 });
        root.AddChild(panel);
        _menu = new VBoxContainer();
        _menu.AddThemeConstantOverride("separation", 8);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(scroll);
        _menu.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(_menu);
        _menu.AddChild(new Label { Text = "TRACKSTORM · MULTIPLAYER", HorizontalAlignment = HorizontalAlignment.Center });
        _online = new OnlineLobbyPanel { Coordinator = () => OnlineCoordinator(), IdentityStatus = () => OnlineStatus(), Login = () => OnlineLogin(), Logout = () => OnlineLogout() };
        _online.LeaveSession = Leave;
        _online.Logout = () =>
        {
            _logoutAfterLeave = true;
            Leave();
        };
        _menu.AddChild(_debug);
        _menu.AddChild(_online);
        _debug.Toggled += enabled =>
        {
            if (enabled)
            {
                OnlineCoordinator()?.Leave();
            }
        };
        _menu.AddChild(_name);
        _menu.AddChild(_address);
        _menu.AddChild(_host);
        _menu.AddChild(_join);
        _menu.AddChild(_status);
        _menu.AddChild(_roster);
        _menu.AddChild(_ready);
        _menu.AddChild(_start);
        _menu.AddChild(_leave);
        var matchBar = new HBoxContainer { Position = new Vector2(24, 72) };
        root.AddChild(matchBar);
        matchBar.AddChild(_arenaStatus);
        _host.Pressed += () => Open(true, _address.Text, _name.Text);
        _join.Pressed += () => Open(false, _address.Text, _name.Text);
        _ready.Pressed += () => _lobby?.Request(LobbyCommand.Ready, !(_lobby.State?.Players.Single(player => player.Id == _lobby.LocalPlayerId).Ready ?? false));
        _start.Pressed += () =>
        {
            if (_lobby?.Request(LobbyCommand.Start) != true)
            {
                _message = "Start requires a connected, fully ready lobby.";
            }
        };
        _leave.Pressed += Leave;
        Render();
    }

    /// <inheritdoc/>
    public override void _Process(double delta) => Render();

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        if (_onlineTransport)
        {
            OnlineCoordinator()?.PreserveResumeOnShutdown();
            OnlineCoordinator()?.Leave();
        }

        _ownedOnline?.Dispose();
        _ownedOnline = null;
        _gateway = null;
    }

    /// <summary>Creates a listener or connects through the existing production transport.</summary>
    /// <param name="host">Whether to host.</param>
    /// <param name="address">Numeric IP and port.</param>
    /// <param name="name">Requested local name.</param>
    internal void Open(bool host, string address, string name)
    {
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

            _lobby = new LobbyNetworkDriver(_transport.Gateway, session, peer, name);
            _message = host ? $"Hosting {address}. Everyone must be ready to start." : $"Joining {address}…";
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

        if (_lobby is null && !_debug.ButtonPressed && OnlineCoordinator() is { Active: not null, TransportFactory: not null } coordinator)
        {
            try
            {
                var lobby = coordinator.Active;
                _ownedOnline = coordinator.CreateTransport();
                ulong peer = 0;
                if (coordinator.IsHost)
                {
                    _ownedOnline.Listen(EosP2pTransport.Endpoint(lobby, coordinator.Identity));
                }
                else
                {
                    peer = _ownedOnline.Connect(EosP2pTransport.Endpoint(lobby, lobby.Owner));
                }

                var binding = OpenOnline(_ownedOnline, peer, _name.Text);
                _ownedOnline.Authorize = binding.AuthorizePeer;
                if (!coordinator.IsHost)
                {
                    binding.Driver.Reconnect = () =>
                    {
                        _ownedOnline.Stop();
                        return _ownedOnline.Connect(EosP2pTransport.Endpoint(coordinator.Active ?? throw new InvalidOperationException("Session unavailable"), lobby.Owner));
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
                Leave();
                coordinator.Leave();
                _message = exception.Message;
            }
        }

        if (_onlineTransport && OnlineCoordinator()?.Active is null)
        {
            Leave();
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
            _arena.Advance(input);
        }

        if (_transportFailure is not null || _lobby.Failure.Length > 0 || _arena?.Driver.Failure.Length > 0)
        {
            string failure = _transportFailure ?? (_lobby.Failure.Length > 0 ? _lobby.Failure : _arena!.Driver.Failure);
            Leave();
            _message = failure;
            return;
        }

        if (_lobby.State?.Phase != SessionPhase.Arena || _arenaGeneration != _lobby.State.Match)
        {
            RemoveArena();
        }

        if (_lobby.State?.Phase == SessionPhase.Arena && _arena is null)
        {
            _arenaGeneration = _lobby.State.Match;
            _arena = new NetworkVehicleArena { Name = "SessionArena" };
            _arena.Initialize(_gateway!, _lobby.Authority is null ? 0 : _arenaGeneration, _lobby.ServerPeer, _lobby);
            AddChild(_arena);
        }
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
    /// <param name="gateway">Caller-owned authenticated gateway supplied by the transport integration.</param>
    /// <param name="serverPeer">Connected server peer for a client, zero for a host.</param>
    /// <param name="name">Requested gameplay display name.</param>
    /// <returns>The admission binding used by the authenticated transport adapter.</returns>
    internal OnlineSessionBinding OpenOnline(ITransportGateway gateway, ulong serverPeer, string name)
    {
        if (_lobby is not null || _transport is not null)
        {
            throw new InvalidOperationException("Leave the current gameplay session before attaching online transport.");
        }

        var coordinator = OnlineCoordinator() ?? throw new InvalidOperationException("Online services unavailable.");
        var binding = coordinator.AttachTransport(gateway, serverPeer, name);
        _gateway = gateway;
        _lobby = binding.Driver;
        _onlineTransport = true;
        _debug.ButtonPressed = false;
        _message = "Online transport connected. Everyone must be ready to start.";
        return binding;
    }

    private void CloseSession()
    {
        _leaving = false;
        RemoveArena();
        _lobby = null;
        if (_onlineTransport || OnlineCoordinator()?.CanLeave == true)
        {
            OnlineCoordinator()?.Leave();
            _onlineTransport = false;
        }

        _gateway = null;
        _ownedOnline?.Dispose();
        _ownedOnline = null;
        _transportFailure = null;
        if (_transport is not null)
        {
            RemoveChild(_transport);
            _transport.QueueFree();
            _transport = null;
        }

        _message = "Choose or host a game. Everyone must be ready before starting.";
        if (_logoutAfterLeave)
        {
            _logoutAfterLeave = false;
            OnlineLogout();
        }
    }

    private void RemoveArena()
    {
        if (_arena is not null)
        {
            RemoveChild(_arena);
            _arena.QueueFree();
            _arena = null;
        }
    }

    private void Render()
    {
        bool active = _lobby is not null;
        bool arena = _arena is not null || _lobby?.State?.Phase == SessionPhase.Arena;
        _menu.GetParent<ScrollContainer>().GetParent<Control>().Visible = !arena;
        ((Control)_arenaStatus.GetParent()).Visible = arena;
        _online.Visible = !arena && !_debug.ButtonPressed;
        _debug.Visible = !active;
        _status.Visible = true;
        _name.Visible = !active;
        _address.Visible = !active && _debug.ButtonPressed;
        _host.Visible = !active && _debug.ButtonPressed;
        _join.Visible = !active && _debug.ButtonPressed;
        _leave.Visible = active && !arena;
        _ready.Visible = _lobby?.State is not null && !arena;
        _start.Visible = _lobby?.Authority is not null && !arena;
        _start.Disabled = _leaving || _lobby?.State?.CanStart != true;
        _ready.Disabled = _leaving || _lobby?.Reconnecting == true;
        _arenaStatus.Text = _leaving ? "Leaving session…" : _lobby?.ResumeStatus ?? string.Empty;
        _status.Text = _gateway is null ? _message : $"{_gateway.Name}: {_gateway.ConnectionState}\n{(_lobby?.ResumeStatus.Length > 0 ? _lobby.ResumeStatus : _message)}";
        _roster.Text = _lobby?.State is not LobbySnapshot state ? string.Empty : $"{state.Players.Count}/8 slots\n" + string.Join("\n", state.Players.Select(player => $"{(!player.Connected ? "↻ RECONNECTING" : player.Ready ? "✓ READY" : "○ WAITING")}   {player.Name}  #{player.Id}{(player.Id == 1 ? " · HOST" : string.Empty)}{(player.Id == _lobby.LocalPlayerId ? " · YOU" : string.Empty)}"));
        _ready.Text = _lobby?.State?.Players.Single(player => player.Id == _lobby.LocalPlayerId).Ready == true ? "Unready" : "Ready";
    }
}
