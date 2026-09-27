using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

/// <summary>Reversible presentation-only rear deployment; future weapons attach beneath WeaponRack.</summary>
internal sealed partial class CarDeployment : Node
{
    private Node3D _left = null!;
    private Node3D _right = null!;
    private Node3D _rack = null!;
    private Node3D[] _pistons = [];
    private Node3D[] _lowerStages = [];
    private Node3D[] _upperStages = [];
    private float _progress;
    private static readonly VehicleConfiguration Defaults = new();

    internal Func<VehicleConfiguration?>? Configuration { get; init; }

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
        _lowerStages = new[] { "LiftStage1_-1", "LiftStage1_1" }.Select(name => _rack.GetNode<Node3D>(name)).ToArray();
        _upperStages = new[] { "LiftStage2_-1", "LiftStage2_1" }.Select(name => _rack.GetNode<Node3D>(name)).ToArray();
        ApplyPose(0);
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        VehicleConfiguration tuning = Configuration?.Invoke() ?? Defaults;
        float remaining = (float)delta;
        // Consume time up to the phase boundary first, then carry excess time into
        // the other phase. Retuning and reversal preserve the current mechanical pose.
        bool trunk = Deployed ? _progress < 0.45f : _progress <= 0.45f;
        float boundary = Deployed ? (trunk ? 0.45f : 1) : (trunk ? 0 : 0.45f);
        float rate = (trunk ? tuning.TrunkDeploymentSpeed : tuning.RackDeploymentSpeed) / 1.6f;
        float step = Math.Min(remaining, Math.Abs(boundary - _progress) / rate);
        _progress = Mathf.MoveToward(_progress, boundary, step * rate);
        remaining -= step;
        if (remaining > 0)
        {
            rate = (trunk ? tuning.RackDeploymentSpeed : tuning.TrunkDeploymentSpeed) / 1.6f;
            _progress = Mathf.MoveToward(_progress, Deployed ? 1 : 0, remaining * rate);
        }
        ApplyPose(_progress);
    }

    /// <summary>Applies the same ordered open-then-lift path in either direction, including mid-cycle reversals.</summary>
    internal void ApplyPose(float progress)
    {
        float lid = Mathf.SmoothStep(0, 1, Mathf.Clamp(progress / 0.45f, 0, 1));
        float lift = Mathf.SmoothStep(0, 1, Mathf.Clamp((progress - 0.45f) / 0.55f, 0, 1));
        _left.Rotation = new Vector3(0, 0, 1.70f * lid);
        _right.Rotation = new Vector3(0, 0, -1.70f * lid);
        _rack.Position = new Vector3(0, -0.08f + (1.42f * lift), 1.47f);
        foreach (Node3D piston in _pistons)
        {
            float extension = 0.21f + (1.42f * lift);
            piston.Scale = new Vector3(1, extension / 0.21f, 1);
            piston.Position = new Vector3(piston.Position.X, -0.065f - (0.71f * lift), 0);
        }
        foreach (Node3D stage in _lowerStages)
        {
            stage.Scale = new Vector3(1, (.21f + (.47f * lift)) / .21f, 1);
            stage.Position = new Vector3(stage.Position.X, -.065f - (1.185f * lift), 0);
        }
        foreach (Node3D stage in _upperStages)
        {
            stage.Scale = new Vector3(1, (.21f + (.47f * lift)) / .21f, 1);
            stage.Position = new Vector3(stage.Position.X, -.065f - (.715f * lift), 0);
        }
    }
}
