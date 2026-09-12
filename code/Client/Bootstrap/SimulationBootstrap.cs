using Godot;
using Trackstorm.Core.Simulation;

namespace Trackstorm.Client.Bootstrap;

/// <summary>
/// Composes the authoritative Core simulation with Godot timing and local input.
/// </summary>
public sealed partial class SimulationBootstrap : Node
{
    private readonly SimulationConfiguration _configuration =
        new(SimulationConfiguration.DefaultTicksPerSecond);

    private double _accumulatedSeconds;
    private Simulation _simulation = null!;

    /// <summary>
    /// Gets the latest authoritative tick observed from Core.
    /// </summary>
    public long CurrentSimulationTick => _simulation.State.Tick;

    /// <inheritdoc />
    public override void _Ready()
    {
        _simulation = new Simulation(_configuration);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        LogicalInput logicalInput = CaptureLogicalInput();
        double secondsPerTick = 1.0 / _configuration.TicksPerSecond;

        _accumulatedSeconds += delta;
        while (_accumulatedSeconds >= secondsPerTick)
        {
            _simulation.Step(logicalInput);
            _accumulatedSeconds -= secondsPerTick;
        }
    }

    private static LogicalInput CaptureLogicalInput()
    {
        return new LogicalInput(Input.IsActionPressed("ui_accept"));
    }
}
