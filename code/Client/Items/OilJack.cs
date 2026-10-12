using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using N = System.Numerics;

namespace Trackstorm.Client.Items;

/// <summary>Blender box/lid/head with bounded procedural inertia; no physics collision or item authority.</summary>
internal sealed partial class OilJack : Node3D
{
    internal const string AssetPath = "res://assets/items/oil/JackInTheBox.glb";
    private Node3D _lid = null!;
    private Node3D _head = null!;
    private Node3D _spring = null!;
    private Vector3 _previousPosition;
    private Vector3 _previousVelocity;
    private bool _sampled;
    private N.Vector3 _offset;
    private N.Vector3 _velocity;
    private float _open;
    private float _extension;
    private double _accumulator;
    internal float Extension => _extension;
    internal bool Nested => _extension <= .001f && _open <= .001f;
    internal void ReconstructPour() { _open = _extension = 1; }

    public override void _Ready()
    {
        var model = Networking.MatchResourceLoader.LoadResource<PackedScene>(AssetPath).Instantiate<Node3D>();
        AddChild(model);
        _lid = (Node3D)model.FindChild("LidPivot", true, false);
        _head = (Node3D)model.FindChild("JesterPivot", true, false);
        _spring = (Node3D)model.FindChild("Spring", true, false);
        Present(false, 0, null, new());
    }

    internal void Present(bool expose, float delta, OilPour? accepted, ItemConfiguration configuration)
    {
        _open = Mathf.MoveToward(_open, expose ? 1 : _extension > .01f ? 1 : 0, delta * 4);
        _extension = Mathf.MoveToward(_extension, expose && _open > .9f ? 1 : 0, delta * 3);
        if (delta > 0)
        {
            Vector3 position = GlobalPosition;
            Vector3 speed = _sampled ? (position - _previousPosition) / delta : Vector3.Zero;
            Vector3 acceleration = _sampled ? GlobalBasis.Inverse() * ((speed - _previousVelocity) / delta) : Vector3.Zero;
            _previousPosition = position; _previousVelocity = speed; _sampled = true;
            _accumulator += Math.Min(delta, .1f);
            while (_accumulator >= 1.0 / 60)
            {
                (_offset, _velocity) = OilPour.Spring(_offset, _velocity, VehicleBody.ToCore(acceleration), configuration.OilSpringStiffness, configuration.OilSpringDamping, configuration.OilSpringReaction);
                _accumulator -= 1.0 / 60;
            }
        }
        Vector3 offset = VehicleBody.ToGodot(accepted?.HeadOffset ?? _offset) * _extension;
        _lid.Rotation = new Vector3(-_open * 1.85f, 0, 0);
        _head.Visible = _spring.Visible = _extension > .01f;
        _head.Position = new Vector3(0, .70f + .72f * _extension, 0) + offset;
        _head.Rotation = new Vector3(offset.Z * .8f, 0, -offset.X * .8f);
        Vector3 collar = _head.Position + _head.Basis * new Vector3(0, -.17f, 0) - _spring.Position;
        _spring.Quaternion = collar.LengthSquared() > .0001f ? new Quaternion(Vector3.Up, collar.Normalized()) : Quaternion.Identity;
        _spring.Scale = new Vector3(1, Math.Max(.02f, collar.Length() / .62f), 1);
    }
}
