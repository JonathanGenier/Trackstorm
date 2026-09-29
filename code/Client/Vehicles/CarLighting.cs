using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

/// <summary>Per-instance lamps driven by the same accepted movement used for local and remote art.</summary>
internal sealed partial class CarLighting : Node3D
{
    private readonly List<StandardMaterial3D> _owned = [];
    private readonly List<StandardMaterial3D> _brakes = [];
    private readonly List<StandardMaterial3D> _reverse = [];
    private readonly List<Light3D> _reverseBeams = [];
    private readonly List<MeshInstance3D> _lenses = [];
    internal Func<VehicleState?>? Source { get; init; }
    internal bool Braking { get; private set; }
    internal bool Reversing { get; private set; }

    public override void _Ready()
    {
        Node3D model = GetParent<Node3D>();
        foreach (MeshInstance3D lens in Descendants(model).OfType<MeshInstance3D>())
        {
            string name = lens.Name;
            bool head = name.StartsWith("Headlight_", StringComparison.Ordinal);
            bool roof = name.StartsWith("RoofAuxLight_", StringComparison.Ordinal);
            bool tail = name.StartsWith("TailRunning_", StringComparison.Ordinal);
            bool brake = name.StartsWith("Brake_", StringComparison.Ordinal);
            bool reverse = name.StartsWith("Reverse_", StringComparison.Ordinal);
            if (!(head || roof || tail || brake || reverse)) { continue; }
            Color color = tail || brake ? new Color(1, .025f, .008f) : new Color(1, .88f, .66f);
            if (reverse) { color = new Color(.85f, .93f, 1); }
            var material = new StandardMaterial3D
            {
                AlbedoColor = color * (brake || reverse ? .16f : .65f),
                Roughness = .22f, Metallic = .05f, EmissionEnabled = true,
                Emission = color, EmissionEnergyMultiplier = brake || reverse ? 0 : tail ? 1.2f : 3.5f,
            };
            lens.MaterialOverride = material;
            _lenses.Add(lens);
            _owned.Add(material);
            if (brake) { _brakes.Add(material); }
            if (reverse) { _reverse.Add(material); }
            if (head || roof || reverse)
            {
                // Beams live in model space, independent of Blender lens mesh orientation/scale.
                Aabb bounds = model.GlobalTransform.AffineInverse() * lens.GlobalTransform * lens.GetAabb();
                var beam = new SpotLight3D
                {
                    Name = "Beam_" + name, Position = bounds.GetCenter() + new Vector3(0, 0, reverse ? .04f : -.04f),
                    RotationDegrees = new Vector3(reverse ? -12 : -8, reverse ? 180 : 0, 0),
                    LightColor = color, LightEnergy = reverse ? 1.3f : roof ? 1.8f : 3.0f,
                    SpotRange = reverse ? 7 : roof ? 22 : 26, SpotAngle = roof ? 25 : 38,
                    SpotAttenuation = .8f, ShadowEnabled = false, Visible = !reverse,
                    DistanceFadeEnabled = true, DistanceFadeBegin = 65, DistanceFadeLength = 20,
                };
                AddChild(beam);
                if (reverse) { _reverseBeams.Add(beam); }
            }
        }
    }

    public override void _Process(double delta)
    {
        VehicleState? sample = Source?.Invoke();
        float speed = sample is { } state
            ? System.Numerics.Vector3.Dot(state.Physics.LinearVelocity,
                System.Numerics.Vector3.Transform(-System.Numerics.Vector3.UnitZ, state.Physics.Orientation)) : 0;
        // LongitudinalAcceleration contains tire drive/braking, excluding passive drag and gravity.
        // This also handles acceleration input braking a reversing vehicle, without transmitting inputs.
        Braking = sample is { } movement && (movement.Handbrake > .08f ||
            (Math.Abs(speed) > .2f && movement.LongitudinalAcceleration * Math.Sign(speed) < -.15f));
        Reversing = speed < -.2f;
        foreach (StandardMaterial3D material in _brakes) { material.EmissionEnergyMultiplier = Braking ? 5 : 0; }
        foreach (StandardMaterial3D material in _reverse) { material.EmissionEnergyMultiplier = Reversing ? 3 : 0; }
        foreach (Light3D beam in _reverseBeams) { beam.Visible = Reversing; }
    }

    public override void _ExitTree()
    {
        foreach (MeshInstance3D lens in _lenses)
        {
            if (GodotObject.IsInstanceValid(lens)) { lens.MaterialOverride = null; }
        }
        foreach (StandardMaterial3D material in _owned) { material.Dispose(); }
        _lenses.Clear();
        _owned.Clear();
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            yield return child;
            foreach (Node nested in Descendants(child)) { yield return nested; }
        }
    }
}
