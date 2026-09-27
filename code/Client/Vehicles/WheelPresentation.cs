using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

/// <summary>Animates the Blender Car rig from accepted movement; never feeds presentation into physics.</summary>
internal sealed partial class WheelPresentation : Node
{
    internal const float TireRadius = 0.582f;
    private Node3D[] _wheels = [];
    private Node3D[] _spins = [];
    private readonly Node3D[,] _links = new Node3D[4, 3];
    private readonly Vector3[,] _anchors = new Vector3[4, 3];
    private Node3D[] _rods = [];
    private Node3D _model = null!;
    private bool _initialized;
    private float _spin;

    internal Func<(VehicleState State, VehicleConfiguration Configuration)?> Source { get; init; } = null!;

    public override void _Ready()
    {
        _model = GetParent<Node3D>();
        string[] names = ["FL", "FR", "RL", "RR"];
        _wheels = names.Select(name => _model.GetNode<Node3D>("WheelCarrier_" + name)).ToArray();
        _spins = names.Select((name, index) => _wheels[index].GetNode<Node3D>("WheelSpin_" + name)).ToArray();
        _rods = names.Select(name => _model.GetNode<Node3D>("ShockRod_" + name)).ToArray();
        string[] suffixes = ["A", "B", "Upper"];
        for (int index = 0; index < 4; index++)
        {
            for (int part = 0; part < 3; part++)
            {
                _anchors[index, part] = _model.GetNode<Node3D>($"SuspensionAnchor_{names[index]}_{suffixes[part]}").Position;
                _links[index, part] = _model.GetNode<Node3D>($"SuspensionLink_{names[index]}_{suffixes[part]}");
            }
        }
    }

    public override void _Process(double delta)
    {
        if (Source() is not { } sample) { return; }
        var compression = sample.State.Wheels.Compression;
        ReadOnlySpan<float> values = [compression.X, compression.Y, compression.Z, compression.W];
        float blend = _initialized ? 1 - MathF.Exp(-30 * (float)delta) : 1;
        var forward = System.Numerics.Vector3.Transform(-System.Numerics.Vector3.UnitZ, sample.State.Physics.Orientation);
        float speed = System.Numerics.Vector3.Dot(sample.State.Physics.LinearVelocity, forward);
        _spin = Mathf.PosMod(_spin - (speed * (float)delta / TireRadius), Mathf.Tau);
        for (int index = 0; index < _wheels.Length; index++)
        {
            Node3D wheel = _wheels[index];
            float target = -sample.Configuration.SuspensionLength + values[index] + TireRadius;
            wheel.Position = new Vector3(wheel.Position.X, Mathf.Lerp(wheel.Position.Y, target, blend), wheel.Position.Z);
            wheel.Rotation = new Vector3(0, index < 2 ? -sample.State.SteeringAngle : 0, 0);
            _spins[index].Rotation = new Vector3(_spin, 0, 0);
            for (int part = 0; part < 3; part++)
            {
                Vector3 anchor = _anchors[index, part];
                Vector3 hub = wheel.Position + new Vector3(index % 2 == 0 ? 0.12f : -0.12f, -0.04f, 0);
                Node3D link = _links[index, part];
                if (part == 2)
                {
                    Vector3 middle = anchor.Lerp(hub, 0.57f);
                    Align(link, anchor, middle);
                    Align(_rods[index], middle, hub);
                }
                else { Align(link, anchor, hub); }
            }
        }

        _initialized = true;
    }

    // Blender +Z cylinders export along Godot +Y with a one-metre centred mesh.
    private static void Align(Node3D link, Vector3 start, Vector3 end)
    {
        Vector3 direction = end - start;
        link.Transform = new Transform3D(new Basis(new Quaternion(Vector3.Up, direction.Normalized())).Scaled(new Vector3(1, direction.Length(), 1)), (start + end) / 2);
    }
}
