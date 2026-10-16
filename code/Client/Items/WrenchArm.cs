using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Vehicle-local telescopic rig and accepted three-contact repair presentation.</summary>
internal sealed partial class WrenchArm : Node3D
{
    internal const string AssetPath = "res://assets/items/wrench/RepairArm.glb";
    private static readonly Vector3 ShoulderPoint = new(0, .23f, 0);
    private static readonly Vector3 Stored = new(-.56f, .23f, 0);
    private static readonly Vector3 Raised = new(0, 1.60f, -.65f);
    private static readonly Vector3 Rest = new(0, .85f, -3.595f);
    private Node3D _services = null!;
    private Node3D _shoulder = null!;
    private Node3D _elbow = null!;
    private Node3D _slide = null!;
    private Node3D _reach = null!;
    private Node3D _wrist = null!;
    private float _deployment;
    private float _repairTime = -1;
    private WrenchRepairSettings _repairSettings = new();
    private WrenchSparks _sparks = null!;
    private Node3D _chuck = null!;
    private int _lastBurst = -1;
    private int _repairContact = -1;
    private float _contact;
    private float _lateral;
    private float _forward;
    private int? _requestedContact;
    private Vector3 _tip;

    internal bool ServicesVisible => _services.Visible;

    // Existing Oil/Nitro presentation policy: full-size underslung hardware is
    // revealed only after the original rack clears the bay. No second lift.
    internal void PresentServices(float rackHeight) => _services.Visible = rackHeight >= .75f;

    internal bool Nested => _deployment == 0;
    internal bool IsReady => _deployment == 1 && !Repairing && _contact == 0 && _lateral == 0 && _forward == 0;
    internal bool Repairing => _repairTime >= 0;
    internal bool SparksActive => _sparks.Active;
    internal float SparkAge => _sparks.Age;
    internal float Deployment => _deployment;
    internal Vector3 Tip => _tip;
    internal int ContactIndex => Repairing ? _repairContact : _contact >= .999f && _requestedContact is int index &&
        Math.Abs(_lateral - HoodContact(index).X) < .001f ? index : -1;
    internal float JointError { get; private set; }
    internal double AnimationSeconds { get; private set; }

    // Bonnet slopes from y=.40 at z=-.94 to y=.27 at z=-2.12.
    // z=-1.98 is 14 cm behind the bonnet's front edge, within the rigid arm's reach.
    // Coordinates are rack-local; the approved deployment/rest target stays unchanged.
    internal static Vector3 HoodContact(int index)
    {
        if (index is < 0 or > 2) { throw new ArgumentOutOfRangeException(nameof(index)); }
        return new Vector3((index - 1) * .50f, .40f - (1.04f / 1.18f * .13f) - 1.34f, -3.825f);
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
        _services = (Node3D)model.FindChild("Services", true, false);
        _services.Visible = false;
        _shoulder = (Node3D)model.FindChild("Shoulder", true, false);
        _elbow = (Node3D)model.FindChild("Elbow", true, false);
        _slide = (Node3D)model.FindChild("Slide", true, false);
        _reach = (Node3D)model.FindChild("Reach", true, false);
        _wrist = (Node3D)model.FindChild("Wrist", true, false);
        _chuck = (Node3D)model.FindChild("Chuck", true, false);
        _sparks = new WrenchSparks();
        AddChild(_sparks);
        Pose(Stored, 0, 0);
    }

    internal void BeginRepair()
    {
        if (!IsReady) { return; }
        _repairSettings = WrenchRepairSettings.Current;
        _repairTime = 0;
        _lastBurst = _repairContact = -1;
        _requestedContact = null;
    }

