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
    private VehicleArena _arena = null!;
    private SettingsPanel _settingsPanel = null!;
    private NetworkTransportNode? _network;

    /// <summary>
    /// Gets the latest authoritative tick observed from Core.
    /// </summary>
    public ulong CurrentSimulationTick => _arena.Simulation.State.Tick;

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
        _arena = new VehicleArena { Name = "VehicleArena" };
        AddChild(_arena);
        string[] networkArguments = OS.GetCmdlineUserArgs().Where(argument => argument.StartsWith("--transport-host=", StringComparison.Ordinal) || argument.StartsWith("--transport-connect=", StringComparison.Ordinal)).ToArray();
        if (networkArguments.Length > 1)
        {
            throw new ArgumentException("Specify exactly one transport host or connect endpoint.");
        }

        if (networkArguments.Length == 1)
        {
            _network = new NetworkTransportNode { Name = "NetworkTransport" };
            AddChild(_network);
            string argument = networkArguments[0];
            string endpoint = argument[(argument.IndexOf('=') + 1)..];
            if (argument.StartsWith("--transport-host=", StringComparison.Ordinal))
            {
                _network.Gateway.Listen(endpoint);
            }
            else
            {
                _network.Gateway.Connect(endpoint);
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
        _arena.Advance(input);
        int? ping = _network?.Gateway.Connections.Keys.Select(peer => _network.Gateway.GetStatistics(peer).PingMilliseconds).FirstOrDefault();
        _settingsPanel.SetVehicleTelemetry(_arena.Player.Snapshot.Speed, ping);
    }
}
