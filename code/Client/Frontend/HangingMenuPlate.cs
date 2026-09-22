using Godot;

namespace Trackstorm.Client.Frontend;

/// <summary>Reusable visual plate. Its hit target is separately owned and remains stationary.</summary>
internal sealed partial class HangingMenuPlate : Control
{
    private ShaderMaterial _treatment = null!;
    private float _selection;
    internal bool Selected { get; set; }
    internal bool Pressed { get; set; }

    internal void Initialize(MainMenuEntry entry)
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(780, 180);
        _treatment = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/frontend/main-menu/Plate.gdshader") };
        var plate = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Texture = new AtlasTexture { Atlas = GD.Load<Texture2D>("res://assets/frontend/main-menu/Plate.png"), Region = new Rect2(0, 75, 2172, 540) },
            Size = Size,
            MouseFilter = MouseFilterEnum.Ignore, Material = _treatment,
        };
        AddChild(plate);
        var icon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Texture = new AtlasTexture { Atlas = GD.Load<Texture2D>("res://assets/frontend/main-menu/Icons.png"), Region = new Rect2(entry.Icon % 2 * 627, entry.Icon / 2 * 627, 627, 627) },
            Position = new Vector2(125, 32), Size = new Vector2(112, 112),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(icon);
        var divider = new ColorRect { Position = new Vector2(270, 37), Size = new Vector2(2, 104), Color = new Color("b58b63"), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(divider);
        var label = new Label
        {
            Text = entry.Label.ToUpperInvariant(), Position = new Vector2(286, entry.Status.Length > 0 ? 25 : 40),
            Size = new Vector2(370, 88), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontOverride("font", new SystemFont { FontNames = ["Impact", "Arial Black"], FontWeight = 800 });
        label.AddThemeFontSizeOverride("font_size", 66);
        label.AddThemeColorOverride("font_color", new Color("efd6b7"));
        label.AddThemeColorOverride("font_shadow_color", new Color("130906"));
        label.AddThemeConstantOverride("shadow_offset_x", 3);
        label.AddThemeConstantOverride("shadow_offset_y", 5);
        AddChild(label);
        if (entry.Status.Length > 0)
        {
            var status = new Label { Text = entry.Status.ToUpperInvariant(), Position = new Vector2(313, 116), Size = new Vector2(314, 34), HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
            status.AddThemeFontSizeOverride("font_size", 30);
            status.AddThemeColorOverride("font_color", new Color("e5c5a2"));
            status.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color("211b18"), BorderColor = new Color("92735b"), BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2 });
            AddChild(status);
        }
        Modulate = entry.Available ? Colors.White : new Color(0.62f, 0.62f, 0.62f);
    }

    public override void _Process(double delta)
    {
        _selection = Mathf.MoveToward(_selection, Selected ? 1 : 0, (float)delta * 12);
        _treatment.SetShaderParameter("selected", _selection);
        _treatment.SetShaderParameter("pressed", Pressed ? 1.0f : 0.0f);
    }
}
