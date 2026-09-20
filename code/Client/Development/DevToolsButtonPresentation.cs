using Godot;

namespace Trackstorm.Client.Development;

/// <summary>Consistent icon and interaction-state treatment for DevTools actions.</summary>
internal static class DevToolsButtonPresentation
{
    /// <summary>Visual roles used by the action palette.</summary>
    internal enum Treatment
    {
        /// <summary>Dark secondary action.</summary>
        Neutral,
        /// <summary>Non-destructive reset staging with a red outline.</summary>
        Reset,
        /// <summary>Positive commit action.</summary>
        Apply,
        /// <summary>Destructive or abandoning action.</summary>
        Cancel,
        /// <summary>Primary shell dismissal action.</summary>
        Close,
        /// <summary>Immediate developer match action.</summary>
        ForceStart,
    }

    /// <summary>Adds the action icon and complete normal, hover, pressed and focus palette.</summary>
    /// <param name="button">Button to present.</param>
    /// <param name="icon">Project-owned icon basename.</param>
    /// <param name="treatment">Semantic color treatment.</param>
    internal static void Configure(Button button, string icon, Treatment treatment)
    {
        button.Icon = GD.Load<Texture2D>($"res://assets/devtools/{icon}.svg");
        button.TooltipText = button.Text;
        button.CustomMinimumSize = new Vector2(Math.Max(button.CustomMinimumSize.X, 104), Math.Max(button.CustomMinimumSize.Y, 40));
        button.AddThemeColorOverride("font_color", Colors.White);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
        button.AddThemeColorOverride("font_focus_color", Colors.White);

        (Color normal, Color hover, Color pressed, Color border) = treatment switch
        {
            Treatment.Reset => (new Color("1b2027"), new Color("342329"), new Color("481f26"), new Color("e24a4a")),
            Treatment.Apply => (new Color("238636"), new Color("2ea043"), new Color("196c2e"), new Color("46b85d")),
            Treatment.Cancel => (new Color("b4232c"), new Color("d02f3a"), new Color("8f1b23"), new Color("e24a4a")),
            Treatment.Close => (new Color("1f6feb"), new Color("388bfd"), new Color("195ab6"), new Color("58a6ff")),
            Treatment.ForceStart => (new Color("9a5b13"), new Color("b86e17"), new Color("75430d"), new Color("e6a23c")),
            _ => (new Color("252a31"), new Color("343b45"), new Color("1b2027"), new Color("59636f")),
        };
        button.AddThemeStyleboxOverride("normal", Plate(normal, border));
        button.AddThemeStyleboxOverride("hover", Plate(hover, border));
        button.AddThemeStyleboxOverride("pressed", Plate(pressed, border));
        button.AddThemeStyleboxOverride("focus", Plate(Colors.Transparent, new Color("ffffff"), 2));
        button.AddThemeStyleboxOverride("disabled", Plate(normal.Darkened(0.35f), border.Darkened(0.35f)));
    }

    private static StyleBoxFlat Plate(Color fill, Color border, int width = 1) => new()
    {
        BgColor = fill,
        BorderColor = border,
        BorderWidthLeft = width,
        BorderWidthTop = width,
        BorderWidthRight = width,
        BorderWidthBottom = width,
        CornerRadiusTopLeft = 4,
        CornerRadiusTopRight = 4,
        CornerRadiusBottomLeft = 4,
        CornerRadiusBottomRight = 4,
        ContentMarginLeft = 10,
        ContentMarginRight = 10,
        ContentMarginTop = 6,
        ContentMarginBottom = 6,
    };
}
