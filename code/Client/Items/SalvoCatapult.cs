using Godot;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>Blender-authored rigid catapult. Accepted inventory drives visible rounds;
/// this mechanism never schedules shots, authors projectiles or uses world gravity.</summary>
internal sealed partial class SalvoCatapult : Node3D
{
    internal const string AssetPath = "res://assets/items/salvo/SalvoCatapult.glb";
    private readonly Node3D _slide;
    private readonly Node3D _yaw;
    private readonly Node3D _hinge;
    private readonly Node3D _middle;
    private readonly Node3D _tip;
    private readonly Node3D _support;
    private readonly Node3D _spring;
    private readonly Node3D _springTop;
    private readonly Node3D _springFoot;
    private readonly Node3D _cable;
    private readonly Node3D _cableReturn;
    private readonly Node3D _eye;
    private readonly Node3D _swivel;
    private readonly Node3D _exit;
    private readonly Node3D _drum;
    private readonly Node3D _services;
    private readonly Node3D _gate;
    private readonly Node3D[] _rounds;
    private readonly Vector3[] _stored;
    private float _shotAge = 10;
    private int _remaining = 5;
    private float _yawAngle;

    internal Node3D Bowl { get; }
    internal float Yaw => _yawAngle;
    internal float ArmDegrees => Mathf.RadToDeg(-_hinge.Rotation.X);
    internal int VisibleRounds => _rounds.Count(round => round.Visible);
    internal bool Centered => Math.Abs(Mathf.AngleDifference(_yawAngle, 0)) < .005f;
    internal bool Cycling => _shotAge < .48f;
    internal bool Deployed { get; private set; }
    internal bool IsReady { get; private set; }
    internal int ReleaseCount { get; private set; }
    internal float SpringLength { get; private set; }
    internal IReadOnlyList<Node3D> Rounds => _rounds;

    internal SalvoCatapult()
    {
        Name = "SalvoCatapult";
        var model = Networking.MatchResourceLoader.LoadResource<PackedScene>(AssetPath).Instantiate<Node3D>();
        AddChild(model);
        Node3D Joint(string name) => (Node3D)model.FindChild(name, true, false);
        _slide = Joint("TurretSlide"); _yaw = Joint("TurretYaw"); _hinge = Joint("SpoonHinge");
        _middle = Joint("ArmMiddle"); _tip = Joint("ArmTip"); _support = Joint("SupportExtension");
        _spring = Joint("Spring"); _springTop = Joint("SpringTop"); _springFoot = Joint("SpringFoot");
        _cable = Joint("Cable"); _eye = Joint("CableEye"); _swivel = Joint("CableSwivel"); _exit = Joint("WinchExit");
        _drum = Joint("WinchDrum"); _services = Joint("WinchHousing"); _gate = Joint("FeedGate");
        Bowl = Joint("BowlSocket");
        _rounds = Enumerable.Range(0, 5).Select(i => Joint("Round_" + i)).ToArray();
        _stored = _rounds.Select(round => round.Position).ToArray();
        _cableReturn = (Node3D)_cable.Duplicate(); _cableReturn.Name = "CableThroughSwivel";
        model.AddChild(_cableReturn);
    }

    /// <summary>Reconstruction sets the current count without replay; only an observed
    /// decrease within the installed selection produces a release.</summary>
    internal void ObserveRounds(int remaining, bool release = false, float elapsed = 0)
    {
        remaining = Math.Clamp(remaining, 0, 5);
        if (release && remaining < _remaining)
        {
            _shotAge = Math.Clamp(elapsed, 0, .48f);
            ReleaseCount++;
        }
        _remaining = remaining;
    }

