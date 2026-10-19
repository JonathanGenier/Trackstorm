using Godot;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>Rigid Blender cannon with a fixed rack saddle, full-yaw bearing and independent
/// pitch/recoil/extension. Accepted round bits retain their original socket identities.</summary>
internal sealed partial class EmpClawLauncher : Node3D
{
    internal const string AssetPath = "res://assets/items/emp-claw/EmpClawLauncher.glb";
    private readonly Node3D _yaw;
    private readonly Node3D _lift;
    private readonly Node3D _pitch;
    private readonly Node3D _extension;
    private readonly Node3D _middle;
    private readonly Node3D _recoil;
    private readonly Node3D[] _sockets;
    private readonly Node3D _breech;
    private readonly Node3D _upper;
    private readonly Node3D _lower;
    private readonly Node3D _upperSlide;
    private readonly Node3D _lowerSlide;
    private readonly Node3D _gripper;
    private readonly Node3D[] _fingers;
    private readonly EmpClawVisual[] _rounds;
    private readonly EmpClawDischarge _discharge = new();
    private EmpClawRackState? _state;
    private float _shotAge = 1;
    private float _reloadAge = 1;
    private float _reloadDuration = 50f / 60;
    private int _feedIndex = -1;

    internal Node3D Muzzle { get; }
    internal bool Reloading => _feedIndex >= 0 && _reloadAge < _reloadDuration;
    internal float ReloadPhase => Math.Min(1, _reloadAge / _reloadDuration);
    internal bool BreechOpen => Math.Abs(_breech.Rotation.Y) > .01f;
    internal int StoredRounds => _rounds.Where((round, i) => round.Visible && (i > ChamberIndex || (i == ChamberIndex && Reloading && ReloadPhase <= .20f))).Count();
    internal int LoadedRounds => ChamberIndex < 3 && (!Reloading || ReloadPhase >= .74f) ? 1 : 0;
    internal int ChamberIndex => _state is null ? 0 : Enumerable.Range(0, 3).FirstOrDefault(i => (_state.RoundMask & (1 << i)) != 0, 3);
    internal IReadOnlyList<Node3D> RoundSockets => _sockets;
    internal IReadOnlyList<Node3D> NoseAnchors => _rounds.Select(round => round.NoseAnchor).ToArray();
    internal int VisibleRounds => _rounds.Count(round => round.Visible);
    internal int DischargeCount { get; private set; }
    internal bool Discharging => _discharge.Active;
    internal bool Centered => Math.Abs(Mathf.AngleDifference(_yaw.Rotation.Y, 0)) < .008f && Math.Abs(_pitch.Rotation.X) < .008f;
    internal bool IsReady { get; private set; }
    internal Vector2 Angles => new(_yaw.Rotation.Y, _pitch.Rotation.X);

    internal EmpClawLauncher()
    {
        Name = "EmpClawLauncher";
        var model = Networking.MatchResourceLoader.LoadResource<PackedScene>(AssetPath).Instantiate<Node3D>();
        AddChild(model);
        _yaw = (Node3D)model.FindChild("TurretYaw", true, false);
        _lift = (Node3D)model.FindChild("CannonLift", true, false);
        _pitch = (Node3D)model.FindChild("CannonPitch", true, false);
        _extension = (Node3D)model.FindChild("BarrelExtension", true, false);
        _middle = (Node3D)model.FindChild("BarrelMiddle", true, false);
        _recoil = (Node3D)model.FindChild("BarrelRecoil", true, false);
        Muzzle = (Node3D)model.FindChild("Muzzle", true, false);
        _sockets = Enumerable.Range(0, 3).Select(i => (Node3D)model.FindChild("RoundSocket_" + i, true, false)).ToArray();
        _breech = (Node3D)model.FindChild("BreechHinge", true, false);
        _upper = (Node3D)model.FindChild("LoaderUpper", true, false);
        _lower = (Node3D)model.FindChild("LoaderLower", true, false);
        _upperSlide = (Node3D)model.FindChild("LoaderUpperSlide", true, false);
        _lowerSlide = (Node3D)model.FindChild("LoaderLowerSlide", true, false);
        _gripper = (Node3D)model.FindChild("LoaderGripper", true, false);
        _fingers = new[] { -1, 1 }.Select(i => (Node3D)model.FindChild("LoaderFinger_" + i, true, false)).ToArray();
        _rounds = _sockets.Select((socket, index) =>
        {
            var round = new EmpClawVisual { Name = "Round_" + index };
            socket.AddChild(round);
            return round;
        }).ToArray();
        AddChild(_discharge);
    }

