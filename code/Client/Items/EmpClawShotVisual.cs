using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Short cosmetic discharge preview. It performs no queries, targeting, hits or gameplay mutation.</summary>
internal sealed partial class EmpClawShotVisual : Node3D
{
    internal required Vector3 Direction { get; init; }
    private float _age;
    public override void _Ready()
    {
        var claw = new EmpClawVisual();
        AddChild(claw);
        Basis = new Basis(new Quaternion(Vector3.Forward, Direction));
        claw.Observe(EmpClawVisualState.Fired);
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        Position += Direction * (float)delta * 65;
        if (_age >= .75f) { QueueFree(); }
    }
}
