using Godot;

namespace Trackstorm.Client.Settings;

/// <summary>Reusable carnival steel presentation; contains no navigation or settings behavior.</summary>
internal sealed partial class MenuPresentation : Control
{
    private Texture2D _steel = null!;

    /// <inheritdoc/>
    public override void _Ready() => _steel = GD.Load<Texture2D>("res://assets/hud/Health.png");

    /// <inheritdoc/>
    public override void _Draw()
    {
        float bottom = Size.Y - 10;
        DrawRect(new Rect2(0, 80, 640, bottom - 80), new Color("131516"));
        for (int x = 0; x < 640; x += 80)
        {
            for (int y = 80; y < bottom; y += 59)
            {
                DrawTextureRectRegion(_steel, new Rect2(x, y, 80, Math.Min(59, bottom - y)), new Rect2(_steel.GetSize() * new Vector2(0.36f, 0.64f), _steel.GetSize() * new Vector2(0.19f, 0.10f)), new Color(1.8f, 1.6f, 1.4f));
            }
        }

        DrawRect(new Rect2(8, 87, 624, bottom - 95), new Color("766355"), false, 5);
        DrawRect(new Rect2(23, 100, 594, bottom - 125), new Color(0.025f, 0.028f, 0.03f, 0.9f));
        DrawRect(new Rect2(24, 100, 592, 58), new Color("481b1b"));
        DrawLine(new Vector2(24, 159), new Vector2(616, 159), new Color("eb5643"), 2);
        for (int index = 0; index < 150; index++)
        {
            float x = 16 + ((index * 137) % 604);
            float y = 88 + ((index * 83) % (bottom - 110));
            DrawLine(new Vector2(x, y), new Vector2(Math.Min(624, x + 4 + (index % 21)), y - 2), new Color(0.7f, 0.63f, 0.5f, 0.09f));
        }

        for (int x = 32; x < 640; x += 48)
        {
            Rivet(new Vector2(x, 92));
            Rivet(new Vector2(x, bottom - 16));
            if (x < 272 || x > 368)
            {
                DrawColoredPolygon(new[] { new Vector2(x - 14, 80), new Vector2(x, 52), new Vector2(x + 14, 80) }, new Color("6b5d4e"));
                DrawLine(new Vector2(x, 52), new Vector2(x + 14, 80), new Color("b3a08a"), 2);
            }
        }

        foreach (float x in new[] { 8f, 632f })
        {
            for (int y = 108; y < bottom; y += 23)
            {
                DrawEllipse(new Vector2(x, y), 6, 12, new Color("8b7660"));
            }

            foreach (float y in new[] { 114f, 143f })
            {
                DrawCircle(new Vector2(x + (x < 100 ? 27 : -27), y), 11, new Color("6b1511"));
                DrawCircle(new Vector2(x + (x < 100 ? 27 : -27), y), 6, new Color("ff6346"));
                DrawCircle(new Vector2(x + (x < 100 ? 27 : -27), y), 3, new Color("ffe0b0"));
            }
        }

        // Original clown crest and striped canopy, drawn separately from every interactive row.
        for (int index = 0; index < 12; index++)
        {
            DrawColoredPolygon(new[] { new Vector2(320, 20), new Vector2(42 + (index * 46), 82), new Vector2(88 + (index * 46), 82) }, new Color(index % 2 == 0 ? "702820" : "aa9d83"));
        }

        DrawCircle(new Vector2(320, 53), 40, new Color("2b2521"));
        DrawCircle(new Vector2(320, 52), 32, new Color("c8b89b"));
        foreach (float x in new[] { 303f, 337f })
        {
            DrawColoredPolygon(new[] { new Vector2(x - 9, 47), new Vector2(x, 25), new Vector2(x + 9, 47), new Vector2(x, 60) }, new Color("292626"));
            DrawCircle(new Vector2(x, 47), 5, new Color("fb382c"));
        }

        DrawArc(new Vector2(320, 53), 23, 0.15f, Mathf.Pi - 0.15f, 20, new Color("2a1715"), 11, true);
        DrawArc(new Vector2(320, 53), 22, 0.25f, Mathf.Pi - 0.25f, 20, new Color("e0cdb0"), 4, true);
        DrawCircle(new Vector2(320, 56), 8, new Color("af201c"));
        DrawCircle(new Vector2(317, 53), 2, new Color("ff9c72"));
    }

    /// <summary>Builds native styles with an obvious red keyboard/gamepad focus state.</summary>
    /// <returns>The shared focus and control theme.</returns>
    internal static Theme CreateTheme()
    {
        var theme = new Theme { DefaultFontSize = 22 };
        foreach (string type in new[] { "Button", "OptionButton", "CheckButton" })
        {
            theme.SetStylebox("normal", type, Plate("252728", "73695b"));
            theme.SetStylebox("hover", type, Plate("4b2220", "c17754"));
            theme.SetStylebox("pressed", type, Plate("651d1c", "ff795a"));
            var focus = Plate("531d1b", "ff795a");
            focus.ShadowColor = new Color(0.95f, 0.12f, 0.05f, 0.35f);
            focus.ShadowSize = 5;
            theme.SetStylebox("focus", type, focus);
            theme.SetColor("font_color", type, new Color("e8dfca"));
            theme.SetColor("font_focus_color", type, new Color("fff4da"));
        }

        theme.SetStylebox("slider", "HSlider", Plate("171819", "6b6357"));
        theme.SetStylebox("grabber_area", "HSlider", Plate("a42c25", "ef6450"));
        theme.SetStylebox("grabber_area_highlight", "HSlider", Plate("e14a32", "ffd7a0"));
        return theme;
    }

    private static StyleBoxFlat Plate(string fill, string border) => new()
    {
        BgColor = new Color(fill),
        BorderColor = new Color(border),
        BorderWidthLeft = 2,
        BorderWidthRight = 2,
        BorderWidthTop = 2,
        BorderWidthBottom = 2,
        CornerRadiusTopLeft = 6,
        CornerRadiusTopRight = 6,
        CornerRadiusBottomLeft = 6,
        CornerRadiusBottomRight = 6,
        ContentMarginLeft = 16,
        ContentMarginRight = 16,
        ContentMarginTop = 8,
        ContentMarginBottom = 8,
    };

    private void DrawEllipse(Vector2 center, float width, float height, Color color)
    {
        Vector2[] points = Enumerable.Range(0, 17).Select(index => center + new Vector2(Mathf.Cos(index * Mathf.Tau / 16) * width, Mathf.Sin(index * Mathf.Tau / 16) * height)).ToArray();
        DrawPolyline(points, color, 2, true);
    }

    private void Rivet(Vector2 position)
    {
        DrawCircle(position, 4, new Color("b39a78"));
        DrawLine(position - new Vector2(2, 0), position + new Vector2(2, 0), new Color("292624"), 2);
    }
}
