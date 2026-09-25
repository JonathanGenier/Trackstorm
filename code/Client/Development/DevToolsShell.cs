using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Client.Settings;
using Trackstorm.Client.Statistics;
using Trackstorm.Core.Input;

namespace Trackstorm.Client.Development;

/// <summary>Single full-window owner for developer-tool navigation and input suppression.</summary>
internal sealed partial class DevToolsShell : CanvasLayer
{
    private static readonly InputAction[] NavigationActions =
    [
        InputAction.MenuCancel, InputAction.Pause, InputAction.MenuUp, InputAction.MenuDown,
        InputAction.MenuLeft, InputAction.MenuRight, InputAction.MenuAccept,
    ];

    private readonly PanelContainer _root = new() { Visible = false };
    private readonly VBoxContainer _pages = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
    private readonly Dictionary<DevToolsTab, Button> _tabs = new();
    private readonly Dictionary<DevToolsTab, Control> _content = new();
    private readonly Button _close = new() { Text = "Close", CustomMinimumSize = new Vector2(90, 40) };
    private readonly Button _forceStart = new() { Name = "ForceStart", Text = "Force Start", Visible = false };
    private readonly Dictionary<InputAction, bool> _menuHeld = new();
    private readonly VBoxContainer _confirmation = new() { Visible = false, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
    private readonly HBoxContainer _navigation = new();
    private readonly HBoxContainer _footer = new();
    private Control? _decisionFocus;
    private PlayerInputAdapter _input = null!;
    private Control? _previousFocus;
    private InputAction? _repeatAction;
    private double _repeatDelay;

    /// <summary>Synchronizes the underlying menu's held input when navigation returns to it.</summary>
    internal Action NavigationClosed { get; set; } = () => { };

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
        var navigation = _navigation;
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
        _forceStart.Pressed += Configs.ForceStart;
        DevToolsButtonPresentation.Configure(_forceStart, "force-start", DevToolsButtonPresentation.Treatment.ForceStart);
        navigation.AddChild(_forceStart);
        _close.Pressed += Close;
        DevToolsButtonPresentation.Configure(_close, "close", DevToolsButtonPresentation.Treatment.Close);
        layout.AddChild(new HSeparator());
        layout.AddChild(_pages);

        var configsMargin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        configsMargin.AddChild(Configs);
        configsMargin.Resized += () =>
        {
            int margin = Math.Max(0, (int)(configsMargin.Size.X - 640) / 2);
            configsMargin.AddThemeConstantOverride("margin_left", margin);
            configsMargin.AddThemeConstantOverride("margin_right", margin);
        };
        AddPage(DevToolsTab.Configs, configsMargin);
        AddPage(DevToolsTab.Stats, Stats);
        AddPage(DevToolsTab.Logs, Logs);
        layout.AddChild(_footer);
        _footer.AddChild(Configs.Footer);
        _footer.Alignment = BoxContainer.AlignmentMode.End;
        Configs.FooterActions.AddChild(_close);
        layout.AddChild(_confirmation);
        _confirmation.AddChild(new Label { Text = "Unapplied Configs changes", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        _confirmation.AddChild(new Label { Text = "Apply these changes before closing, discard them, or stay in DevTools?", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        foreach ((string choice, string icon, DevToolsButtonPresentation.Treatment treatment) in new[]
        {
            ("Apply", "apply", DevToolsButtonPresentation.Treatment.Apply),
            ("Discard", "discard", DevToolsButtonPresentation.Treatment.Cancel),
            ("Stay", "stay", DevToolsButtonPresentation.Treatment.Neutral),
        })
        {
            var button = new Button { Text = choice, CustomMinimumSize = new Vector2(0, 40) };
            button.Pressed += () => ResolveClose(choice);
            DevToolsButtonPresentation.Configure(button, icon, treatment);
            _confirmation.AddChild(button);
        }

        _tabs[DevToolsTab.Configs].Disabled = !DeveloperTools.Enabled;
        Select(DeveloperTools.Enabled ? DevToolsTab.Configs : DevToolsTab.Stats);
    }

    /// <inheritdoc/>
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            DevToolsTab? requested = KeyMatches(key, Key.F1) && DeveloperTools.Enabled ? DevToolsTab.Configs
                : KeyMatches(key, Key.F2) ? DevToolsTab.Stats
                : KeyMatches(key, Key.F3) ? DevToolsTab.Logs
                : null;
            if (requested is { } tab)
            {
                Open(tab);
                GetViewport().SetInputAsHandled();
                return;
            }

            if (IsOpen && KeyMatches(key, Key.Escape))
            {
                Close();
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        bool open = IsOpen;
        bool navigation = SampleNavigation(open);
        if (open && (navigation || @event is InputEventJoypadButton or InputEventJoypadMotion ||
            (@event is InputEventKey && GetViewport().GuiGetFocusOwner() is not LineEdit)))
        {
            // Keep native text entry, but never let ui_* defaults bypass logical remaps or trap controller focus.
            GetViewport().SetInputAsHandled();
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        _forceStart.Visible = DeveloperTools.Enabled && Configs.Session()?.IsDeveloperHost == true;
        SampleNavigation(IsOpen);
        if (IsOpen && _repeatAction is { } repeat)
        {
            _repeatDelay -= delta;
            if (_repeatDelay <= 0)
            {
                Navigate(repeat);
                _repeatDelay = 0.12;
            }
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
        if (_confirmation.Visible)
        {
            return;
        }

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
            SampleNavigation(false);
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

        if (_confirmation.Visible)
        {
            ResolveClose("Stay");
            return;
        }

        if (Configs.HasUnappliedChanges)
        {
            _decisionFocus = GetViewport().GuiGetFocusOwner();
            _pages.Hide();
            _navigation.Hide();
            _footer.Hide();
            _confirmation.Show();
            _repeatAction = null;
            Focusable(_confirmation).Last().GrabFocus();
            return;
        }

        FinishClose();
    }

    private static bool KeyMatches(InputEventKey input, Key key) => input.Keycode == key || input.PhysicalKeycode == key;

    private static IEnumerable<Control> Focusable(Node node)
    {
        foreach (Node child in node.GetChildren(includeInternal: true))
        {
            if (child is Control control && control.IsVisibleInTree() && control.FocusMode == Control.FocusModeEnum.All && control is not BaseButton { Disabled: true })
            {
                yield return control;
            }

            foreach (Control descendant in Focusable(child))
            {
                yield return descendant;
            }
        }
    }

    private void ResolveClose(string choice)
    {
        _confirmation.Hide();
        _pages.Show();
        _navigation.Show();
        _footer.Show();
        if (choice == "Discard")
        {
            Configs.Cancel();
            FinishClose();
        }
        else if (choice == "Apply")
        {
            Select(DevToolsTab.Configs);
            if (Configs.Apply())
            {
                FinishClose();
            }
            else
            {
                _tabs[DevToolsTab.Configs].GrabFocus();
            }
        }
        else if (IsInstanceValid(_decisionFocus) && _decisionFocus!.IsVisibleInTree())
        {
            _decisionFocus.GrabFocus();
        }

        _decisionFocus = null;
    }

    private void FinishClose()
    {
        _root.Hide();
        _repeatAction = null;
        NavigationClosed();
        _input.DiagnosticSuppressed = false;
        _input.Observe();
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        if (IsInstanceValid(_previousFocus) && _previousFocus!.IsInsideTree() && _previousFocus.IsVisibleInTree())
        {
            _previousFocus.GrabFocus();
        }

        _previousFocus = null;
    }

    private bool SampleNavigation(bool dispatch)
    {
        bool active = false;
        bool routed = false;
        foreach (InputAction action in NavigationActions)
        {
            bool held = _input.Enabled && _input.Bindings.Strength(action, _input.DeadZone, ignoreTextKeys: SelectedTab == DevToolsTab.Stats && Stats.EditingSearch) > 0.5f;
            bool previous = _menuHeld.GetValueOrDefault(action);
            _menuHeld[action] = held;
            active |= held;
            if (!held || !dispatch)
            {
                if (_repeatAction == action)
                {
                    _repeatAction = null;
                }
            }
            else if (!previous && !routed)
            {
                routed = true;
                Navigate(action);
                if (IsOpen && action is InputAction.MenuUp or InputAction.MenuDown or InputAction.MenuLeft or InputAction.MenuRight)
                {
                    _repeatAction = action;
                    _repeatDelay = 0.4;
                }
            }
        }

        return active;
    }

    private void Navigate(InputAction action)
    {
        if (action is InputAction.MenuCancel or InputAction.Pause)
        {
            Close();
            return;
        }

        MenuFocusNavigation.Navigate(action, Focusable(_root).ToArray(), GetViewport().GuiGetFocusOwner());
    }

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
        Configs.SetConfigurationFooterVisible(tab == DevToolsTab.Configs && Configs.CanConfigure);
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
