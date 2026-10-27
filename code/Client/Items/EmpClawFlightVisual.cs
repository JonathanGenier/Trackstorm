using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Interpolates accepted flight only; no local targeting, terrain, collision or lifetime decisions.</summary>
internal sealed partial class EmpClawFlightVisual : Node3D
{
    private readonly EmpClawVisual _claw = new();
    private Vector3 _sample;
    private Vector3 _velocity;
    private float _age;
    private bool _observed;

    public override void _Ready()
    {
        AddChild(_claw);
        _claw.Observe(EmpClawVisualState.Fired);
    }

    internal void Observe(Vector3 position, Vector3 velocity)
    {
        _sample = position;
        _velocity = velocity;
        _age = 0;
        if (!_observed || Position.DistanceSquaredTo(position) > 400)
        {
            Position = position;
            Quaternion = new Quaternion(Vector3.Forward, velocity.Normalized());
            _observed = true;
        }
    }

    public override void _Process(double delta)
    {
        float elapsed = Math.Max(0, (float)delta);
        _age = Math.Min(.1f, _age + elapsed);
        Position = Position.Lerp(_sample + _velocity * _age, 1 - MathF.Exp(-45 * elapsed));
        Quaternion = Quaternion.Slerp(new Quaternion(Vector3.Forward, _velocity.Normalized()), 1 - MathF.Exp(-30 * elapsed));
    }
}
