using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Ordinary free-aim cursor and unchanged body reference for accuracy checks; target art is world-space.</summary>
internal sealed partial class WeaponAimOverlay : Control
{
    private Vector2? _marker;
    private Rect2? _bounds;
    private bool _ready;
    private bool _targetMarked;
    internal float SizeMultiplier { get; set; } = 1;

    internal Vector2? Marker => _marker;
    // Undrawn production-body reference. Decorative marker sizes never enter accuracy.
    internal Rect2? Bounds => _bounds;

    internal void Present(Vector2 center, bool ready, Rect2? bounds, bool targetMarked = false)
    {
        _marker = center;
        _ready = ready;
        _bounds = bounds;
        _targetMarked = targetMarked;
        QueueRedraw();
    }

    internal void Reset()
    {
        _marker = null;
        _bounds = null;
        _targetMarked = false;
        QueueRedraw();
    }

    public override void _Draw()
    {
        float scale = Math.Max(.7f, GetViewportRect().Size.Y / 1080) * SizeMultiplier;
        if (_marker is Vector2 marker)
        {
            var rect = new Rect2(marker - Vector2.One * (4 * scale), Vector2.One * (8 * scale));
            DrawRect(rect.Grow(scale), new Color(0, 0, 0, .8f), false, 3 * scale);
            DrawRect(rect, _targetMarked ? Colors.LimeGreen : _ready ? Colors.White : new Color(1, .35f, .2f, .85f), false, 1.5f * scale);
        }
    }
}
