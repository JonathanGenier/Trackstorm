using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>Reconstructs authoritative anchors against the same rendered vehicle frame every frame.
/// Owns only bounded cosmetic grip, electricity and release; no collision or gameplay timer.</summary>
internal sealed partial class EmpClawAttachments : Node3D
{
    private readonly Dictionary<ulong, (EmpClawAttachment State, EmpClawVisual View, float Age)> _active = [];
    private readonly List<EmpClawVisual> _released = [];
    private readonly Dictionary<(ulong Vehicle, ulong Life), EmpVehicleElectric> _electric = [];
    private ulong _revision;
    private ulong _session;
    private bool _restoring = true;
    internal Func<ulong, ulong, Transform3D?>? TargetFrame { get; set; }
    internal Func<ulong>? AcceptedTick { get; set; }
    internal IReadOnlyList<EmpClawVisual> Views => _active.Values.Select(e => e.View).ToArray();
    internal int ReleasedCount => _released.Count;

    internal void Restore()
    {
        foreach (var entry in _active.Values) { entry.View.QueueFree(); }
        foreach (var view in _released) { view.QueueFree(); }
        foreach (var effect in _electric.Values) { effect.QueueFree(); }
        _electric.Clear();
        _active.Clear(); _released.Clear(); _revision = 0; _restoring = true;
    }

    internal void Observe(ItemPublication publication)
    {
        if (_session != publication.World.Session) { Restore(); _session = publication.World.Session; }
        if (publication.Revision <= _revision) { return; }
        _revision = publication.Revision;
        ulong tick = Math.Max(publication.World.Tick, AcceptedTick?.Invoke() ?? 0);
        var retained = publication.EmpAttachments.Select(c => c.Id).ToHashSet();
        foreach (ulong id in _active.Keys.Where(id => !retained.Contains(id)).ToArray())
        {
            var entry = _active[id];
            if (TargetFrame?.Invoke(entry.State.Vehicle, entry.State.Life) is not null)
            { Release(entry.View, entry.State.ExpiresAtTick <= tick); }
            else { entry.View.QueueFree(); }
            _active.Remove(id);
        }
        foreach (var claw in publication.EmpAttachments)
        {
            if (_active.ContainsKey(claw.Id) || claw.ExpiresAtTick <= tick) { continue; }
            var view = new EmpClawVisual();
            AddChild(view);
            bool contact = !_restoring && publication.Events.Any(e => e.Token == claw.Id && e.EmpClawContact is not null);
            view.Observe(contact ? EmpClawVisualState.Landed : EmpClawVisualState.Activated, !contact);
            _active.Add(claw.Id, (claw, view, contact ? 0 : 1));
        }
        _restoring = false;
        Advance(0);
    }

    public override void _Process(double delta) => Advance((float)delta);

    private void Advance(float delta)
    {
        foreach (var pair in _active.ToArray())
        {
            var (state, view, age) = pair.Value;
            var frame = TargetFrame?.Invoke(state.Vehicle, state.Life);
            view.Visible = frame.HasValue;
            if (frame is { } target)
            {
                Vector3 normal = VehicleBody.ToGodot(state.LocalNormal);
                Basis basis = Basis.LookingAt(normal, Math.Abs(normal.Y) > .98f ? Vector3.Right : Vector3.Up);
                // Place the authored metal contact anchor on the actual collision point.
                // The local hit is never moved to a different face or accessible location.
                Transform3D anchor = view.GlobalTransform.AffineInverse() * view.Attachment.GlobalTransform;
                view.GlobalTransform = target * new Transform3D(basis, VehicleBody.ToGodot(state.LocalPosition)) * anchor.AffineInverse();
                if (age >= .14f) { view.Observe(EmpClawVisualState.Activated); }
            }
            _active[pair.Key] = (state, view, age + delta);
        }
        var affected = _active.Values.Select(e => (e.State.Vehicle, e.State.Life)).ToHashSet();
        foreach (var key in _electric.Keys.Where(k => !affected.Contains(k)).ToArray())
        { _electric[key].QueueFree(); _electric.Remove(key); }
        foreach (var key in affected)
        {
            if (!_electric.TryGetValue(key, out var effect))
            { effect = new EmpVehicleElectric(); AddChild(effect); _electric.Add(key, effect); }
            var frame = TargetFrame?.Invoke(key.Vehicle, key.Life);
            effect.Visible = frame.HasValue;
            if (frame is { } target) { effect.GlobalTransform = target; }
        }
        for (int i = _released.Count - 1; i >= 0; i--)
        {
            if (!_released[i].Finished) { continue; }
            _released[i].QueueFree(); _released.RemoveAt(i);
        }
    }

    private void Release(EmpClawVisual view, bool expired)
    {
        if (_released.Count == ItemAuthority.MaximumEmpAttachments)
        { _released[0].QueueFree(); _released.RemoveAt(0); }
        view.Observe(expired ? EmpClawVisualState.Expired : EmpClawVisualState.NoseDestroyed);
        _released.Add(view);
    }
}
