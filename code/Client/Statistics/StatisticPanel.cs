using Godot;

namespace Trackstorm.Client.Statistics;

/// <summary>Read-only live diagnostics content hosted by the unified developer-tools shell.</summary>
internal sealed partial class StatisticPanel : VBoxContainer
{
    private readonly OptionButton _players = new();
    private readonly VBoxContainer _global = new();
    private readonly VBoxContainer _player = new();
    private ulong[] _identities = [];
    private ulong _selected;
    private double _refresh;

    /// <summary>Read-only projection supplied by the composition root.</summary>
    internal Func<ulong, StatisticView> Capture { get; set; } = id => RuntimeStatistics.Capture(null, null, id);
    /// <summary>Actual tab-content coverage for native verification.</summary>
    internal Rect2 Bounds => GetGlobalRect();
    /// <summary>Last displayed projection; carries no gameplay authority.</summary>
    internal StatisticView? View { get; private set; }

    /// <inheritdoc/>
    public override void _Ready()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddChild(new Label { Text = "Read only · live at 5 Hz · simulation continues · vehicle values are confirmed state", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        AddChild(tabs);
        var globalPage = new VBoxContainer { Name = "Global - Session" };
        tabs.AddChild(globalPage);
        AddScroll(globalPage, _global);
        var playerPage = new VBoxContainer { Name = "Player - Vehicle" };
        tabs.AddChild(playerPage);
        playerPage.AddChild(_players);
        AddScroll(playerPage, _player);
        _players.ItemSelected += index =>
        {
            if (index >= 0 && index < _identities.Length)
            {
                _selected = _identities[(int)index];
            }

            RefreshNow();
        };
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (IsVisibleInTree() && (_refresh -= delta) <= 0)
        {
            RefreshNow();
        }
    }

    /// <summary>Refreshes the existing read-only projection immediately after tab selection.</summary>
    internal void RefreshNow()
    {
        if (!IsInsideTree())
        {
            return;
        }

        View = Capture(_selected);
        _selected = View.Selected;
        if (!_identities.SequenceEqual(View.Players))
        {
            _identities = View.Players.ToArray();
            _players.Clear();
            foreach (ulong id in _identities)
            {
                _players.AddItem($"Player / vehicle {id}");
            }
        }

        _players.Disabled = _identities.Length == 0;
        _players.Select(Array.IndexOf(_identities, _selected));
        Render(_global, View.Global);
        Render(_player, View.Player);
        _refresh = 0.2;
    }

    private static void AddScroll(VBoxContainer parent, VBoxContainer content)
    {
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        parent.AddChild(scroll);
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        content.AddThemeConstantOverride("separation", 16);
        scroll.AddChild(content);
    }

    private static void Render(VBoxContainer column, IReadOnlyList<StatisticSection> sections)
    {
        if (column.GetChildCount() != sections.Count)
        {
            foreach (Node child in column.GetChildren())
            {
                column.RemoveChild(child);
                child.QueueFree();
            }

            foreach (var section in sections)
            {
                column.AddChild(new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore });
            }
        }

        for (int index = 0; index < sections.Count; index++)
        {
            var section = sections[index];
            column.GetChild<Label>(index).Text = section.Title + "\n" + section.Text;
        }
    }
}
