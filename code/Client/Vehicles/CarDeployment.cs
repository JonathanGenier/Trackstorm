using Godot;

namespace Trackstorm.Client.Vehicles;

/// <summary>Reversible presentation-only rear deployment; future weapons attach beneath WeaponRack.</summary>
internal sealed partial class CarDeployment : Node
{
    private Node3D _left = null!;
    private Node3D _right = null!;
    private Node3D _rack = null!;
    private Node3D[] _pistons = [];
    private float _progress;

    /// <summary>Requested presentation pose. This adds no gameplay action or replicated state.</summary>
    internal bool Deployed { get; set; }

    /// <summary>Current mechanical path position, zero closed and one raised.</summary>
    internal float Progress => _progress;

    /// <summary>Discards old-life animation memory at an authoritative lifecycle boundary.</summary>
    internal void ResetPose()
    {
        Deployed = false;
        _progress = 0;
        ApplyPose(0);
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        Node3D model = GetParent<Node3D>();
        _left = model.GetNode<Node3D>("TrunkHinge_L");
        _right = model.GetNode<Node3D>("TrunkHinge_R");
        _rack = model.GetNode<Node3D>("WeaponRack");
        _pistons = new[] { "LiftPiston_-1", "LiftPiston_1" }.Select(name => _rack.GetNode<Node3D>(name)).ToArray();
        ApplyPose(0);
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        _progress = Mathf.MoveToward(_progress, Deployed ? 1 : 0, (float)delta / 1.6f);
        ApplyPose(_progress);
    }

    /// <summary>Applies the same ordered open-then-lift path in either direction, including mid-cycle reversals.</summary>
    internal void ApplyPose(float progress)
    {
        float lid = Mathf.SmoothStep(0, 1, Mathf.Clamp(progress / 0.45f, 0, 1));
        float lift = Mathf.SmoothStep(0, 1, Mathf.Clamp((progress - 0.45f) / 0.55f, 0, 1));
        _left.Rotation = new Vector3(0, 0, 1.70f * lid);
        _right.Rotation = new Vector3(0, 0, -1.70f * lid);
        _rack.Position = new Vector3(0, -0.08f + (0.90f * lift), 1.47f);
        foreach (Node3D piston in _pistons)
        {
            float extension = 0.21f + (0.90f * lift);
            piston.Scale = new Vector3(1, extension / 0.21f, 1);
            piston.Position = new Vector3(piston.Position.X, -0.065f - (0.45f * lift), 0);
        }
    }
}
