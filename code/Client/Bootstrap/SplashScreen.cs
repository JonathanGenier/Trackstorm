using Godot;

namespace Trackstorm.Client.Bootstrap;

/// <summary>Dedicated, self-timed splash presentation with no application initialization responsibility.</summary>
internal sealed partial class SplashScreen : CanvasLayer
{
    private readonly Control _root = new();
    private double _elapsed;

    /// <summary>Raised once after this presentation has fully elapsed.</summary>
    internal event Action? Completed;

    /// <summary>Presentation duration in seconds.</summary>
    internal double Duration { get; set; } = 1.4;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Layer = 20;
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var backdrop = new ColorRect { Color = new Color("080b12"), MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(backdrop);
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var title = new Label
        {
            Text = "TRACKSTORM",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorRight = 1,
            AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        title.AddThemeFontSizeOverride("font_size", 54);
        title.AddThemeColorOverride("font_color", new Color("e9edf5"));
        title.AddThemeColorOverride("font_shadow_color", new Color("a81f2b"));
        title.AddThemeConstantOverride("shadow_offset_x", 4);
        title.AddThemeConstantOverride("shadow_offset_y", 4);
        _root.AddChild(title);
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        _elapsed += delta;
        double fade = Math.Min(_elapsed / 0.25, Math.Max(0, (Duration - _elapsed) / 0.25));
        _root.Modulate = new Color(1, 1, 1, (float)Math.Clamp(fade, 0, 1));
        if (_elapsed >= Duration)
        {
            SetProcess(false);
            Completed?.Invoke();
        }
    }
}
