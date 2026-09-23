using Godot;

namespace Trackstorm.Client.Frontend;

/// <summary>One disposable visual and its associated selectable nameplate; stores no membership or ready state.</summary>
internal sealed class LobbyShowcase : IDisposable
{
    private Node3D _model;
    internal int Slot { get; }
    internal Node3D Root { get; } = new() { RotationDegrees = new Vector3(0, 180, 0) };
    internal Button Target { get; } = new() { Size = new Vector2(148, 48), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };

    internal LobbyShowcase(Node3D world, Control canvas, Node3D model, int slot)
    {
        Slot = slot;
        _model = model;
        world.AddChild(Root);
        Root.AddChild(new MeshInstance3D { Position = new Vector3(0, -0.885f, 0), Mesh = new PlaneMesh { Size = new Vector2(4, 6) }, MaterialOverride = new ShaderMaterial { Shader = new Shader { Code = "shader_type spatial; render_mode unshaded, cull_disabled, depth_draw_never; void fragment() { float d = length((UV - vec2(0.5)) * 2.0); ALBEDO = vec3(0.025, 0.012, 0.006); ALPHA = (1.0 - smoothstep(0.3, 1.0, d)) * 0.65; }" } } });
        Root.AddChild(model);
        Target.AddThemeFontSizeOverride("font_size", 18);
        Target.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color(0.035f, 0.025f, 0.02f, 0.87f), BorderColor = new Color("8e7150"), BorderWidthBottom = 1 });
        Target.AddThemeStyleboxOverride("disabled", Target.GetThemeStylebox("normal"));
        Target.AddThemeStyleboxOverride("focus", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = new Color("ffcc70"), BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2 });
        canvas.AddChild(Target);
    }

    internal void Replace(Node3D model)
    {
        Root.RemoveChild(_model);
        _model.QueueFree();
        _model = model;
        Root.AddChild(model);
    }

    public void Dispose()
    {
        Root.GetParent().RemoveChild(Root);
        Root.QueueFree();
        Target.GetParent().RemoveChild(Target);
        Target.QueueFree();
    }
}
