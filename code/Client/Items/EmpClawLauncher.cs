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
    private readonly Node3D[] _feedRods;
    private readonly EmpClawVisual[] _rounds;
    private readonly EmpClawDischarge _discharge = new();
    private EmpClawRackState? _state;
    private float _shotAge = 1;
    private float _feed;
    private int _feedIndex = -1;

    internal Node3D Muzzle { get; }
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
        _feedRods = Enumerable.Range(0, 3).Select(i => (Node3D)model.FindChild("FeedRod_" + i, true, false)).ToArray();
        _rounds = _sockets.Select((socket, index) =>
        {
            var round = new EmpClawVisual { Name = "Round_" + index, Rotation = new(0, MathF.PI, 0) };
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
        if (!shot) { return; }
        _shotAge = 0;
        _feed = 0;
        _feedIndex = Enumerable.Range(0, 3).FirstOrDefault(i => (state.RoundMask & (1 << i)) != 0, -1);
        _discharge.Fire(Muzzle.GlobalTransform);
        DischargeCount++;
    }

    internal void Present(float progress, WeaponAimSolution? aim, float delta)
    {
        _shotAge += delta;
        _discharge.Advance(delta);
        // Trunk/rack precedes a straight lift, short seating drop, then bore extension.
        float rise = Ease(progress, .44f, .62f);
        float seat = Ease(progress, .62f, .76f);
        _lift.Position = new(0, .45f * rise - .21f * seat, 0);
        _extension.Position = new(0, 0, -.68f * Ease(progress, .76f, 1));
        _middle.Position = new(0, 0, -.34f * Ease(progress, .76f, 1));
        _recoil.Position = new(0, 0, .065f * MathF.Sin(Math.Min(1, _shotAge / .20f) * MathF.PI));
        float blend = 1 - MathF.Exp(-22 * Math.Max(0, delta));
        float yaw = progress >= .999f ? aim?.Yaw ?? 0 : 0;
        float pitch = progress >= .999f ? aim?.Pitch ?? 0 : 0;
        _yaw.Rotation = new(0, Mathf.LerpAngle(_yaw.Rotation.Y, yaw, blend), 0);
        _pitch.Rotation = new(Mathf.LerpAngle(_pitch.Rotation.X, pitch, blend), 0, 0);
        IsReady = progress >= .999f && (aim?.Ready ?? false) && _shotAge >= .22f;
        _feed = Mathf.MoveToward(_feed, _feedIndex >= 0 && progress >= .999f ? 1 : 0, delta / .22f);
        for (int i = 0; i < 3; i++)
        {
            // Stored rounds stay beneath the fixed rack. Only the selected loading sled
            // transfers to the breech; the other magazine positions never inherit aim.
            Vector3 stored = new((i - 1) * .37f, -.125f, .28f);
            Transform3D breechPose = _yaw.Transform * _lift.Transform * _pitch.Transform;
            Vector3 breech = breechPose * new Vector3(0, 0, .30f);
            float feed = i == _feedIndex ? Ease(_feed, 0, 1) : 0;
            // Lift behind the rack's rear crossmember before traversing to the gun.
            Vector3 withdrawn = new(stored.X, stored.Y, .64f);
            Vector3 clearance = new(stored.X, .55f, .64f);
            Vector3 position = feed < .25f ? stored.Lerp(withdrawn, Ease(feed, 0, .25f))
                : feed < .65f ? withdrawn.Lerp(clearance, Ease(feed, .25f, .65f))
                : clearance.Lerp(breech, Ease(feed, .65f, 1));
            Basis orientation = Basis.Identity.Slerp(breechPose.Basis * Basis.FromEuler(new(0, MathF.PI, 0)), feed);
            _sockets[i].Transform = new Transform3D(orientation, position);
            Vector3 anchor = new(stored.X, -.245f, stored.Z);
            Vector3 piston = _sockets[i].Position + Vector3.Down * .10f - anchor;
            var bearing = new Basis(new Quaternion(Vector3.Up, piston.Normalized()));
            _feedRods[i].Transform = new Transform3D(new Basis(bearing.X, bearing.Y * piston.Length(), bearing.Z), anchor);
            _rounds[i].Visible = _state is not null && (_state.RoundMask & (1 << i)) != 0;
        }
    }

    internal static float Ease(float value, float start, float end) => Mathf.SmoothStep(0, 1, Mathf.Clamp((value - start) / (end - start), 0, 1));
}
