using Godot;

namespace Trackstorm.Client.Hud;

/// <summary>Project-authored adjoining steel chassis, reusing the existing approved HUD steel sample.</summary>
internal sealed partial class ItemHudFrame : Control
{
    private Texture2D _steel = null!;

    public override void _Ready() => _steel = Bootstrap.StartupController.LoadResource<Texture2D>("res://assets/hud/Health.png");

    public override void _Draw()
    {
        // The left joint is overlapped by the speedometer's original bolted foot.
        DrawRect(new Rect2(0, 0, 294, 96), new Color("090909"));
        for (int x = 0; x < 294; x += 98)
        {
            DrawTextureRectRegion(_steel, new Rect2(x, 0, 98, 96), new Rect2(_steel.GetSize() * new Vector2(.36f, .64f), _steel.GetSize() * new Vector2(.19f, .10f)), new Color(2.8f, 2.3f, 1.9f));
        }
        DrawRect(new Rect2(2, 2, 290, 92), new Color("907d6b"), false, 2);
        DrawRect(new Rect2(5, 5, 284, 86), new Color("351e17"), false, 2);
        foreach (int x in new[] { 12, 150 })
        {
            DrawRect(new Rect2(x, 10, 130, 76), new Color(0.015f, 0.019f, 0.021f, .78f));
            DrawLine(new Vector2(x, 87), new Vector2(x + 129, 87), new Color("813328"), 2);
        }
        for (int i = 0; i < 72; i++)
        {
            float x = 8 + i * 47 % 277;
            float y = 7 + i * 29 % 82;
            DrawLine(new Vector2(x, y), new Vector2(Math.Min(286, x + 3 + i % 7), y - 2), new Color(.75f, .60f, .45f, .12f));
        }
        foreach (int x in new[] { 12, 146, 282 })
        {
            Rivet(new Vector2(x, 6));
            Rivet(new Vector2(x, 90));
        }
        foreach (int x in new[] { 70, 210, 286 }) Spike(new Vector2(x, 1), new Vector2(x + 1, -19));
        Spike(new Vector2(290, 48), new Vector2(310, 42));
        Spike(new Vector2(286, 90), new Vector2(303, 104));
    }

    private void Rivet(Vector2 p)
    {
        DrawCircle(p, 5, new Color("181312"));
        DrawCircle(p, 3.5f, new Color("a6947e"));
        DrawCircle(p, 2, new Color("403a34"));
        DrawLine(p - new Vector2(1, 1), p + new Vector2(1, 1), new Color("191717"));
    }

    private void Spike(Vector2 p, Vector2 tip)
    {
        Vector2 side = (tip - p).Orthogonal().Normalized() * 6;
        DrawColoredPolygon(new[] { p - side, tip, p + side }, new Color("372b23"));
        DrawColoredPolygon(new[] { p - side, tip, p }, new Color("a89a86"));
        DrawPolyline(new[] { p - side, tip, p + side }, new Color("665242"), 1, true);
    }
}
