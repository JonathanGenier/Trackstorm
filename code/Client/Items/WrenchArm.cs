using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Rigid Blender repair rig, entirely vehicle-local; accepted use only triggers a cosmetic cycle.</summary>
internal sealed partial class WrenchArm : Node3D
{
    internal const string AssetPath = "res://assets/items/wrench/RepairArm.glb";
    internal const float RepairSeconds = 2.7f;
    private static readonly Vector3 ShoulderPoint = new(0, .23f, 0);
    private static readonly Vector3 Stored = new(-.56f, .23f, 0);
    private static readonly Vector3 Rest = new(-.10f, .60f, -.85f);
    private Node3D _shoulder = null!;
    private Node3D _elbow = null!;
    private Node3D _wrist = null!;
    private Node3D _chuck = null!;
    private float _deployment;
    private float _repairTime;
    private bool _repair;
    private Vector3 _tip;
    private Vector3 _lastTarget = Stored;
    private Vector3 _cancelFrom;
    private float _cancelTime = -1;

    internal bool Nested => _deployment == 0;
    internal bool IsReady => _deployment == 1 && !_repair;
    internal bool Repairing => _repair;
    internal float Deployment => _deployment;
    internal Vector3 Tip => _tip;
    internal int ContactIndex { get; private set; } = -1;
    internal float JointError { get; private set; }
    internal double AnimationSeconds { get; private set; }

    public override void _Ready()
    {
        Name = "RackItem_Wrench";
        var model = Networking.MatchResourceLoader.LoadResource<PackedScene>(AssetPath).Instantiate<Node3D>();
        AddChild(model);
        _shoulder = (Node3D)model.FindChild("Shoulder", true, false);
        _elbow = (Node3D)model.FindChild("Elbow", true, false);
        _wrist = (Node3D)model.FindChild("Wrist", true, false);
        _chuck = (Node3D)model.FindChild("Chuck", true, false);
        Pose(Stored, 0);
    }

    internal void BeginRepair()
    {
        _repair = true;
        _repairTime = 0;
        _cancelTime = -1;
    }

    /// <summary>Return is completed before the shared rack is permitted to descend.</summary>
    internal void Present(bool deploy, float delta)
    {
        delta = Math.Max(0, delta);
        AnimationSeconds += delta;
        // A selection change cancels the cue gracefully through the raised return pose.
        if (!deploy && _repair && _cancelTime < 0)
        {
            _cancelFrom = _lastTarget;
            _cancelTime = 0;
        }
        if (!_repair) { _deployment = Mathf.MoveToward(_deployment, deploy ? 1 : 0, delta / .75f); }
        float amount = Ease(_deployment);
        // Arc around the shoulder's unreachable inner radius rather than cutting
        // through it on a straight line between folded and ready positions.
        Vector3 target = Stored.Lerp(Rest, amount) + Vector3.Forward * (.60f * MathF.Sin(Mathf.Pi * amount));
        ContactIndex = -1;
        float spin = 0;
        if (_cancelTime >= 0)
        {
            _cancelTime = Math.Min(.4f, _cancelTime + delta);
            target = _cancelFrom.Lerp(Rest, Ease(_cancelTime / .4f));
            if (_cancelTime >= .4f) { _repair = false; _cancelTime = -1; }
        }
        else if (_repair && _deployment >= 1)
        {
            _repairTime = Math.Min(RepairSeconds, _repairTime + delta);
            // Three deliberate lift/contact/lift strokes. No HP or timing authority here.
            float phase = Math.Min(_repairTime, 2.1f) / .7f;
            int index = Math.Min(2, (int)phase);
            float t = phase - index;
            Vector3 contact = new((index - 1) * .55f, .038f, -1.525f);
            Vector3 hover = contact + Vector3.Up * .22f;
            if (_repairTime < 2.1f)
            {
                Vector3 from = index == 0 ? Rest : new Vector3((index - 2) * .55f, .258f, -1.525f);
                target = t < .35f ? from.Lerp(hover, Ease(t / .35f)) :
                    t < .55f ? hover.Lerp(contact, Ease((t - .35f) / .20f)) :
                    t < .75f ? contact : contact.Lerp(hover, Ease((t - .75f) / .25f));
                if (t >= .55f && t < .75f)
                {
                    ContactIndex = index;
                    spin = _repairTime * 100;
                    target.Y += .0015f * (1 + MathF.Sin(_repairTime * 160));
                }
            }
            else { target = new Vector3(.55f, .258f, -1.525f).Lerp(Rest, Ease((_repairTime - 2.1f) / .6f)); }
            if (_repairTime >= RepairSeconds) { _repair = false; }
        }
        Pose(target, amount);
        _chuck.Rotation = new Vector3(0, spin, 0);
    }

    private void Pose(Vector3 target, float unfolded)
    {
        _lastTarget = target;
        Vector3 offset = target - ShoulderPoint;
        float distance = Mathf.Clamp(offset.Length(), .5501f, 1.949f);
        Vector3 direction = offset.Normalized();
        float along = (.7f * .7f - 1.25f * 1.25f + distance * distance) / (2 * distance);
        Vector3 pole = Vector3.Back.Lerp(Vector3.Up, unfolded).Normalized();
        Vector3 bend = (pole - direction * pole.Dot(direction)).Normalized();
        Vector3 elbow = ShoulderPoint + direction * along + bend * MathF.Sqrt(Math.Max(0, .49f - along * along));
        Vector3 hinge = direction.Cross(bend).Normalized();
        Vector3 upperAxis = (elbow - ShoulderPoint).Normalized();
        Vector3 lowerAxis = (target - elbow).Normalized();
        Basis upper = new(hinge, upperAxis, hinge.Cross(upperAxis));
        Basis lower = new(hinge, lowerAxis, hinge.Cross(lowerAxis));
        // Child transforms preserve real joint hierarchy and fixed link lengths.
        _shoulder.Transform = new Transform3D(upper, ShoulderPoint);
        _elbow.Transform = new Transform3D(upper.Inverse() * lower, new Vector3(0, .7f, 0));
        Basis tool = new Basis(Vector3.Right, -Mathf.Pi / 2 * (1 - unfolded));
        _wrist.Transform = new Transform3D(lower.Inverse() * tool, new Vector3(0, 1.25f, 0));
        Vector3 actualWrist = ToLocal(_wrist.GlobalPosition);
        JointError = actualWrist.DistanceTo(target);
        _tip = actualWrist + tool * new Vector3(0, -.398f, 0);
    }

    private static float Ease(float value) => Mathf.SmoothStep(0, 1, Mathf.Clamp(value, 0, 1));
}
