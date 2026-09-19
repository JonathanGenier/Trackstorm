using Godot;

namespace Trackstorm.Client.Statistics;

/// <summary>Read-only live diagnostics content hosted by the unified developer-tools shell.</summary>
internal sealed partial class StatisticPanel : VBoxContainer
{
    private readonly OptionButton _players = new();
    private readonly VBoxContainer _global = new();
    private readonly VBoxContainer _player = new();
    private readonly LineEdit _search = new() { PlaceholderText = "Search statistics by label, category or value", ClearButtonEnabled = true };
    private ulong[] _identities = [];
    private ulong _selected;
    private double _refresh;

    /// <summary>Read-only projection supplied by the composition root.</summary>
    internal Func<ulong, StatisticView> Capture { get; set; } = id => RuntimeStatistics.Capture(null, null, id);
    /// <summary>Actual tab-content coverage for native verification.</summary>
    internal Rect2 Bounds => GetGlobalRect();
    /// <summary>Whether printable keyboard bindings must remain search text instead of menu actions.</summary>
    internal bool EditingSearch => _search.HasFocus();
    /// <summary>Last displayed projection; carries no gameplay authority.</summary>
    internal StatisticView? View { get; private set; }

    /// <inheritdoc/>
    public override void _Ready()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddChild(new Label { Text = "Read only · live at 5 Hz · simulation continues · vehicle values are confirmed state", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        AddChild(_search);
        _search.TextChanged += _ => RenderView();
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
        RenderView();
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

    private static void Render(VBoxContainer column, IReadOnlyList<StatisticSection> sections, string query)
    {
        if (column.GetChildCount() == 0)
        {
            column.AddChild(CreateLabel("No matching statistics in this view. Try another search or switch views.", Colors.White));
        }

        while (column.GetChildCount() > sections.Count + 1)
        {
            Node last = column.GetChild(column.GetChildCount() - 1);
            column.RemoveChild(last);
            last.QueueFree();
        }

        bool any = false;
        for (int index = 0; index < sections.Count; index++)
        {
            var section = sections[index];
            if (column.GetChildCount() <= index + 1)
            {
                var group = new VBoxContainer();
                group.AddChild(CreateLabel(string.Empty, Colors.White));
                column.AddChild(group);
            }

            var category = column.GetChild<VBoxContainer>(index + 1);
            category.GetChild<Label>(0).Text = section.Title;
            var entries = StatisticEntry.From(section).ToArray();
            while (category.GetChildCount() > entries.Length + 1)
            {
                Node last = category.GetChild(category.GetChildCount() - 1);
                category.RemoveChild(last);
                last.QueueFree();
            }

            bool visible = false;
            for (int rowIndex = 0; rowIndex < entries.Length; rowIndex++)
            {
                if (category.GetChildCount() <= rowIndex + 1)
                {
                    var row = new HBoxContainer();
                    row.AddThemeConstantOverride("separation", 16);
                    var label = CreateLabel(string.Empty, Colors.White);
                    label.SizeFlagsStretchRatio = 0.4f;
                    row.AddChild(label);
                    var value = CreateLabel(string.Empty, new Color("69b7ff"));
                    value.SizeFlagsStretchRatio = 0.6f;
                    row.AddChild(value);
                    category.AddChild(row);
                }

                var entry = entries[rowIndex];
                var control = category.GetChild<HBoxContainer>(rowIndex + 1);
                control.GetChild<Label>(0).Text = entry.Label;
                control.GetChild<Label>(1).Text = entry.Value;
                control.Visible = entry.Matches(section.Title, query);
                visible |= control.Visible;
            }

            category.Visible = visible;
            any |= visible;
        }

        column.GetChild<Label>(0).Visible = !any;
    }

    private static Label CreateLabel(string text, Color color)
    {
        var label = new Label
        {
            Text = text,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private void RenderView()
    {
        if (View is not null)
        {
            Render(_global, View.Global, _search.Text);
            Render(_player, View.Player, _search.Text);
        }
    }

}
