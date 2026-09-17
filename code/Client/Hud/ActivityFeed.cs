using Godot;
using Trackstorm.Core.Events;

namespace Trackstorm.Client.Hud;

/// <summary>Automatic, input-transparent gameplay feed over the shared event journal.</summary>
internal sealed partial class ActivityFeed : CanvasLayer
{
    private readonly ActivityFeedView _view = new();
    private readonly Control _root = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly List<Label> _labels = new();

    /// <summary>The same session or practice journal used by the Event Log.</summary>
    internal Func<EventStream?> Source { get; set; } = () => null;
    /// <summary>Whether the existing arena is currently present.</summary>
    internal Func<bool> Gameplay { get; set; } = () => false;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Layer = 2;
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        for (int index = 0; index < ActivityFeedView.Capacity; index++)
        {
            var label = new Label
            {
                Name = $"Entry{index}",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
            label.AddThemeConstantOverride("outline_size", 4);
            _root.AddChild(label);
            _labels.Add(label);
        }

        Refresh(0);
    }

    /// <inheritdoc/>
    public override void _Process(double delta) => Refresh(delta);

    /// <inheritdoc/>
    public override void _ExitTree() => _view.Dispose();

    /// <summary>Updates timed rows and their input-transparent layout.</summary>
    /// <param name="delta">Elapsed presentation seconds.</param>
    internal void Refresh(double delta)
    {
        _view.Update(Source(), Gameplay(), delta);
        Visible = _view.Entries.Count > 0;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        float scale = Math.Clamp(Math.Min(size.X / 1280, size.Y / 720), 0.75f, 1.5f);
        float width = Math.Min(580 * scale, size.X - 32);
        for (int index = 0; index < _labels.Count; index++)
        {
            Label label = _labels[index];
            label.Visible = index < _view.Entries.Count;
            label.Position = new Vector2(size.X - width - 16, 38 + (index * 26 * scale));
            label.Size = new Vector2(width, 26 * scale);
            label.AddThemeFontSizeOverride("font_size", (int)(18 * scale));
            if (label.Visible)
            {
                ActivityFeedEntry entry = _view.Entries[index];
                label.Text = entry.Text;
                Color color = entry.Tone switch
                {
                    ActivityFeedTone.Arrival => new Color("a8e6bc"),
                    ActivityFeedTone.Departure => new Color("edce96"),
                    _ => new Color("f2aaa4"),
                };
                label.AddThemeColorOverride("font_color", color);
            }
        }
    }
}
