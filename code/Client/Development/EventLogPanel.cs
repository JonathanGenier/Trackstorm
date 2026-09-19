using Godot;
using Trackstorm.Core.Events;

namespace Trackstorm.Client.Development;

/// <summary>Read-only bounded journal content hosted by the unified developer-tools shell.</summary>
internal sealed partial class EventLogPanel : VBoxContainer
{
    private readonly RichTextLabel _text = new() { BbcodeEnabled = false, SelectionEnabled = true, ScrollActive = true, SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly OptionButton _filter = new();
    private readonly CheckButton _follow = new() { Text = "Follow latest (off freezes view)", ButtonPressed = true };
    private EventStream? _rendered;
    private ulong _revision = ulong.MaxValue;

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

        _filter.ItemSelected += _ => _revision = ulong.MaxValue;
        _follow.Toggled += enabled =>
        {
            _text.ScrollFollowing = enabled;
            _revision = ulong.MaxValue;
        };
        options.AddChild(_filter);
        options.AddChild(_follow);
        AddChild(new Label { Text = "Read only · bounded history · disable Follow latest to inspect earlier entries", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        AddChild(_text);
        _text.AddThemeFontSizeOverride("normal_font_size", 14);
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
        if (!IsInsideTree() || stream is null || (_rendered == stream && (_revision == stream.Revision || (!_follow.ButtonPressed && _revision != ulong.MaxValue))))
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
}
