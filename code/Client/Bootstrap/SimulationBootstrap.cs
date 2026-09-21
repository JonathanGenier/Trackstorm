using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Online;
using Trackstorm.Client.Settings;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Simulation;

namespace Trackstorm.Client.Bootstrap;

/// <summary>
/// Composes the authoritative Core simulation with Godot timing and local input.
/// </summary>
public sealed partial class SimulationBootstrap : Node
{
    private readonly SimulationConfiguration _configuration =
        new(SimulationConfiguration.DefaultTicksPerSecond);

    private PlayerInput _playerInput = null!;
    private VehicleArena? _arena;
    private DevelopmentSession? _session;
    private SettingsPanel _settingsPanel = null!;
    private PlayerSettingsController _settings = null!;
    private EosIdentityNode? _online;
    private StartupController? _startup;
    private bool _quitRequested;

    /// <summary>
    /// Gets the latest authoritative tick observed from Core.
    /// </summary>
    public ulong CurrentSimulationTick => _session?.Arena?.Driver.Latest?.Tick ?? _arena?.Simulation.State.Tick ?? 0;

    /// <summary>Whether this scene presents the complete product startup sequence.</summary>
    [Export]
    public bool StartupEnabled { get; set; }

    /// <summary>Allows isolated runtime verification without authenticating an online identity.</summary>
    internal bool OnlineEnabled { get; set; } = true;
    /// <summary>Prevents recursive composition when the exported executable runs its verification entry point.</summary>
    internal bool VerificationChild { get; set; }
    /// <summary>Lets a runtime harness observe production cleanup before it tears down its own scene.</summary>
    internal bool VerificationOwnsExit { get; set; }
    /// <summary>Optional isolated storage for native integration checks.</summary>
    internal string? SettingsPath { get; set; }

    /// <inheritdoc />
    public override void _Ready()
    {
        string canonicalVersion = Core.Sessions.GameVersion.Current.ToString();
        if (OS.GetCmdlineUserArgs().Contains("--version-check"))
        {
            string embedded = ProjectSettings.GetSetting("application/config/version", string.Empty).AsString();
            bool valid = OS.HasFeature("editor") || embedded == canonicalVersion;
            GD.Print($"Trackstorm version: {canonicalVersion}; Godot metadata: {embedded}; exported: {!OS.HasFeature("editor")}");
            GetTree().Quit(valid ? 0 : 1);
            return;
        }

        ProjectSettings.SetSetting("application/config/version", canonicalVersion);

        if (OnlineEnabled && OS.GetCmdlineUserArgs().Contains("--statistics-check"))
        {
            // Replace the scene so the normal input owner exits before the verification scene creates its own.
            GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://scenes/verification/statistic_checks.tscn");
            return;
        }

        if (!VerificationChild && OS.GetCmdlineUserArgs().Contains("--event-log-check"))
        {
            var input = GetNodeOrNull<PlayerInput>("PlayerInput");
            if (input is not null)
            {
                RemoveChild(input);
                input.Free();
            }

            AddChild(new Verification.EventLogIntegrationChecks());
            return;
        }

        if (OS.GetCmdlineUserArgs().Contains("--eos-multiplayer-check"))
        {
            AddChild(new Trackstorm.Client.Verification.EosMultiplayerChecks());
            return;
        }

        if (OS.GetCmdlineUserArgs().Contains("--eos-check"))
        {
            AddChild(new Trackstorm.Client.Verification.EosIntegrationChecks());
            return;
        }

        if (StartupEnabled)
        {
            bool startupCheck = OS.GetCmdlineUserArgs().Contains("--startup-check");
            if (startupCheck)
            {
                OnlineEnabled = false;
            }

            _startup = new StartupController
            {
                Name = "StartupController",
                InitializeApplication = () => InitializeApplication(false),
                PrepareFrontend = PrepareFrontend,
                PresentMainMenu = PresentMainMenu,
                AbortApplication = AbortApplicationInitialization,
                FailNextFrontendDependency = startupCheck,
                FailNextFrontendSetup = startupCheck,
                FailNextInitialization = startupCheck,
            };
            AddChild(_startup);
            if (startupCheck)
            {
                var checks = new Verification.StartupIntegrationChecks();
                checks.Initialize(_startup);
                AddChild(checks);
            }

            return;
        }

        InitializeApplication(true);
    }

