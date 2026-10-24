using Godot;

namespace Trackstorm.Client.Items;

/// <summary>One independently owned rigid claw and balloon. State changes are presentation
/// commands, never contact detection, EMP timers, hit validation or damage.</summary>
internal sealed partial class EmpClawVisual : Node3D
{
    internal const string AssetPath = "res://assets/items/emp-claw/EmpClaw.glb";
    private readonly Node3D[] _jaws;
    private readonly Node3D _model;
    private readonly Node3D _nose;
    private readonly EmpClawElectric _electric = new();
    private float _age;
    private EmpClawVfxSettings _releaseSettings = new();
    private float _spread;
    private float _inflation = 1;
    private Transform3D _releasePose;
    private bool _releaseCaptured;
    private float _noseAtTransition = 1;

    internal EmpClawVisualState State { get; private set; }
    internal bool Finished => State == EmpClawVisualState.NoseDestroyed ? _age >= .4f : State == EmpClawVisualState.Expired && _age >= _releaseSettings.CleanupSeconds;
    internal bool BodyVisible => _model.IsVisibleInTree();
    internal Node3D NoseAnchor { get; }
    internal Node3D Attachment { get; }
    internal IReadOnlyList<Node3D> GripContacts { get; }
    internal float Inflation => _inflation;

    internal EmpClawVisual()
    {
        Name = "EmpClaw";
        var model = Networking.MatchResourceLoader.LoadResource<PackedScene>(AssetPath).Instantiate<Node3D>();
        _model = model;
        AddChild(model);
        _jaws = Enumerable.Range(0, 4).Select(i => (Node3D)model.FindChild("JawHinge_" + i, true, false)).ToArray();
        _nose = (Node3D)model.FindChild("BalloonNose", true, false);
        NoseAnchor = (Node3D)model.FindChild("NoseHitAnchor", true, false);
        Attachment = (Node3D)model.FindChild("Attachment", true, false);
        GripContacts = Enumerable.Range(0, 4).Select(i => (Node3D)model.FindChild("GripContact_" + i, true, false)).ToArray();
        AddChild(_electric);
        ApplyPose();
    }

    /// <summary>Idempotent state observation. Restore skips entry sparks and terminal replay.</summary>
    internal void Observe(EmpClawVisualState state, bool restore = false)
    {
        if (State == state && !restore) { return; }
        State = state;
        if (state is EmpClawVisualState.Expired or EmpClawVisualState.NoseDestroyed) _releaseSettings = EmpClawVfxSettings.Current;
        SetProcess(true);
        _age = restore ? 1.3f : 0;
        _noseAtTransition = _inflation;
        _releaseCaptured = false;
        if (restore)
        {
            _spread = state is EmpClawVisualState.Landed or EmpClawVisualState.Activated ? 1 : state == EmpClawVisualState.Fired ? .35f : 0;
            _inflation = state == EmpClawVisualState.Activated ? 2.6f : 1;
        }
        _electric.Burst = !restore && state == EmpClawVisualState.Landed;
        // Retire the whole rigid model at the authoritative pop boundary. An opaque
        // gravity-only cosmetic drop can pass through any face of a moving/rolled car.
        // Keep only a short, shrinking membrane-fragment burst at the actual nose.
        _electric.Position = Vector3.Zero;
        if (state == EmpClawVisualState.NoseDestroyed)
        { _electric.Position = GlobalTransform.AffineInverse() * NoseAnchor.GlobalPosition + Vector3.Back * .16f; }
        _electric.Mode = State;
        _electric.StateAge = _age;
        _electric.Advance(0);
        ApplyPose();
    }

    public override void _Process(double delta) => Advance(Math.Max(0, (float)delta));

    internal void Advance(float delta)
    {
        _age += delta;
        bool terminal = State is EmpClawVisualState.Expired or EmpClawVisualState.NoseDestroyed;
        float targetSpread = State is EmpClawVisualState.Landed or EmpClawVisualState.Activated ? 1 : State == EmpClawVisualState.Fired ? .35f : 0;
        _spread = Mathf.MoveToward(_spread, targetSpread, delta * 8);
        if (State == EmpClawVisualState.Expired)
        { _inflation = Mathf.Lerp(_noseAtTransition, .06f, EmpClawLauncher.Ease(_age, 0, _releaseSettings.DeflationSeconds)); }
        else if (State == EmpClawVisualState.NoseDestroyed) { _inflation = .01f; }
        else { _inflation = Mathf.MoveToward(_inflation, State == EmpClawVisualState.Activated ? 2.6f : 1, delta * 7); }
        if (terminal && IsInsideTree())
        {
            if (!_releaseCaptured) { _releasePose = GlobalTransform; TopLevel = true; GlobalTransform = _releasePose; _releaseCaptured = true; }
            float fall = State == EmpClawVisualState.NoseDestroyed ? 0 : Math.Max(0, _age - _releaseSettings.DeflationSeconds);
            GlobalTransform = new Transform3D(_releasePose.Basis.Rotated(Vector3.Right, fall * 2.8f),
                _releasePose.Origin + Vector3.Down * (3 * fall * fall));
        }
        if (Finished) { Visible = false; SetProcess(false); }
        _electric.Mode = State;
        _electric.StateAge = _age;
        _electric.Advance(delta);
        ApplyPose();
    }

    private void ApplyPose()
    {
        _model.Visible = State != EmpClawVisualState.NoseDestroyed;
        foreach (Node3D jaw in _jaws) { jaw.Rotation = new(0, (1 - _spread) * Mathf.DegToRad(-78), 0); }
        _nose.Scale = Vector3.One * Math.Max(.01f, _inflation);
        _nose.Visible = State != EmpClawVisualState.NoseDestroyed;
    }
}
