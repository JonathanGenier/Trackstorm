using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

/// <summary>Reconstructs existing tire mesh travel from observed suspension; never feeds presentation into physics.</summary>
internal sealed partial class WheelPresentation : Node
{
    private MeshInstance3D[] _wheels = [];
    private bool _initialized;

    internal Func<(VehicleState State, VehicleConfiguration Configuration)?> Source { get; init; } = null!;

    public override void _Ready()
    {
        string[] names = ["wheel-front-left", "wheel-front-right", "wheel-back-left", "wheel-back-right"];
        _wheels = names.Select(name => GetParent().GetNode<MeshInstance3D>(name)).ToArray();
    }

    public override void _Process(double delta)
    {
        if (Source() is not { } sample) { return; }
        var compression = sample.State.Wheels.Compression;
        float[] values = [compression.X, compression.Y, compression.Z, compression.W];
        float blend = _initialized ? 1 - MathF.Exp(-30 * (float)delta) : 1;
        for (int index = 0; index < _wheels.Length; index++)
        {
            MeshInstance3D wheel = _wheels[index];
            float target = -sample.Configuration.SuspensionLength + values[index] - wheel.GetAabb().Position.Y;
            wheel.Position = new Vector3(wheel.Position.X, Mathf.Lerp(wheel.Position.Y, target, blend), wheel.Position.Z);
        }

        _initialized = true;
    }
}
