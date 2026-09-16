using Godot;

namespace Trackstorm.Client.Hud;

/// <summary>Original procedural steel frame inspired by the supplied Match Standings references.</summary>
internal sealed partial class StandingsMetal : Control
{
    private Texture2D _steel = null!;

    /// <inheritdoc/>
    public override void _Ready() => _steel = GD.Load<Texture2D>("res://assets/hud/Health.png");

    /// <inheritdoc/>
    public override void _Draw()
    {
        DrawRect(new Rect2(-9, -9, 1058, 538), new Color(0, 0, 0, 0.65f));
        DrawRect(new Rect2(0, 0, 1040, 520), new Color("191b1c"));
        for (int x = 0; x < 1040; x += 130)
        {
            for (int y = 0; y < 520; y += 104)
            {
                DrawTextureRectRegion(_steel, new Rect2(x, y, 130, 104), new Rect2(_steel.GetSize() * new Vector2(0.36f, 0.64f), _steel.GetSize() * new Vector2(0.19f, 0.10f)), new Color(2.8f, 2.3f, 1.9f));
            }
        }

        DrawRect(new Rect2(7, 7, 1026, 506), new Color("675247"), false, 3);
        DrawRect(new Rect2(15, 15, 1010, 490), new Color(0.035f, 0.04f, 0.045f, 0.83f));
        DrawRect(new Rect2(20, 20, 1000, 69), new Color(0.28f, 0.01f, 0.015f, 0.65f));
        DrawColoredPolygon(new[] { new Vector2(20, 20), new Vector2(630, 20), new Vector2(560, 89), new Vector2(20, 89) }, new Color(0.6f, 0.035f, 0.025f, 0.32f));
        DrawRect(new Rect2(20, 89, 1000, 3), new Color("bc342c"));
        DrawRect(new Rect2(28, 130, 984, 40), new Color(0.5f, 0.035f, 0.04f, 0.75f));
        DrawRect(new Rect2(28, 130, 4, 40), new Color("f0ca84"));
        for (int row = 1; row < 8; row++)
        {
            DrawRect(new Rect2(28, 130 + (row * 40), 984, 40), new Color(row % 2 == 0 ? "1b1d1e" : "151718"));
        }

        for (int row = 0; row <= 8; row++)
        {
            DrawLine(new Vector2(28, 130 + (row * 40)), new Vector2(1012, 130 + (row * 40)), new Color("44403a"));
        }

        // Fixed arithmetic creates repeatable wear without consuming gameplay randomness.
        for (int index = 0; index < 230; index++)
        {
            float x = 20 + ((index * 137) % 1000);
            float y = 20 + ((index * 83) % 480);
            DrawLine(new Vector2(x, y), new Vector2(Math.Min(1020, x + 4 + (index % 23)), y - 1), new Color(0.65f, 0.59f, 0.49f, 0.045f), 1);
        }

        for (int x = 22; x < 1040; x += 83)
        {
            Rivet(new Vector2(x, 8));
            Rivet(new Vector2(x, 512));
        }

        for (int y = 46; y < 510; y += 70)
        {
            Rivet(new Vector2(8, y));
            Rivet(new Vector2(1032, y));
        }

        foreach (float x in new[] { 0f, 1040f })
        {
            float direction = x == 0 ? -1 : 1;
            for (int y = 38; y < 510; y += 64)
            {
                var tip = new Vector2(x + (direction * 16), y - 17);
                DrawColoredPolygon(new[] { new Vector2(x, y - 10), tip, new Vector2(x, y + 9) }, new Color("66584a"));
                DrawLine(tip, new Vector2(x, y + 9), new Color("b4a493"), 1);
            }

            foreach (float y in new[] { 15f, 505f })
            {
                float vertical = y < 100 ? -1 : 1;
                DrawColoredPolygon(new[] { new Vector2(x, y + (vertical * 13)), new Vector2(x + (direction * 20), y + (vertical * 35)), new Vector2(x - (direction * 13), y) }, new Color("75675a"));
            }
        }
    }

    private void Rivet(Vector2 position)
    {
        DrawCircle(position + Vector2.One, 4, Colors.Black);
        DrawCircle(position, 3, new Color("877360"));
        DrawLine(position - new Vector2(2, 0), position + new Vector2(2, 0), new Color("292624"));
    }
}
