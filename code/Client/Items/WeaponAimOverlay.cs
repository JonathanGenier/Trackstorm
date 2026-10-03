using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Camera-centered square replaced by disconnected corners on a visible car hit; no targeting authority.</summary>
internal sealed partial class WeaponAimOverlay : Control
{
    private Vector2? _marker;
    private Rect2? _bounds;
    private bool _ready;

    internal Vector2? Marker => _marker;
    internal Rect2? Bounds => _bounds;

    internal void Present(Vector2 center, bool ready, Rect2? bounds)
    {
        _marker = center;
        _ready = ready;
        _bounds = bounds;
        QueueRedraw();
    }

    internal void Reset()
    {
        _marker = null;
        _bounds = null;
        QueueRedraw();
    }

    public override void _Draw()
    {
        float scale = Math.Max(.7f, GetViewportRect().Size.Y / 1080);
        if (_bounds is Rect2 bounds)
        {
            float length = Math.Min(16 * scale, Math.Min(bounds.Size.X, bounds.Size.Y) * .2f);
            foreach (Vector2 corner in new[] { Vector2.Zero, Vector2.Right, Vector2.Down, Vector2.One })
            {
                Vector2 p = bounds.Position + bounds.Size * corner;
                Vector2 x = Vector2.Right * (corner.X == 0 ? length : -length);
                Vector2 y = Vector2.Down * (corner.Y == 0 ? length : -length);
                var red = new Color(.94f, .14f, .10f, .85f);
                var white = new Color(1, .96f, .92f);
                DrawLine(p, p + x, red, 4 * scale, true); DrawLine(p, p + y, red, 4 * scale, true);
                DrawLine(p, p + x * .7f, white, 1.4f * scale, true); DrawLine(p, p + y * .7f, white, 1.4f * scale, true);
            }
        }
        else if (_marker is Vector2 marker)
        {
            var rect = new Rect2(marker - Vector2.One * (4 * scale), Vector2.One * (8 * scale));
            DrawRect(rect.Grow(scale), new Color(0, 0, 0, .8f), false, 3 * scale);
            DrawRect(rect, _ready ? Colors.White : new Color(1, .35f, .2f, .85f), false, 1.5f * scale);
        }
    }
}
