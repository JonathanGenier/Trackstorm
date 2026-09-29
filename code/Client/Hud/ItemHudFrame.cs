using Godot;

namespace Trackstorm.Client.Hud;

/// <summary>Blank reference-derived chassis. Content and gauge fill remain live Client presentation.</summary>
internal sealed partial class ItemHudFrame : TextureRect
{
    // The shader samples the tight visible region of the unchanged generated PNG.
    internal static Vector2 DesignSize => new(600, 144.24f);

    internal static Font CreateFont() => new SystemFont
    {
        FontNames = ["Impact", "Roboto Condensed", "Arial Narrow"],
        FontWeight = 700,
    };

    internal ShaderMaterial GaugeMaterial { get; private set; } = null!;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = DesignSize;
        ExpandMode = ExpandModeEnum.IgnoreSize;
        Texture = Bootstrap.StartupController.LoadResource<Texture2D>("res://assets/hud/CornerAssembly.png");
        GaugeMaterial = new ShaderMaterial { Shader = Bootstrap.StartupController.LoadResource<Shader>("res://assets/hud/CornerAssembly.gdshader") };
        Material = GaugeMaterial;
    }
}
