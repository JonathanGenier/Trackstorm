using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Arena-owned, bounded collection of accepted claw views. Reconstruction suppresses
/// historical entry effects; missing IDs and arena teardown remove all owned resources.</summary>
internal sealed partial class EmpClawPresentation : Node3D
{
    internal const int MaximumViews = 64;
    private readonly Dictionary<ulong, EmpClawVisual> _views = [];
    internal IReadOnlyDictionary<ulong, EmpClawVisual> Views => _views;

    internal void Observe(IEnumerable<EmpClawView> publication, bool restore = false)
    {
        var states = publication.ToArray();
        if (states.Length > MaximumViews || states.Any(state => state.Id == 0 || !state.Pose.IsFinite() || !Enum.IsDefined(state.State)) ||
            states.Select(state => state.Id).Distinct().Count() != states.Length)
        { throw new ArgumentException("Claw presentation requires unique finite bounded views."); }
        if (restore) { Clear(); }
        var retained = states.Select(state => state.Id).ToHashSet();
        foreach (ulong id in _views.Keys.Where(id => !retained.Contains(id)).ToArray())
        { _views[id].QueueFree(); _views.Remove(id); }
        foreach (var state in states)
        {
            if (!_views.TryGetValue(state.Id, out var view))
            {
                view = new EmpClawVisual();
                AddChild(view);
                _views.Add(state.Id, view);
                view.GlobalTransform = state.Pose;
            }
            if (view.State is not (EmpClawVisualState.Expired or EmpClawVisualState.NoseDestroyed)) { view.GlobalTransform = state.Pose; }
            view.Observe(state.State, restore);
        }
    }

    internal void Clear()
    {
        foreach (var view in _views.Values) { view.QueueFree(); }
        _views.Clear();
    }
}
