using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Client.Networking;
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

    /// <summary>
    /// Gets the latest authoritative tick observed from Core.
    /// </summary>
    public ulong CurrentSimulationTick => _session?.Arena?.Driver.Latest?.Tick ?? _arena?.Simulation.State.Tick ?? 0;

    /// <inheritdoc />
    public override void _Ready()
    {
        _playerInput = GetNode<PlayerInput>("PlayerInput");
        _playerInput.FrameCaptured += OnFrameCaptured;
        Engine.PhysicsTicksPerSecond = _configuration.TicksPerSecond;
        var settings = new PlayerSettingsController { Name = "PlayerSettings" };
        settings.Initialize(_playerInput.Adapter, ProjectSettings.GlobalizePath("user://player-settings.json"));
        AddChild(settings);
        var panel = new SettingsPanel { Name = "SettingsPanel" };
        panel.Initialize(settings, _playerInput.Adapter);
        settings.AddChild(panel);
        _settingsPanel = panel;
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
            _session = new DevelopmentSession { Name = "DevelopmentSession" };
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
    public override void _ExitTree()
    {
        if (_playerInput is not null)
        {
            _playerInput.FrameCaptured -= OnFrameCaptured;
        }
    }

    private void OnFrameCaptured(InputFrame input)
    {
        _arena?.Advance(input);
        _session?.Advance(input);
        int? ping = _session?.Ping;
        _settingsPanel.SetVehicleTelemetry(_session?.Arena?.LocalState?.Speed ?? _arena?.Player.Snapshot.Speed ?? 0, ping);
    }
}
