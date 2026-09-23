using Godot;

namespace Trackstorm.Client.Frontend;

/// <summary>One disposable visual and its associated selectable nameplate; stores no membership or ready state.</summary>
internal sealed class LobbyShowcase : IDisposable
{
    private Node3D _model;
    internal Node3D Root { get; } = new() { RotationDegrees = new Vector3(0, 180, 0) };
    internal Button Target { get; } = new() { Size = new Vector2(200, 52), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };

    internal LobbyShowcase(Node3D world, Control canvas, Node3D model)
    {
        _model = model;
        world.AddChild(Root);
        Root.AddChild(model);
        Target.AddThemeFontSizeOverride("font_size", 18);
        JoinedLobby.Style(Target);
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
