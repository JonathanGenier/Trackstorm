using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Client.Statistics;

namespace Trackstorm.Client.Development;

/// <summary>Single full-window owner for developer-tool navigation and input suppression.</summary>
internal sealed partial class DevToolsShell : CanvasLayer
{
    private readonly PanelContainer _root = new() { Visible = false };
    private readonly VBoxContainer _pages = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
    private readonly Dictionary<DevToolsTab, Button> _tabs = new();
    private readonly Dictionary<DevToolsTab, Control> _content = new();
    private readonly Button _close = new() { Text = "Close", CustomMinimumSize = new Vector2(90, 40) };
    private PlayerInputAdapter _input = null!;
    private Control? _previousFocus;

    /// <summary>The existing host-authoritative configuration surface.</summary>
    internal DeveloperOptionsPanel Configs { get; } = new() { Name = "Configs" };
    /// <summary>The existing read-only statistics projection.</summary>
    internal StatisticPanel Stats { get; } = new() { Name = "Stats" };
    /// <summary>The existing read-only event journal projection.</summary>
    internal EventLogPanel Logs { get; } = new() { Name = "Logs" };
    /// <summary>Whether the common shell currently owns developer navigation.</summary>
    internal bool IsOpen => _root.Visible;
    /// <summary>The selected destination while open or most recently closed.</summary>
    internal DevToolsTab SelectedTab { get; private set; }
    /// <summary>Actual viewport coverage for runtime layout verification.</summary>
    internal Rect2 Bounds => _root.GetGlobalRect();

    /// <inheritdoc/>
    public override void _Ready()
    {
        Layer = 30;
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("111418"),
            ContentMarginLeft = 16,
            ContentMarginRight = 16,
            ContentMarginTop = 12,
            ContentMarginBottom = 12,
        });
        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 10);
        _root.AddChild(layout);
        var navigation = new HBoxContainer();
        navigation.AddThemeConstantOverride("separation", 8);
        layout.AddChild(navigation);
        var title = new Label { Text = "DEVTOOLS", CustomMinimumSize = new Vector2(100, 40), VerticalAlignment = VerticalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 20);
        navigation.AddChild(title);
        foreach (DevToolsTab tab in Enum.GetValues<DevToolsTab>())
        {
            var button = new Button { Text = tab.ToString(), ToggleMode = true, CustomMinimumSize = new Vector2(84, 40) };
            button.Pressed += () => Select(tab);
            _tabs.Add(tab, button);
            navigation.AddChild(button);
        }

        navigation.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _close.Pressed += Close;
        navigation.AddChild(_close);
        layout.AddChild(new HSeparator());
        layout.AddChild(_pages);

        var configsScroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        configsScroll.AddChild(Configs);
        AddPage(DevToolsTab.Configs, configsScroll);
        AddPage(DevToolsTab.Stats, Stats);
        AddPage(DevToolsTab.Logs, Logs);
        _tabs[DevToolsTab.Configs].Disabled = !DeveloperTools.Enabled;
        Select(DeveloperTools.Enabled ? DevToolsTab.Configs : DevToolsTab.Stats);
    }

    /// <inheritdoc/>
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        DevToolsTab? requested = KeyMatches(key, Key.F1) && DeveloperTools.Enabled ? DevToolsTab.Configs
            : KeyMatches(key, Key.F2) ? DevToolsTab.Stats
            : KeyMatches(key, Key.F3) ? DevToolsTab.Logs
            : null;
        if (requested is { } tab)
        {
            Open(tab);
            GetViewport().SetInputAsHandled();
        }
        else if (IsOpen && KeyMatches(key, Key.Escape))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        if (_input is not null)
        {
            _input.DiagnosticSuppressed = false;
        }
    }

    /// <summary>Binds the existing input owner before entering the tree.</summary>
    /// <param name="input">Existing local input adapter.</param>
    internal void Initialize(PlayerInputAdapter input) => _input = input;

    /// <summary>Opens the one shell or immediately switches its selected tab.</summary>
    /// <param name="tab">Destination selected by shortcut or navigation.</param>
    internal void Open(DevToolsTab tab)
    {
        if (tab == DevToolsTab.Configs && !DeveloperTools.Enabled)
        {
            return;
        }

        if (!IsOpen)
        {
            _previousFocus = GetViewport().GuiGetFocusOwner();
            _root.Show();
            _input.DiagnosticSuppressed = true;
            _input.Observe();
        }

        Select(tab);
        _tabs[tab].GrabFocus();
    }

    /// <summary>Closes navigation without changing any tab backend or gameplay state.</summary>
    internal void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        _root.Hide();
        _input.DiagnosticSuppressed = false;
        _input.Observe();
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        if (IsInstanceValid(_previousFocus) && _previousFocus!.IsInsideTree() && _previousFocus.IsVisibleInTree())
        {
            _previousFocus.GrabFocus();
        }

        _previousFocus = null;
    }

    private static bool KeyMatches(InputEventKey input, Key key) => input.Keycode == key || input.PhysicalKeycode == key;

    private void AddPage(DevToolsTab tab, Control content)
    {
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        content.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _content.Add(tab, content);
        _pages.AddChild(content);
    }

    private void Select(DevToolsTab tab)
    {
        if (tab == DevToolsTab.Configs && !DeveloperTools.Enabled)
        {
            return;
        }

        SelectedTab = tab;
        foreach ((DevToolsTab candidate, Control content) in _content)
        {
            bool selected = candidate == tab;
            content.Visible = selected;
            _tabs[candidate].ButtonPressed = selected;
        }

        if (tab == DevToolsTab.Stats)
        {
            Stats.RefreshNow();
        }
        else if (tab == DevToolsTab.Logs)
        {
            Logs.RefreshNow();
        }
    }
}
