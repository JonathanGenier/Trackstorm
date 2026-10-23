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
    internal int StoredRounds => VisibleRounds;
    internal int LoadedRounds => 0;
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
        Vector3 rest = new(0, -.68f, .50f);
        for (int i = 0; i < 3; i++)
        {
            // TS-299 exposes all three original sockets behind the cannon. They remain
            // fixed hit regions rather than hiding one live round inside the barrel.
            _sockets[i].Transform = new(Basis.FromEuler(new(0, MathF.PI, 0)),
                Vehicles.VehicleBody.ToGodot(EmpClawGeometry.StoredRoot(i)));
            _rounds[i].Visible = _state is not null && (_state.RoundMask & (1 << i)) != 0;
        }
        _breech.Rotation = Vector3.Zero;
        Vector3 wrist = rest;
        _gripper.Transform = new(Basis.Identity, wrist);
        for (int i = 0; i < 2; i++) { _fingers[i].Position = new((i == 0 ? -1 : 1) * .29f, 0, 0); }
        // Compact rigid sleeves extend through translated rails, as on the Wrench arm.
        // The elbow stays below retrieval, then behind the reserves during the lift.
        Vector3 anchor = new(0, -.68f, .38f);
        Vector3 line = wrist - anchor;
        float distance = Math.Max(line.Length(), .001f);
        float length = Math.Max(.42f, distance * .52f);
        Vector3 direction = line.Normalized();
        Vector3 preferred = Vector3.Down;
        float unfold = 0;
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
