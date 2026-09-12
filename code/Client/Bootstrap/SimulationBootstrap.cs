using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Client.Settings;
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
    private Simulation _simulation = null!;

    /// <summary>
    /// Gets the latest authoritative tick observed from Core.
    /// </summary>
    public ulong CurrentSimulationTick => _simulation.State.Tick;

    /// <inheritdoc />
    public override void _Ready()
    {
        _simulation = new Simulation(_configuration);
        _playerInput = GetNode<PlayerInput>("PlayerInput");
        _playerInput.FrameCaptured += OnFrameCaptured;
        Engine.PhysicsTicksPerSecond = _configuration.TicksPerSecond;
        var settings = new PlayerSettingsController { Name = "PlayerSettings" };
        settings.Initialize(_playerInput.Adapter, ProjectSettings.GlobalizePath("user://player-settings.json"));
        AddChild(settings);
        var panel = new SettingsPanel { Name = "SettingsPanel" };
        panel.Initialize(settings, _playerInput.Adapter);
        settings.AddChild(panel);
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
        _simulation.Step(input);
    }
}
