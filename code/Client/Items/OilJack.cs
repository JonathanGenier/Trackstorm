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
    private float _slide;
    private Vector3 _presentedOffset;
    private Node3D _model = null!;
    private readonly StandardMaterial3D _railMaterial = new() { AlbedoColor = new Color(.20f, .13f, .045f), Metallic = .8f, Roughness = .28f };
    private readonly List<(MeshInstance3D Node, BoxMesh Mesh)> _rails = new();
    private double _accumulator;
    internal float Extension => _extension;
    internal bool Nested => _extension <= .001f && _open <= .001f && _slide <= .001f;
    internal float RearSlide => _slide;
    internal Vector3 HeadOffset => _presentedOffset;
    internal Vector3 SpringBase => _spring.GlobalPosition;
    internal bool FullyNestedGeometry => _head.Position.Y + .66f * _head.Scale.Y < .68f && _spring.Position.Y < .11f;
    internal Vector3 Mouth => _head.ToGlobal(new Vector3(0, .025f, .225f));
    internal void ReconstructPour() { _open = _extension = 1; _slide = OilPour.RearTravel; }

    public override void _Ready()
    {
        var model = Networking.MatchResourceLoader.LoadResource<PackedScene>(AssetPath).Instantiate<Node3D>(); _model = model;
        AddChild(model);
        _lid = (Node3D)model.FindChild("LidPivot", true, false);
        _head = (Node3D)model.FindChild("JesterPivot", true, false);
        _spring = (Node3D)model.FindChild("Spring", true, false);
        foreach (float x in new[] { -.32f, .32f })
        {
            var mesh = new BoxMesh { Size = new Vector3(.07f, .045f, OilPour.RearTravel + .60f) };
            var rail = new MeshInstance3D { Mesh = mesh, MaterialOverride = _railMaterial, Position = new Vector3(x, -.025f, 0) };
            AddChild(rail); _rails.Add((rail, mesh));
        }
        Present(false, 0, null, new());
    }

    internal void Present(bool expose, float delta, OilPour? accepted, ItemConfiguration configuration)
    {
        _slide = Mathf.MoveToward(_slide, expose || _extension > .001f || _open > .001f ? OilPour.RearTravel : 0, delta * 3.5f);
        _model.Position = new Vector3(0, 0, _slide);
        foreach (var rail in _rails)
        {
            rail.Node.Position = new Vector3(rail.Node.Position.X, -.025f, _slide * .5f);
            rail.Node.Scale = new Vector3(1, 1, (_slide + .60f) / (OilPour.RearTravel + .60f));
        }
        _open = Mathf.MoveToward(_open, expose && _slide >= OilPour.RearTravel ? 1 : _extension > .01f ? 1 : 0, delta * 4);
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
        // Blend accepted 10 Hz spring samples without letting cosmetic sway alter aim.
        _presentedOffset = _presentedOffset.Lerp(VehicleBody.ToGodot(accepted?.HeadOffset ?? _offset), 1 - MathF.Exp(-delta * 24));
        Vector3 offset = _presentedOffset * _extension;
        _lid.Rotation = new Vector3(-_open * 1.85f, 0, 0);
        _head.Visible = _spring.Visible = _extension > .01f;
        _head.Position = new Vector3(0, .29f + 1.13f * _extension, 0) + offset;
        _head.Scale = Vector3.One * Mathf.Lerp(.48f, 1, _extension);
        _head.Rotation = new Vector3(offset.Z * .8f, 0, -offset.X * .8f);
        Vector3 collar = _head.Position + _head.Basis * new Vector3(0, -.17f, 0) - _spring.Position;
        _spring.Quaternion = collar.LengthSquared() > .0001f ? new Quaternion(Vector3.Up, collar.Normalized()) : Quaternion.Identity;
        _spring.Scale = new Vector3(1, Math.Max(.02f, collar.Length() / 1.145f), 1);
    }

    public override void _ExitTree() { foreach (var rail in _rails) { rail.Mesh.Dispose(); } _railMaterial.Dispose(); }
}
