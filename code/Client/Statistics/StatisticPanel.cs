using Godot;
using Trackstorm.Client.Input;

namespace Trackstorm.Client.Statistics;

/// <summary>Plain full-viewport read-only diagnostics, available independently of developer mutation build flags.</summary>
internal sealed partial class StatisticPanel : CanvasLayer
{
    private readonly PanelContainer _root = new() { Visible = false };
    private readonly OptionButton _players = new();
    private readonly VBoxContainer _global = new();
    private readonly VBoxContainer _player = new();
    private readonly Button _close = new() { Text = "Close (F2)", CustomMinimumSize = new Vector2(120, 40) };
    private PlayerInputAdapter _input = null!;
    private Control? _previousFocus;
    private ulong[] _identities = [];
    private ulong _selected;
    private double _refresh;

    /// <summary>Read-only projection supplied by the composition root.</summary>
    internal Func<ulong, StatisticView> Capture { get; set; } = id => RuntimeStatistics.Capture(null, null, id);
    /// <summary>Whether another overlay still needs local input suppression.</summary>
    internal Func<bool> MenuOpen { get; set; } = () => false;
    /// <summary>Current visibility, also used to suspend underlying menu navigation.</summary>
    internal bool IsOpen => _root.Visible;
    /// <summary>Actual viewport coverage for native verification.</summary>
    internal Rect2 Bounds => _root.GetGlobalRect();
    /// <summary>Last displayed projection; carries no gameplay authority.</summary>
    internal StatisticView? View { get; private set; }

    /// <inheritdoc/>
    public override void _Ready()
    {
        Layer = 10;
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("16191e"), ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12 });
        var column = new VBoxContainer();
        _root.AddChild(column);
        var header = new HBoxContainer();
        column.AddChild(header);
        header.AddChild(new Label { Text = "STATISTIC PANEL · Read only", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        header.AddChild(_close);
        _close.Pressed += Close;
        column.AddChild(new Label { Text = "Live at 5 Hz · Simulation continues · Vehicle values are confirmed state", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        column.AddChild(tabs);
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

            Refresh();
        };
    }

    /// <inheritdoc/>
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Keycode: Key.F2, Pressed: true, Echo: false })
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                _previousFocus = GetViewport().GuiGetFocusOwner();
                _root.Show();
                _input.GameplaySuppressed = true;
                _input.Observe();
                Refresh();
                _close.GrabFocus();
            }

            GetViewport().SetInputAsHandled();
        }
        else if (IsOpen && @event is InputEventKey { Keycode: Key.Escape, Pressed: true, Echo: false })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (IsOpen && (_refresh -= delta) <= 0)
        {
            Refresh();
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        _input.GameplaySuppressed = MenuOpen();
    }

    /// <summary>Binds the existing input adapter before entering the tree.</summary>
    /// <param name="input">Existing input owner.</param>
    internal void Initialize(PlayerInputAdapter input) => _input = input;

    /// <summary>Closes navigation without invoking gameplay, settings or persistence commands.</summary>
    internal void Close()
    {
        _root.Hide();
        _input.GameplaySuppressed = MenuOpen();
        _input.Observe();
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        if (IsInstanceValid(_previousFocus) && _previousFocus!.IsInsideTree() && _previousFocus.IsVisibleInTree())
        {
            _previousFocus.GrabFocus();
        }

        _previousFocus = null;
    }

    private static void AddScroll(VBoxContainer parent, VBoxContainer content)
    {
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        parent.AddChild(scroll);
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
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
                column.AddChild(new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore });
            }
        }

        for (int index = 0; index < sections.Count; index++)
        {
            var section = sections[index];
            column.GetChild<Label>(index).Text = section.Title + "\n" + section.Text;
        }
    }

    private void Refresh()
    {
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
}
