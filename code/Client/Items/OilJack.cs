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
    private Node3D _jaw = null!;
    private Node3D _meter = null!;
    private MeshInstance3D[] _lips = [];
    private OilHose _supply = null!;
    private OilHose _headHose = null!;
    private Vector3 _previousPosition;
    private Vector3 _previousVelocity;
    private bool _sampled;
    private N.Vector3 _offset;
    private N.Vector3 _velocity;
    private float _open;
    private float _extension;
    private float _mouthOpen;
    private Vector3 _presentedOffset;
    private double _accumulator;
    internal float Extension => _extension;
    internal bool Nested => _extension <= .001f && _open <= .001f && _mouthOpen <= .001f;
    internal float MouthOpening => _mouthOpen;
    internal float MeterLevel { get; private set; } = 1;
    internal float HoseEndpointError => _headHose.End.DistanceTo(HeadHoseSocket);
    internal Vector3 ModelOffset => Vector3.Zero;
    internal Vector3 HeadOffset => _presentedOffset;
    internal Vector3 SpringBase => _spring.GlobalPosition;
    internal bool FullyNestedGeometry => _head.Position.Y + .66f * _head.Scale.Y < .68f && _spring.Position.Y < .11f;
    private Vector3 HeadHoseSocket => ToLocal(_head.ToGlobal(new Vector3(-.33f, .20f, -.015f)));
    internal Vector3 Mouth => _head.ToGlobal(new Vector3(0, .025f - .08f * _mouthOpen, .225f + .04f * _mouthOpen));
    internal void ReconstructPour() { _open = _extension = _mouthOpen = 1; }

    public override void _Ready()
    {
        var model = Networking.MatchResourceLoader.LoadResource<PackedScene>(AssetPath).Instantiate<Node3D>();
        AddChild(model);
        _lid = (Node3D)model.FindChild("LidPivot", true, false);
        _head = (Node3D)model.FindChild("JesterPivot", true, false);
        _spring = (Node3D)model.FindChild("Spring", true, false);
        _jaw = (Node3D)model.FindChild("JawPivot", true, false);
        _meter = (Node3D)model.FindChild("GaugeLiquidLevel", true, false);
        _lips = model.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Where(m => m.GetBlendShapeCount() > 0).ToArray();
        _supply = new OilHose(); _headHose = new OilHose(); AddChild(_supply); AddChild(_headHose);
        _supply.Present(new(-.38f, -.10f, .06f), new(-.46f, .03f, .16f), new(-.77f, .12f, .13f), new(-.50f, .38f, .10f));
        Present(false, 0, null, new(), 0);
    }

    internal void Present(bool expose, float delta, OilPour? accepted, ItemConfiguration configuration, double tick)
    {
        bool firing = accepted is { Cancelled: false } && tick < accepted.StartTick + (ulong)accepted.DurationTicks;
        // Absolute accepted ticks reconstruct opening/closing on observers and resume.
        float phase = accepted is null ? 0 : (float)(tick - accepted.StartTick);
        // A reconstructed presentation of the accepted burst clock, never an
        // ammunition resource. A new payload starts full; a spent one stays empty.
        if (accepted is not null)
        {
            MeterLevel = accepted.Cancelled ? 1 - Mathf.Clamp(accepted.Emitted / MathF.Ceiling(accepted.DurationTicks / accepted.IntervalTicks), 0, 1)
                : 1 - Mathf.Clamp(phase / accepted.DurationTicks, 0, 1);
        }
        _meter.Visible = MeterLevel > .001f;
        _meter.Scale = new Vector3(1, Math.Max(.001f, MeterLevel), 1);
        float target = firing ? Mathf.Clamp(phase / 5, 0, 1) : accepted is { Cancelled: false }
            ? Mathf.Clamp(1 - (phase - accepted.DurationTicks) / 12, 0, 1) : 0;
        _mouthOpen = Mathf.MoveToward(_mouthOpen, target, delta * 12);
        bool hold = expose || _mouthOpen > .001f;
        _open = Mathf.MoveToward(_open, hold || _extension > .01f ? 1 : 0, delta * 4);
        _extension = Mathf.MoveToward(_extension, hold && _open > .9f ? 1 : 0, delta * 3);
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
        if (firing && accepted is not null) { _offset = accepted.HeadOffset; _velocity = accepted.HeadVelocity; }
        _presentedOffset = _presentedOffset.Lerp(VehicleBody.ToGodot(_offset), 1 - MathF.Exp(-delta * 24));
        Vector3 offset = _presentedOffset * _extension;
        _lid.Rotation = new Vector3(-_open * 2.35f, 0, 0);
        _head.Visible = _spring.Visible = _extension > .01f;
        _head.Position = new Vector3(0, .29f + 1.13f * _extension, 0) + offset;
        _head.Scale = Vector3.One * Mathf.Lerp(.48f, 1, _extension);
        _head.Rotation = new Vector3(offset.Z * .8f, 0, -offset.X * .8f);
        _jaw.Position = new Vector3(0, -.125f * _mouthOpen, .01f * _mouthOpen);
        foreach (var lip in _lips) { lip.SetBlendShapeValue(0, _mouthOpen); }
        Vector3 collar = _head.Position + _head.Basis * new Vector3(0, -.17f, 0) - _spring.Position;
        _spring.Quaternion = collar.LengthSquared() > .0001f ? new Quaternion(Vector3.Up, collar.Normalized()) : Quaternion.Identity;
        _spring.Scale = new Vector3(1, Math.Max(.02f, collar.Length() / 1.145f), 1);
        _headHose.Visible = _extension > .01f;
        Vector3 end = HeadHoseSocket;
        Vector3 start = new(-.29f, .635f, .16f);
        // The slack route stays beside the coil and forward of the painted face.
        // It follows pose only: no force, constraint or gameplay input is created.
        _headHose.Present(start, start + new Vector3(-.10f * _extension, .42f * _extension, .02f),
            end + new Vector3(-.23f * _extension, -.08f, 0), end);
    }
}
