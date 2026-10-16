using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Vehicle-local telescopic rig. Hood poses are independent controls for the later activation sequence.</summary>
internal sealed partial class WrenchArm : Node3D
{
    internal const string AssetPath = "res://assets/items/wrench/RepairArm.glb";
    private static readonly Vector3 ShoulderPoint = new(0, .23f, 0);
    private static readonly Vector3 Stored = new(-.56f, .23f, 0);
    private static readonly Vector3 Raised = new(0, 1.60f, -.65f);
    private static readonly Vector3 Rest = new(0, .85f, -3.595f);
    private Node3D _shoulder = null!;
    private Node3D _elbow = null!;
    private Node3D _slide = null!;
    private Node3D _reach = null!;
    private Node3D _wrist = null!;
    private float _deployment;
    private float _cueTime;
    private float _contact;
    private float _lateral;
    private int? _requestedContact;
    private Vector3 _tip;

    internal bool Nested => _deployment == 0;
    internal bool IsReady => _deployment == 1 && !Repairing;
    internal bool Repairing => _cueTime > 0;
    internal float Deployment => _deployment;
    internal Vector3 Tip => _tip;
    internal int ContactIndex => _contact >= .999f && _requestedContact is int index &&
        Math.Abs(_lateral - HoodContact(index).X) < .001f ? index : -1;
    internal float JointError { get; private set; }
    internal double AnimationSeconds { get; private set; }

    // Bonnet slopes from y=.40 at z=-.94 to y=.27 at z=-2.12.
    // z=-1.75 clears the scoop for all three positions. Coordinates are rack-local.
    internal static Vector3 HoodContact(int index)
    {
        if (index is < 0 or > 2) { throw new ArgumentOutOfRangeException(nameof(index)); }
        return new Vector3((index - 1) * .50f, .40f - (.81f / 1.18f * .13f) - 1.34f, -3.595f);
    }

    /// <summary>Requests one pose, without repair, consumption, effects or an automatic three-contact sequence.</summary>
    internal void RequestHoodPose(int? index)
    {
        if (index is < 0 or > 2) { throw new ArgumentOutOfRangeException(nameof(index)); }
        _requestedContact = index;
    }

    public override void _Ready()
    {
        Name = "RackItem_Wrench";
        var model = Networking.MatchResourceLoader.LoadResource<PackedScene>(AssetPath).Instantiate<Node3D>();
        AddChild(model);
        _shoulder = (Node3D)model.FindChild("Shoulder", true, false);
        _elbow = (Node3D)model.FindChild("Elbow", true, false);
        _slide = (Node3D)model.FindChild("Slide", true, false);
        _reach = (Node3D)model.FindChild("Reach", true, false);
        _wrist = (Node3D)model.FindChild("Wrist", true, false);
        Pose(Stored, 0, 0);
    }

    // Retain a short accepted-use cue until TS-293 supplies the actual hood sequence.
    internal void BeginRepair() => _cueTime = .32f;

    /// <summary>Lift away from the hood, telescope above the roof, then fold before shared rack descent.</summary>
    internal void Present(bool deploy, float delta)
    {
        delta = Math.Max(0, delta);
        AnimationSeconds += delta;
        _cueTime = Math.Max(0, _cueTime - delta);
        float wantedX = deploy && _deployment == 1 && _requestedContact is int index ? HoodContact(index).X : 0;
        bool aligned = Math.Abs(_lateral - wantedX) < .001f;
        bool touching = deploy && _deployment == 1 && _requestedContact is not null && aligned;
        _contact = Mathf.MoveToward(_contact, touching ? 1 : 0, delta / .6f);
        if (_contact == 0) { _lateral = Mathf.MoveToward(_lateral, wantedX, delta); }
        if (_contact == 0 && _lateral == 0)
            _deployment = Mathf.MoveToward(_deployment, deploy ? 1 : 0, delta / 1.8f);
        float unfold = Ease(_deployment / .45f);
        float extension = Ease((_deployment - .45f) / .55f);
        Vector3 target = _deployment <= .45f
            ? Stored.Lerp(Raised, unfold) + Vector3.Forward * (.60f * MathF.Sin(Mathf.Pi * unfold))
            : Raised.Lerp(Rest, extension);
        target.X += _lateral;
        target.Y = Mathf.Lerp(target.Y, HoodContact(1).Y + .498f, Ease(_contact));
        Pose(target, unfold, extension);
    }

    private void Pose(Vector3 target, float unfolded, float extension)
    {
        float lowerLength = 1.25f + 2 * extension;
        Vector3 offset = target - ShoulderPoint;
        float distance = Mathf.Clamp(offset.Length(), lowerLength - .6999f, lowerLength + .6999f);
        Vector3 direction = offset.Normalized();
        float along = (.49f - lowerLength * lowerLength + distance * distance) / (2 * distance);
        Vector3 pole = Vector3.Back.Lerp(Vector3.Up, unfolded).Normalized();
        Vector3 bend = (pole - direction * pole.Dot(direction)).Normalized();
        Vector3 elbow = ShoulderPoint + direction * along + bend * MathF.Sqrt(Math.Max(0, .49f - along * along));
        Vector3 hinge = direction.Cross(bend).Normalized();
        Vector3 upperAxis = (elbow - ShoulderPoint).Normalized();
        Vector3 lowerAxis = (target - elbow).Normalized();
        Basis upper = new(hinge, upperAxis, hinge.Cross(upperAxis));
        Basis lower = new(hinge, lowerAxis, hinge.Cross(lowerAxis));
        _shoulder.Transform = new Transform3D(upper, ShoulderPoint);
        _elbow.Transform = new Transform3D(upper.Inverse() * lower, new Vector3(0, .7f, 0));
        _slide.Position = _reach.Position = new Vector3(0, .05f + extension, 0);
        Basis tool = new(Vector3.Right, -Mathf.Pi / 2 * (1 - unfolded));
        _wrist.Transform = new Transform3D(lower.Inverse() * tool, new Vector3(0, 1.15f, 0));
        Vector3 actualWrist = ToLocal(_wrist.GlobalPosition);
        JointError = actualWrist.DistanceTo(target);
        _tip = actualWrist + tool * new Vector3(0, -.498f, 0);
    }

    private static float Ease(float value) => Mathf.SmoothStep(0, 1, Mathf.Clamp(value, 0, 1));
}
