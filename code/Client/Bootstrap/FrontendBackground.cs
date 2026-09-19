using Godot;

namespace Trackstorm.Client.Bootstrap;

/// <summary>Persistent animated frontend background shared by loading and main-menu presentation.</summary>
internal sealed partial class FrontendBackground : Control
{
    private double _elapsed;

    /// <inheritdoc/>
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        _elapsed += delta;
        QueueRedraw();
    }

    /// <inheritdoc/>
    public override void _Draw()
    {
        Vector2 size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color("080d17"));
        float shift = (float)((_elapsed * 34) % 180);
        for (float x = -size.Y + shift; x < size.X + size.Y; x += 180)
        {
            var points = new[]
            {
                new Vector2(x, 0),
                new Vector2(x + 84, 0),
                new Vector2(x - size.Y + 84, size.Y),
                new Vector2(x - size.Y, size.Y),
            };
            DrawColoredPolygon(points, new Color(0.12f, 0.025f, 0.04f, 0.72f));
        }

        float horizon = size.Y * 0.72f;
        DrawLine(new Vector2(0, horizon), new Vector2(size.X, horizon), new Color("d33a45"), 3);
        for (int index = 0; index < 7; index++)
        {
            float radius = 110 + (index * 95);
            DrawArc(new Vector2(size.X * 0.5f, horizon + 75), radius, MathF.PI, MathF.Tau, 64, new Color(0.38f, 0.42f, 0.48f, 0.18f), 2);
        }
    }
}
