using Godot;

namespace Trackstorm.Client.Hud;

/// <summary>Blank reference-derived chassis. Content and gauge fill remain live Client presentation.</summary>
internal sealed partial class ItemHudFrame : TextureRect
{
    internal ShaderMaterial GaugeMaterial { get; private set; } = null!;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(600, 200);
        ExpandMode = ExpandModeEnum.IgnoreSize;
        Texture = Bootstrap.StartupController.LoadResource<Texture2D>("res://assets/hud/ItemAssembly.png");
        GaugeMaterial = new ShaderMaterial { Shader = Bootstrap.StartupController.LoadResource<Shader>("res://assets/hud/ItemAssembly.gdshader") };
        Material = GaugeMaterial;
    }
}