    internal void Observe(EmpClawRackState state, bool restore = false)
    {
        state.Validate();
        if (_state is not null && (state.Life != _state.Life || state.Token != _state.Token))
        { throw new ArgumentException("A launcher instance belongs to exactly one accepted capability."); }
        if (_state is not null && state.Revision < _state.Revision) { return; }
        bool shot = !restore && _state is not null && state.ShotSequence > _state.ShotSequence && state.FiredRound >= 0;
        _state = state;
        for (int i = 0; i < 3; i++) { _rounds[i].Visible = (state.RoundMask & (1 << i)) != 0; }
        if (restore && state.ReloadRemaining > 0)
        {
            _reloadDuration = state.ReloadDuration;
            _reloadAge = 0; // A reconstructed mount completes deployment before a visible reload.
            _feedIndex = ChamberIndex;
        }
        if (!shot) { return; }
        _shotAge = 0;
        _reloadAge = 0;
        _reloadDuration = state.ReloadDuration;
        _feedIndex = Enumerable.Range(0, 3).FirstOrDefault(i => (state.RoundMask & (1 << i)) != 0, -1);
        _discharge.Fire(Muzzle.GlobalTransform);
        DischargeCount++;
    }

    internal void Present(float progress, WeaponAimSolution? aim, float delta)
    {
        _shotAge += delta;
        if (progress >= .999f) { _reloadAge += Math.Max(0, delta); }
        _discharge.Advance(delta);
        // Trunk/rack precedes a straight lift, short seating drop, then bore extension.
        float rise = Ease(progress, .44f, .62f);
        float seat = Ease(progress, .62f, .76f);
        _lift.Position = new(0, .62f * rise - .15f * seat, 0);
        _extension.Position = new(0, 0, -.99f * Ease(progress, .76f, 1));
        _middle.Position = new(0, 0, -.50f * Ease(progress, .76f, 1));
        _recoil.Position = new(0, 0, .065f * MathF.Sin(Math.Min(1, _shotAge / .20f) * MathF.PI));
        float blend = 1 - MathF.Exp(-60 * Math.Max(0, delta));
        bool loadingAxis = Reloading && ReloadPhase < .94f;
        float yaw = progress >= .999f && !loadingAxis ? aim?.Yaw ?? 0 : 0;
        float pitch = progress >= .999f && !loadingAxis ? aim?.Pitch ?? 0 : 0;
        _yaw.Rotation = new(0, Mathf.LerpAngle(_yaw.Rotation.Y, yaw, blend), 0);
        _pitch.Rotation = new(Mathf.LerpAngle(_pitch.Rotation.X, pitch, blend), 0, 0);
        PresentLoader();
        IsReady = progress >= .999f && (aim?.Ready ?? false) && _shotAge >= .22f &&
            !Reloading && !BreechOpen && ChamberIndex < 3 &&
            Math.Abs(Mathf.AngleDifference(_yaw.Rotation.Y, yaw)) < .015f && Math.Abs(_pitch.Rotation.X - pitch) < .015f;
    }