    /// <summary>Progress is local mechanical deployment after the shared rack is raised.
    /// TS-211 can consume Bowl and supply accepted releases through ObserveRounds.</summary>
    internal void Present(float progress, bool returning, WeaponAimSolution? aim, float delta, float rackProgress)
    {
        _shotAge += delta;
        float extension = Ease(progress, .0f, .52f);
        float armExtension = Ease(progress, .25f, .68f);
        float cock = Ease(progress, .65f, .84f);
        float feed = Ease(progress, .84f, 1);
        float requestedYaw = !returning && progress >= .84f ? aim?.Yaw ?? _yawAngle : 0;
        _yawAngle += Math.Clamp(Mathf.AngleDifference(_yawAngle, requestedYaw), -delta * 6, delta * 6);
        _yaw.Rotation = new(0, _yawAngle, 0);
        _slide.Position = new(0, Mathf.Lerp(.12f, .40f, extension), Mathf.Lerp(-.65f, -1.72f, extension));
        _middle.Position = new(0, 0, .68f * armExtension);
        _tip.Position = new(0, 0, .64f * armExtension);
        _support.Position = new(0, 0, .74f * armExtension);
        // Arm rests rearward. Positive physical elevation lifts +Z around -X.
        // Rapid release stops at 45 degrees; a damped buffer recoil never exceeds it.
        float degrees = Mathf.Lerp(0, 24, armExtension) * (1 - cock) - 5 * cock;
        if (Cycling && progress >= .84f)
        {
            float up = Ease(_shotAge, 0, .075f);
            float reset = Ease(_shotAge, .17f, .34f);
            float recoil = _shotAge > .075f && _shotAge < .17f ? 3 * MathF.Sin((_shotAge - .075f) / .095f * MathF.PI) : 0;
            degrees = Mathf.Lerp(-5 + 50 * up - recoil, -5, reset);
            feed = Ease(_shotAge, .34f, .48f);
        }
        _hinge.Rotation = new(-Mathf.DegToRad(degrees), 0, 0);
        _services.Visible = rackProgress > .72f;
        _drum.Rotation = new(Mathf.DegToRad(degrees * 9), 0, 0);
        _gate.Position = new(0, -.07f * MathF.Sin(feed * MathF.PI), 0);
        // A captive escapement moves the nearest remaining round along the handle
        // into the bowl. Five minus the accepted count never reappears during reset.
        int loaded = _remaining - 1;
        for (int i = 0; i < _rounds.Length; i++)
        {
            var round = _rounds[i];
            round.Visible = i < _remaining;
            round.Position = _stored[i];
            round.Rotation = Vector3.Zero;
            if (i == loaded)
            {
                Vector3 end = round.GetParent<Node3D>().ToLocal(Bowl.GlobalPosition);
                round.Position = _stored[i].Lerp(end, feed);
                round.Rotation = new(feed * 7, 0, 0);
            }
        }
        // Spring and its foot are both children of the yaw bearing. The fixed
        // winch routes through the bearing's axial fairlead, avoiding cable wrap.
        SpringLength = _springFoot.GlobalPosition.DistanceTo(_springTop.GlobalPosition);
        Stretch(_spring, _springFoot.GlobalPosition, _springTop.GlobalPosition);
        Stretch(_cable, _swivel.GlobalPosition, _eye.GlobalPosition);
        Stretch(_cableReturn, _exit.GlobalPosition, _swivel.GlobalPosition);
        _cable.Visible = extension > .1f;
        _cableReturn.Visible = _services.Visible && extension > .1f;
        Deployed = progress >= .999f && !returning;
        IsReady = Deployed && !Cycling && _remaining > 0;
    }

    private static float Ease(float value, float start, float end) => Mathf.SmoothStep(0, 1, Mathf.Clamp((value - start) / (end - start), 0, 1));

    private static void Stretch(Node3D node, Vector3 from, Vector3 to)
    {
        var parent = node.GetParent<Node3D>();
        Vector3 a = parent.ToLocal(from), b = parent.ToLocal(to), d = b - a;
        float length = Math.Max(.001f, d.Length());
        Vector3 up = d / length;
        Vector3 right = (Math.Abs(up.Dot(Vector3.Right)) > .95f ? Vector3.Forward : Vector3.Right).Cross(up).Normalized();
        node.Transform = new(new Basis(right, up * length, right.Cross(up)), a);
    }
}
