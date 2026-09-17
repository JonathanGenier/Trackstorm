using Godot;
using Trackstorm.Core.Events;

namespace Trackstorm.Client.Development;

/// <summary>Read-only bounded journal viewer, available in both development Debug and Release exports.</summary>
internal sealed partial class EventLogPanel : CanvasLayer
{
    private readonly PanelContainer _panel = new();
    private readonly RichTextLabel _text = new() { BbcodeEnabled = false, SelectionEnabled = true, ScrollActive = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
    private readonly OptionButton _filter = new();
    private readonly CheckButton _follow = new() { Text = "Follow latest (off freezes view)", ButtonPressed = true };
    private EventStream? _rendered;
    private ulong _revision = ulong.MaxValue;

    /// <summary>Current runtime journal; retained history stays available after departure.</summary>
    internal Func<EventStream?> Source { get; set; } = () => null;

    /// <summary>Composed input gate, independent of the Settings overlay.</summary>
    internal Action<bool>? SuppressInput { get; set; }

    /// <inheritdoc/>
    public override void _Ready()
    {
        Layer = 30;
        AddChild(_panel);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.035f, 0.045f, 0.06f, 0.96f),
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        });
        var column = new VBoxContainer();
        _panel.AddChild(column);
        var row = new HBoxContainer();
        column.AddChild(row);
        row.AddChild(new Label { Text = "Event Log · F3", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var close = new Button { Text = "Close" };
        close.Pressed += () => SetOpen(false);
        row.AddChild(close);
        var options = new HBoxContainer();
        column.AddChild(options);
        _filter.AddItem("All categories");
        foreach (var category in Enum.GetValues<EventCategory>())
        {
            _filter.AddItem(category.ToString());
        }

        _filter.ItemSelected += _ => _revision = ulong.MaxValue;
        _follow.Toggled += enabled =>
        {
            _text.ScrollFollowing = enabled;
            _revision = ulong.MaxValue;
        };
        options.AddChild(_filter);
        options.AddChild(_follow);
        column.AddChild(new Label { Text = "Read-only · bounded history · disable Follow latest to inspect earlier entries", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        column.AddChild(_text);
        _text.AddThemeFontSizeOverride("normal_font_size", 14);
        _text.ScrollFollowing = true;
        _panel.Hide();
    }

    /// <inheritdoc/>
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key && (key.Keycode == Key.F3 || key.PhysicalKeycode == Key.F3))
        {
            SetOpen(!_panel.Visible);
            _revision = ulong.MaxValue;
            GetViewport().SetInputAsHandled();
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree() => SuppressInput?.Invoke(false);

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        Vector2 size = GetViewport().GetVisibleRect().Size;
        _panel.Position = size * new Vector2(0.04f, 0.08f);
        _panel.Size = size * new Vector2(0.92f, 0.78f);
        EventStream? stream = Source();
        if (!_panel.Visible || stream is null || (_rendered == stream && (_revision == stream.Revision || (!_follow.ButtonPressed && _revision != ulong.MaxValue))))
        {
            return;
        }

        double scroll = _text.GetVScrollBar().Value;
        _rendered = stream;
        _revision = stream.Revision;
        _text.Text = string.Join('\n', stream.Entries.Where(entry => _filter.Selected == 0 || (int)entry.Category == _filter.Selected - 1).Select(EventLogFormatter.Format));
        if (!_follow.ButtonPressed)
        {
            _text.GetVScrollBar().Value = scroll;
        }
    }

    private void SetOpen(bool open)
    {
        _panel.Visible = open;
        SuppressInput?.Invoke(open);
    }

}
