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
    private EosIdentityNode? _online;
    private bool _quitRequested;

    /// <summary>
    /// Gets the latest authoritative tick observed from Core.
    /// </summary>
    public ulong CurrentSimulationTick => _session?.Arena?.Driver.Latest?.Tick ?? _arena?.Simulation.State.Tick ?? 0;

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

        _playerInput = GetNode<PlayerInput>("PlayerInput");
        _playerInput.GameplayAvailable = () => !_quitRequested && (_arena is not null || _session?.Arena is not null);
        _playerInput.FrameCaptured += OnFrameCaptured;
        Engine.PhysicsTicksPerSecond = _configuration.TicksPerSecond;
        var settings = new PlayerSettingsController { Name = "PlayerSettings" };
        settings.Initialize(_playerInput.Adapter, SettingsPath ?? ProjectSettings.GlobalizePath("user://player-settings.json"));
        AddChild(settings);
        var panel = new SettingsPanel { Name = "SettingsPanel" };
        panel.Initialize(settings, _playerInput.Adapter);
        settings.AddChild(panel);
        _settingsPanel = panel;
        var devTools = new Development.DevToolsShell { Name = "DevTools" };
        devTools.Initialize(_playerInput.Adapter);
        devTools.Configs.Session = () => _session;
        devTools.Configs.Practice = () => _arena;
        devTools.Stats.Capture = selected => Statistics.RuntimeStatistics.Capture(_session, _arena, selected, _online?.DeveloperDiagnostics ?? "EOS unavailable.");
        devTools.Logs.Source = () => _arena?.Simulation.Events ?? _session?.Events;
        panel.DiagnosticOverlayOpen = () => devTools.IsOpen;
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
            Units = () => settings.Current.SpeedUnit,
            Position = () => _session?.Standings.Position ?? "--",
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
            AddChild(_session);
            if (networkArguments.Length == 1)
            {
                string argument = networkArguments[0];
                string endpoint = argument[(argument.IndexOf('=') + 1)..];
                string name = OS.GetCmdlineUserArgs().FirstOrDefault(value => value.StartsWith("--player-name=", StringComparison.Ordinal))?[14..] ?? "Player";
                _session.Open(argument.StartsWith("--transport-host=", StringComparison.Ordinal), endpoint, name);
            }
        }
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
        _settingsPanel.SetVehicleTelemetry(_session?.Arena?.LocalState?.Speed ?? _arena?.Player.Snapshot.Speed ?? 0, _session?.Diagnostics ?? default);
        _settingsPanel.SetCombatHudVisible(_session?.Arena?.LocalState is not null || _arena is not null);
    }
}
