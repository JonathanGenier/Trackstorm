using Godot;
using Trackstorm.Core.Events;

namespace Trackstorm.Client.Development;

/// <summary>Read-only bounded journal content hosted by the unified developer-tools shell.</summary>
internal sealed partial class EventLogPanel : VBoxContainer
{
    private readonly RichTextLabel _text = new() { BbcodeEnabled = false, SelectionEnabled = true, ScrollActive = true, SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly OptionButton _filter = new();
    private readonly CheckButton _follow = new() { Text = "Follow latest", ButtonPressed = true, TooltipText = "Turn off to freeze the view. Event collection continues." };
    private IReadOnlyList<RuntimeEvent> _entries = Array.Empty<RuntimeEvent>();
    private EventStream? _rendered;
    private ulong _revision = ulong.MaxValue;
    private bool _dirty = true;

    /// <summary>Current runtime journal; retained history stays available after departure.</summary>
    internal Func<EventStream?> Source { get; set; } = () => null;

    /// <inheritdoc/>
    public override void _Ready()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        var options = new HBoxContainer();
        AddChild(options);
        _filter.AddItem("All categories");
        foreach (var category in Enum.GetValues<EventCategory>())
        {
            _filter.AddItem(category.ToString());
        }

        _filter.ItemSelected += _ => _dirty = true;
        _follow.Toggled += enabled =>
        {
            _text.ScrollFollowing = enabled;
            if (enabled)
            {
                _dirty = true;
            }
        };
        options.AddChild(_filter);
        options.AddChild(_follow);
        AddChild(new Label { Text = "Read only · bounded history · disable Follow latest to inspect earlier entries", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        AddChild(_text);
        _text.AddThemeFontSizeOverride("normal_font_size", 14);
        _text.AddThemeColorOverride("default_color", new Color("eeeeee"));
        _text.AddThemeConstantOverride("line_separation", 4);
        _text.ScrollFollowing = true;
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (IsVisibleInTree())
        {
            RefreshNow();
        }
    }

    /// <summary>Refreshes the existing journal projection immediately after tab selection.</summary>
    internal void RefreshNow()
    {
        EventStream? stream = Source();
        if (!IsInsideTree() || (!_dirty && _rendered == stream && (!_follow.ButtonPressed || _revision == (stream?.Revision ?? 0))))
        {
            return;
        }

        double scroll = _text.GetVScrollBar().Value;
        if (_rendered != stream || _follow.ButtonPressed)
        {
            _entries = stream?.Entries ?? Array.Empty<RuntimeEvent>();
            _rendered = stream;
            _revision = stream?.Revision ?? 0;
        }

        _dirty = false;
        _text.Clear();
        bool first = true;
        foreach (RuntimeEvent entry in _entries.Where(entry => _filter.Selected == 0 || (int)entry.Category == _filter.Selected - 1))
        {
            if (!first)
            {
                _text.AddText("\n");
            }

            first = false;
            EventLogFormatter.Write(entry, _text.AddText,
                value => ColoredText(value, new Color("929ca8")),
                value => ColoredText(value, new Color("69b7ff")),
                value => ColoredText(value, new Color("ff7777")));
        }

        if (!_follow.ButtonPressed)
        {
            _text.GetVScrollBar().Value = scroll;
        }
    }

    private void ColoredText(string value, Color color)
    {
        _text.PushColor(color);
        _text.AddText(value);
        _text.Pop();
    }
}