    private void PresentLoader()
    {
        float t = ReloadPhase;
        Transform3D chamber = _yaw.Transform * _lift.Transform * _pitch.Transform * _recoil.Transform;
        Vector3 rest = new(0, -.68f, .50f);
        Vector3 reserve = new(_feedIndex == 1 ? -.34f : .34f, -.27f, .40f);
        Vector3 withdrawn = new(reserve.X, -.27f, 1.00f);
        Vector3 raised = new(reserve.X, .47f, 1.00f);
        Vector3 aligned = new(0, .47f, 1.00f);
        Vector3 seated = new(0, .47f, .12f);
        Vector3 carried = t < .34f ? reserve.Lerp(withdrawn, Ease(t, .20f, .34f))
            : t < .47f ? withdrawn.Lerp(raised, Ease(t, .34f, .47f))
            : t < .58f ? raised.Lerp(aligned, Ease(t, .50f, .58f))
            : aligned.Lerp(seated, Ease(t, .58f, .74f));
        Basis facing = Basis.FromEuler(new(0, MathF.PI * (1 - Ease(t, .34f, .47f)), 0));
        for (int i = 0; i < 3; i++)
        {
            bool chambered = i == ChamberIndex;
            Transform3D pose = chambered && (!Reloading || t >= .74f)
                ? chamber * new Transform3D(Basis.Identity, new(0, 0, .12f))
                : chambered && i == _feedIndex ? new(facing, carried)
                : new(Basis.FromEuler(new(0, MathF.PI, 0)), new(i == 1 ? -.34f : .34f, -.27f, .40f));
            _sockets[i].Transform = pose;
            _rounds[i].Visible = _state is not null && (_state.RoundMask & (1 << i)) != 0;
        }
        _breech.Rotation = new(0, -2.65f * Ease(t, .40f, .50f) * (1 - Ease(t, .87f, .94f)), 0);
        Vector3 gripOffset = facing * new Vector3(0, 0, .32f);
        Vector3 grip = carried + gripOffset;
        Vector3 approach = new(reserve.X, rest.Y, reserve.Z - .32f);
        Vector3 wrist = !Reloading ? rest : t < .08f ? rest.Lerp(approach, Ease(t, 0, .08f))
            : t < .20f ? approach.Lerp(reserve + gripOffset, Ease(t, .08f, .12f))
            : t < .80f ? grip
            : t < .87f ? grip.Lerp(new(0, .47f, .90f), Ease(t, .80f, .87f))
            : t < .95f ? new Vector3(0, .47f, .90f).Lerp(new(0, rest.Y, .90f), Ease(t, .87f, .95f))
            : new Vector3(0, rest.Y, .90f).Lerp(rest, Ease(t, .95f, 1));
        _gripper.Transform = new(Reloading ? Basis.Identity.Slerp(facing, Ease(t, 0, .08f)) : Basis.Identity, wrist);
        float opening = Reloading && t >= .12f && t < .80f
            ? .24f + .05f * (1 - Ease(t, .12f, .20f) + Ease(t, .74f, .80f)) : .29f;
        for (int i = 0; i < 2; i++) { _fingers[i].Position = new((i == 0 ? -1 : 1) * opening, 0, 0); }
        // Compact rigid sleeves extend through translated rails, as on the Wrench arm.
        // The elbow stays below retrieval, then behind the reserves during the lift.
        Vector3 anchor = new(0, -.68f, .38f);
        Vector3 line = wrist - anchor;
        float distance = Math.Max(line.Length(), .001f);
        float length = Math.Max(.42f, distance * .52f);
        Vector3 direction = line.Normalized();
        Vector3 preferred = Vector3.Down.Lerp(Vector3.Back, Ease(t, .20f, .34f));
        float unfold = Reloading ? Ease(t, 0, .08f) * (1 - Ease(t, .95f, 1)) : 0;
        preferred = Vector3.Right.Lerp(preferred, unfold);
        Vector3 bend = (preferred - direction * direction.Dot(preferred)).Normalized();
        Vector3 elbow = anchor + direction * (distance / 2) + bend * MathF.Sqrt(length * length - distance * distance / 4);
        _upperSlide.Position = _lowerSlide.Position = new(0, length - .42f, 0);
        SetLink(_upper, anchor, elbow);
        SetLink(_lower, elbow, wrist);
    }

    private static void SetLink(Node3D link, Vector3 start, Vector3 end) =>
        link.Transform = new(new Basis(new Quaternion(Vector3.Up, (end - start).Normalized())), start);

    internal static float Ease(float value, float start, float end) => Mathf.SmoothStep(0, 1, Mathf.Clamp((value - start) / (end - start), 0, 1));
}