    /// <summary>Lift away from the hood, telescope above the roof, then fold before shared rack descent.</summary>
    internal void Present(bool deploy, float delta)
    {
        delta = Math.Max(0, delta);
        AnimationSeconds += delta;
        _sparks.Advance(delta);
        if (Repairing && deploy) { PresentRepair(delta); return; }
        if (Repairing)
        {
            _repairTime = -1;
            _repairContact = -1;
            _sparks.Clear();
        }
        float wantedX = deploy && _deployment == 1 && _requestedContact is int index ? HoodContact(index).X : 0;
        float wantedForward = deploy && _deployment == 1 && _requestedContact is not null ? HoodContact(1).Z - Rest.Z : 0;
        bool aligned = Math.Abs(_lateral - wantedX) < .001f && Math.Abs(_forward - wantedForward) < .001f;
        bool touching = deploy && _deployment == 1 && _requestedContact is not null && aligned;
        _contact = Mathf.MoveToward(_contact, touching ? 1 : 0, delta / .6f);
        if (_contact == 0)
        {
            _lateral = Mathf.MoveToward(_lateral, wantedX, delta);
            _forward = Mathf.MoveToward(_forward, wantedForward, delta);
        }
        if (_contact == 0 && _lateral == 0 && _forward == 0)
            _deployment = Mathf.MoveToward(_deployment, deploy ? 1 : 0, delta / Core.Items.ItemSlot.WrenchDeploymentSeconds);
        float unfold = Ease(_deployment / .45f);
        float extension = Ease((_deployment - .45f) / .55f);
        Vector3 target = _deployment <= .45f
            ? Stored.Lerp(Raised, unfold) + Vector3.Forward * (.60f * MathF.Sin(Mathf.Pi * unfold))
            : Raised.Lerp(Rest, extension);
        target.X += _lateral;
        target.Z += _forward;
        target.Y = Mathf.Lerp(target.Y, HoodContact(1).Y + .498f, Ease(_contact));
        Pose(target, unfold, extension);
    }

    private void PresentRepair(float delta)
    {
        _repairTime = Math.Min(_repairSettings.Duration, _repairTime + delta);
        float beat = _repairTime / _repairSettings.Duration * 3;
        int index = Math.Min(2, (int)beat);
        float phase = beat - index;
        Vector3 contact = HoodContact(index);
        float hover = contact.Y + .498f + .20f;
        float previousX = index == 0 ? 0 : HoodContact(index - 1).X;
        float startY = index == 0 ? Rest.Y : hover;
        float holdEnd = .42f + _repairSettings.ContactFraction;
        float x = Mathf.Lerp(previousX, contact.X, Ease((phase - .04f) / .14f));
        float z = index == 0 ? Mathf.Lerp(Rest.Z, contact.Z, Ease((phase - .04f) / .14f)) : contact.Z;
        float y;
        if (phase < .18f) { y = startY + (index == 0 ? .08f * Ease(phase / .04f) : 0); }
        else if (phase < .42f) { y = Mathf.Lerp(index == 0 ? Rest.Y + .08f : hover, contact.Y + .498f, Ease((phase - .18f) / .24f)); }
        else if (phase < holdEnd) { y = contact.Y + .498f; }
        else { y = Mathf.Lerp(contact.Y + .498f, index == 2 ? Rest.Y : hover, Ease((phase - holdEnd) / (1 - holdEnd))); }
        _repairContact = phase >= .42f && phase < holdEnd ? index : -1;
        // A vibration rises off contact by only 1 mm; never drills through the bonnet.
        if (_repairContact >= 0) { y += .001f * MathF.Abs(MathF.Sin(_repairTime * 180)); _chuck.RotateY(delta * 85); }
        Pose(new Vector3(x, y, z), 1, 1);
        if (_repairContact >= 0 && _lastBurst != index)
        {
            _lastBurst = index;
            _sparks.Burst(_tip, _repairSettings.SparkIntensity);
        }
        _lateral = x;
        _forward = z - Rest.Z;
        // Preserve the existing lift-before-center return when interrupted at any contact.
        float fraction = Mathf.Clamp((Rest.Y - y) / (Rest.Y - contact.Y - .498f), 0, 1);
        _contact = .5f - MathF.Sin(MathF.Asin(1 - 2 * fraction) / 3);
        if (_repairTime >= _repairSettings.Duration)
        {
            _repairTime = -1;
            _repairContact = -1;
            _contact = 0;
            _sparks.Clear();
        }
    }

    private void Pose(Vector3 target, float unfolded, float extension)
    {
        // The front-edge contact needs another 10 cm of travel in each existing
        // telescopic section to keep the elbow/forearm above the roof lamps.
        // Blend it only beyond the approved rest reach; deploy/stow are unchanged.
        float reach = extension + .10f * Mathf.Clamp((Rest.Z - target.Z) / .23f, 0, 1);
        float lowerLength = 1.25f + 2 * reach;
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
        _slide.Position = _reach.Position = new Vector3(0, .05f + reach, 0);
        Basis tool = new(Vector3.Right, -Mathf.Pi / 2 * (1 - unfolded));
        _wrist.Transform = new Transform3D(lower.Inverse() * tool, new Vector3(0, 1.15f, 0));
        Vector3 actualWrist = ToLocal(_wrist.GlobalPosition);
        JointError = actualWrist.DistanceTo(target);
        _tip = actualWrist + tool * new Vector3(0, -.498f, 0);
    }

    private static float Ease(float value) => Mathf.SmoothStep(0, 1, Mathf.Clamp(value, 0, 1));
}