    /// <inheritdoc />
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest && _settingsPanel is not null)
        {
            RequestQuit();
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _startup?.SetFrontendActive(_arena is null && (_session?.InFrontend ?? true));
        if (_quitRequested && (_session?.LeaveComplete ?? true) && _online?.Coordinator?.CanLeave != true)
        {
            // Normal tree teardown owns settings flush, transport disposal, platform release and terminal SDK shutdown.
            if (!VerificationOwnsExit)
            {
                GetTree().Quit();
            }
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_playerInput is not null)
        {
            _playerInput.FrameCaptured -= OnFrameCaptured;
        }
    }

    private bool InitializeApplication(bool presentFrontend)
    {
        PrepareFrontend();
        var panel = new SettingsPanel { Name = "SettingsPanel" };
        panel.Initialize(_settings, _playerInput.Adapter);
        panel.SetFrontendPresentation(presentFrontend, presentFrontend ? 1 : 0);
        _settings.AddChild(panel);
        _settingsPanel = panel;
        var devTools = new Development.DevToolsShell { Name = "DevTools" };
        devTools.Initialize(_playerInput.Adapter);
        devTools.Configs.Session = () => _session;
        devTools.Configs.Practice = () => _arena;
        devTools.Stats.Capture = selected => Statistics.RuntimeStatistics.Capture(_session, _arena, selected, _online?.DeveloperDiagnostics ?? "EOS unavailable.");
        devTools.Logs.Source = () => _arena?.Simulation.Events ?? _session?.Events;
        panel.DiagnosticOverlayOpen = () => devTools.IsOpen;
        devTools.NavigationClosed = panel.ResumeNavigation;
        panel.OpenDeveloperTools = () => devTools.Open(Development.DevToolsTab.Configs);
        AddChild(devTools);
        panel.ArenaAvailable = () => _arena is not null || _session?.Arena is not null || _quitRequested;
        panel.LeaveToMainMenu = LeaveToMainMenu;
        panel.QuitApplication = RequestQuit;
        panel.ExitStatus = () => !_quitRequested ? null : _online?.Coordinator is { CanLeave: true, Busy: false } coordinator
            ? coordinator.Status + " Select Quit to retry."
            : "Closing session…";
        GetTree().AutoAcceptQuit = false;
        var combatHud = new Hud.CombatHud
        {
            Name = "CombatHud",
            Vehicle = () => _session?.Arena?.LocalState ?? _arena?.Player.Snapshot,
            Slot = () => _session?.Arena?.Driver.LocalItem,
            Units = () => _settings.Current.SpeedUnit,
            Position = () => _session?.Standings.Position ?? "--",
            Match = () => _session?.Arena?.Driver.Match,
            MatchUpdates = () => _session?.DrainMatchPresentation() ?? Array.Empty<Core.Matches.MatchState>(),
            Player = () => _session?.Lobby?.LocalPlayerId ?? 0,
        };
        AddChild(combatHud);
        AddChild(new Hud.ActivityFeed
        {
            Name = "ActivityFeed",
            Source = () => _arena?.Simulation.Events ?? _session?.Events,
            Gameplay = () => _arena is not null || _session?.Arena is not null,
        });
        AddChild(new Hud.MatchStandings { Name = "MatchStandings", View = () => _session?.Standings });
        EosIdentityNode? online = null;
        if (OnlineEnabled && !OS.GetCmdlineUserArgs().Contains("--local-practice"))
        {
            online = new EosIdentityNode { Name = "EosIdentity" };
            AddChild(online);
            _online = online;
        }

        string[] networkArguments = OS.GetCmdlineUserArgs().Where(argument => argument.StartsWith("--transport-host=", StringComparison.Ordinal) || argument.StartsWith("--transport-connect=", StringComparison.Ordinal)).ToArray();
        if (networkArguments.Length > 1)
        {
            throw new ArgumentException("Specify exactly one transport host or connect endpoint.");
        }

        if (OS.GetCmdlineUserArgs().Contains("--local-practice"))
        {
            _arena = new VehicleArena { Name = "VehicleArena" };
            AddChild(_arena);
        }
        else
        {
            _session = new DevelopmentSession
            {
                Name = "DevelopmentSession",
                OnlineCoordinator = () => online?.Coordinator,
                OnlineStatus = () => online?.Status ?? EosLobbyStatus.Unavailable,
                OnlineLogin = () => online?.Login(),
                OnlineLogout = () => online?.Logout(),
                DeveloperSettings = Development.DeveloperTools.Enabled ? new Development.DeveloperSettingsStore(SettingsPath is null ? ProjectSettings.GlobalizePath("user://developer-settings.jsonl") : SettingsPath + ".developer.jsonl") : null
            };
            _session.SetFrontendPresentation(presentFrontend, presentFrontend ? 1 : 0);
            AddChild(_session);
            if (networkArguments.Length == 1)
            {
                string argument = networkArguments[0];
                string endpoint = argument[(argument.IndexOf('=') + 1)..];
                string name = OS.GetCmdlineUserArgs().FirstOrDefault(value => value.StartsWith("--player-name=", StringComparison.Ordinal))?[14..] ?? "Player";
                _session.Open(argument.StartsWith("--transport-host=", StringComparison.Ordinal), endpoint, name);
            }
        }

        return true;
    }

    private bool PrepareFrontend()
    {
        if (_settings is not null)
        {
            return true;
        }

        PlayerInput playerInput = GetNode<PlayerInput>("PlayerInput");
        Engine.PhysicsTicksPerSecond = _configuration.TicksPerSecond;
        var settings = new PlayerSettingsController { Name = "PlayerSettings" };
        settings.Initialize(playerInput.Adapter, SettingsPath ?? ProjectSettings.GlobalizePath("user://player-settings.json"));
        AddChild(settings);
        playerInput.GameplayAvailable = () => !_quitRequested && (_arena is not null || _session?.Arena is not null);
        playerInput.FrameCaptured += OnFrameCaptured;
        _playerInput = playerInput;
        _settings = settings;
        return true;
    }

    private void PresentMainMenu()
    {
        _session?.SetFrontendPresentation(true, 0);
        _settingsPanel.SetFrontendPresentation(true, 0);
        _session?.FadeFrontendIn();
        _settingsPanel.FadeFrontendIn();
    }

    private void AbortApplicationInitialization()
    {
        if (_settingsPanel is not null)
        {
            _settings.RemoveChild(_settingsPanel);
            _settingsPanel.Free();
        }

        foreach (string name in new[] { "DevTools", "CombatHud", "ActivityFeed", "MatchStandings", "EosIdentity", "DevelopmentSession", "VehicleArena" })
        {
            Node? node = GetNodeOrNull<Node>(name);
            if (node is null)
            {
                continue;
            }

            RemoveChild(node);
            node.Free();
        }

        _arena = null;
        _session = null;
        _online = null;
        _settingsPanel = null!;
        GetTree().AutoAcceptQuit = true;
    }

    private void RequestQuit()
    {
        _quitRequested = true;
        _session?.Leave();
        if (_session is null)
        {
            _online?.Coordinator?.Leave();
        }
    }

    private void LeaveToMainMenu()
    {
        _session?.Leave();
        if (_arena is not null)
        {
            RemoveChild(_arena);
            _arena.QueueFree();
            _arena = null;
            _session = new DevelopmentSession { Name = "DevelopmentSession" };
            AddChild(_session);
        }
    }

    private void OnFrameCaptured(InputFrame input)
    {
        _arena?.Advance(input);
        _session?.Advance(input);
        _settingsPanel?.SetVehicleTelemetry(_session?.Arena?.LocalState?.Speed ?? _arena?.Player.Snapshot.Speed ?? 0, _session?.Diagnostics ?? default);
        _settingsPanel?.SetCombatHudVisible(_session?.Arena?.LocalState is not null || _arena is not null);
    }
}
