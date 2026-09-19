using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Core.Input;
using Trackstorm.Core.Settings;

namespace Trackstorm.Client.Settings;

/// <summary>Local preference editor with native binding capture and timed display confirmation.</summary>
internal sealed partial class SettingsPanel : CanvasLayer
{
    private readonly MenuPresentation _panel = new() { Size = new Vector2(640, 680) };
    private readonly MenuNavigation _navigation = new();
    private readonly Dictionary<MenuPage, VBoxContainer> _pages = new();
    private readonly Control _root = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly ColorRect _shade = new() { Color = new Color(0, 0, 0, 0.48f) };
    private readonly Label _title = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly ScrollContainer _scroll = new() { Position = new Vector2(48, 180), Size = new Vector2(544, 378), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
    private readonly Button _back = new() { Text = "Back", Position = new Vector2(48, 570), Size = new Vector2(544, 44) };
    private readonly Button _openSettings = new() { Text = "Settings", Position = new Vector2(24, 24), CustomMinimumSize = new Vector2(140, 42) };
    private readonly Dictionary<InputAction, bool> _menuHeld = new();
    private readonly Label _status = new();
    private readonly Label _hint = new();
    private readonly VBoxContainer _confirmation = new() { Position = new Vector2(48, 190), Size = new Vector2(544, 350), Visible = false };
    private readonly Label _confirmText = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Button _keep = new() { Text = "Keep" };
    private readonly Button _revert = new() { Text = "Revert" };
    private readonly Dictionary<InputAction, Button> _bindings = new();
    private readonly OptionButton _mode = new();
    private readonly OptionButton _resolution = new();
    private readonly List<Vector2I> _sizes = new();
    private readonly Control _background = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
    private PlayerSettingsController _settings = null!;
    private PlayerInputAdapter _input = null!;
    private InputAction? _capture;
    private PlayerSettings? _preview;
    private double _seconds;
    private SettingsHud _hud = null!;
    private bool _wasArena;
    private bool _escapeHeld;
    private double _repeatDelay;
    private InputAction? _repeatAction;

    /// <summary>Actual diagnostics bounds for runtime layout verification.</summary>
    internal Rect2 DiagnosticsBounds => _hud.GetGlobalRect();

    /// <summary>Arena presence supplied by composition, never a match-phase or player-count predicate.</summary>
    internal Func<bool> ArenaAvailable { get; set; } = () => false;
    /// <summary>Observation overlay owns navigation while visible; simulation remains active.</summary>
    internal Func<bool> DiagnosticOverlayOpen { get; set; } = () => false;
    /// <summary>Routes the Settings category to the single shared developer-tools shell.</summary>
    internal Action OpenDeveloperTools { get; set; } = () => { };
    /// <summary>The existing session owner's leave path.</summary>
    internal Action LeaveToMainMenu { get; set; } = () => { };
    /// <summary>The application owner's cleanup-aware exit request.</summary>
    internal Action QuitApplication { get; set; } = () => { };
    /// <summary>Current local navigation page for runtime verification.</summary>
    internal MenuPage CurrentPage => _navigation.Page;
    /// <summary>Rendered frame bounds for resolution checks.</summary>
    internal Rect2 MenuBounds => _panel.GetGlobalRect();
    /// <summary>Pending exit progress/failure; null during normal use.</summary>
    internal Func<string?> ExitStatus { get; set; } = () => null;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Layer = 3;
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_background);
        _background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _background.AddChild(_openSettings);
        _openSettings.Pressed += () =>
        {
            _navigation.Open(false);
            ShowPage();
        };
        _hud = new SettingsHud { Name = "Diagnostics", AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -224, OffsetRight = -16, OffsetTop = 8, MouseFilter = Control.MouseFilterEnum.Ignore };
        _hud.Initialize(_settings);
        _background.AddChild(_hud);
        _root.AddChild(_shade);
        _shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_panel);
        _panel.Theme = MenuPresentation.CreateTheme();
        _title.Position = new Vector2(60, 107);
        _title.Size = new Vector2(520, 48);
        _title.AddThemeFontSizeOverride("font_size", 32);
        _panel.AddChild(_title);
        _panel.AddChild(_scroll);
        _panel.AddChild(_back);
        _back.Pressed += Back;
        _status.Position = new Vector2(48, 620);
        _status.Size = new Vector2(544, 36);
        _status.AddThemeFontSizeOverride("font_size", 15);
        _panel.AddChild(_status);
        var column = Page(MenuPage.Game);
        AddButton(column, "Back to Game", Close);
        AddButton(column, "Settings", () => Select(MenuPage.Settings));
        AddButton(column, "Leave to Main Menu", () =>
        {
            Close();
            LeaveToMainMenu();
        });
        AddButton(column, "Quit", () => QuitApplication());
        column = Page(MenuPage.Settings);
        foreach (MenuPage category in Enum.GetValues<MenuPage>().Where(page => page >= MenuPage.Audio))
        {
            AddButton(column, Title(category), () => Select(category));
        }

        AddButton(column, "Developer Options", () => OpenDeveloperTools());

        var retry = new Button { Text = "Save now / retry" };
        retry.Pressed += () => _settings.Flush();
        column.AddChild(retry);
        column = Page(MenuPage.Audio);
        Volume(column, "Master", _settings.Current.MasterVolume, value => _settings.Current with { MasterVolume = value });
        Volume(column, "Music", _settings.Current.MusicVolume, value => _settings.Current with { MusicVolume = value });
        Volume(column, "SFX", _settings.Current.SfxVolume, value => _settings.Current with { SfxVolume = value });
        column = Page(MenuPage.Video);
        _mode.AddItem("Windowed");
        _mode.AddItem("Fullscreen");
        _mode.Selected = _settings.Current.Fullscreen ? 1 : 0;
        Row(column, "Mode", _mode);
        Vector2I screen = DisplayServer.GetName() == "headless" ? new Vector2I(1920, 1080) : DisplayServer.ScreenGetUsableRect().Size;
        foreach (Vector2I size in new[] { new Vector2I(640, 360), new Vector2I(960, 540), new Vector2I(1280, 720), new Vector2I(1600, 900), new Vector2I(1920, 1080), new Vector2I(2560, 1440), new Vector2I(3840, 2160), new Vector2I(_settings.Current.WindowWidth, _settings.Current.WindowHeight) }.Distinct().OrderBy(size => size.X))
        {
            if (size.X <= screen.X && size.Y <= screen.Y)
            {
                _sizes.Add(size);
                _resolution.AddItem($"{size.X} × {size.Y}");
            }
        }

        RefreshDisplaySelection();
        _mode.ItemSelected += index => _resolution.Disabled = index == 1 || _sizes.Count == 0;
        Row(column, "Window resolution", _resolution);
        var displayHint = new Label { Text = "Fullscreen uses the desktop resolution.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        displayHint.AddThemeFontSizeOverride("font_size", 16);
        column.AddChild(displayHint);
        var display = new Button { Text = "Preview display change", Disabled = DisplayServer.GetName() == "headless" };
        display.Pressed += PreviewDisplay;
        column.AddChild(display);
        _panel.AddChild(_confirmation);
        _confirmation.AddChild(_confirmText);
        _confirmation.AddChild(_keep);
        _confirmation.AddChild(_revert);
        _keep.Pressed += KeepDisplay;
        _revert.Pressed += RevertDisplay;
        column = Page(MenuPage.Gameplay);
        var units = new OptionButton();
        units.AddItem("km/h");
        units.AddItem("mph");
        units.Selected = (int)_settings.Current.SpeedUnit;
        units.ItemSelected += index => _settings.UpdateSettings(_settings.Current with { SpeedUnit = (SpeedUnit)index });
        Row(column, "Speed units", units);
        column = Page(MenuPage.Interface);
        Toggle(column, "Show FPS", _settings.Current.ShowFps, value => _settings.Current with { ShowFps = value });
        Toggle(column, "Show Ping", _settings.Current.ShowPing, value => _settings.Current with { ShowPing = value });
        column = Page(MenuPage.Controls);
        Toggle(column, "Invert steering axis", _settings.Current.InvertSteering, value => _settings.Current with { InvertSteering = value });
        _hint.Text = "Select a binding, then press a key or gamepad control. Shared bindings are allowed.";
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_hint);
        foreach (InputAction action in Enum.GetValues<InputAction>())
        {
            var controls = new HBoxContainer();
            var button = new Button { Name = $"Binding_{action}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true };
            button.AddThemeFontSizeOverride("font_size", 17);
            _bindings[action] = button;
            button.Pressed += () =>
            {
                _capture = action;
                _hint.Text = $"{action}: press a key or gamepad control. Replaces that device type's bindings. Escape cancels.";
            };
            controls.AddChild(button);
            var clear = new Button { Text = "Clear", TooltipText = "Unbind this action" };
            clear.Pressed += () =>
            {
                _input.Bindings.Replace(action, []);
                _settings.CaptureInput();
                RefreshBindings();
            };
            controls.AddChild(clear);
            Row(column, action == InputAction.Brake ? "Brake / reverse" : action == InputAction.Drift ? "Handbrake" : System.Text.RegularExpressions.Regex.Replace(action.ToString(), "([a-z])([A-Z])", "$1 $2"), controls);
        }

        RefreshBindings();
        var restore = new Button { Text = "Restore default bindings" };
        restore.Pressed += () =>
        {
            _capture = null;
            _input.Bindings.RestoreDefaults();
            _settings.CaptureInput();
            RefreshBindings();
            _hint.Text = "Default keyboard and gamepad bindings restored.";
        };
        column.AddChild(restore);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _settings.SaveStatusChanged += RefreshStatus;
        RefreshStatus();
        _root.Resized += Layout;
        Layout();
        ShowPage();
    }

    /// <inheritdoc/>
    public override void _Input(InputEvent @event)
    {
        if (DiagnosticOverlayOpen())
        {
            SampleNavigation(false);
            return;
        }

        if (_capture is not InputAction action)
        {
            bool open = CurrentPage != MenuPage.Closed;
            if (@event is InputEventKey { Keycode: Key.Escape } escape)
            {
                if (escape.Pressed && !escape.Echo && !_escapeHeld)
                {
                    if (open)
                    {
                        Back();
                    }
                    else if (ArenaAvailable())
                    {
                        _navigation.Open(true);
                        ShowPage();
                    }
                }

                _escapeHeld = escape.Pressed;
                SampleNavigation(false);
            }
            else
            {
                SampleNavigation(true, @event is InputEventJoypadButton or InputEventJoypadMotion);
            }

            if ((open || CurrentPage != MenuPage.Closed) && @event is InputEventKey or InputEventJoypadButton or InputEventJoypadMotion)
            {
                // Native ui_* defaults must not double-activate or bypass remapped logical controls.
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            _capture = null;
            _hint.Text = "Binding capture cancelled.";
            _escapeHeld = true;
            SampleNavigation(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        InputEvent? candidate = @event switch
        {
            InputEventKey key when key.Pressed && !key.Echo && !key.CtrlPressed && !key.AltPressed && !key.ShiftPressed && !key.MetaPressed && key.PhysicalKeycode != Key.None => new InputEventKey { PhysicalKeycode = key.PhysicalKeycode },
            InputEventMouseButton mouse when mouse.Pressed && !mouse.CtrlPressed && !mouse.AltPressed && !mouse.ShiftPressed && !mouse.MetaPressed => new InputEventMouseButton { ButtonIndex = mouse.ButtonIndex },
            InputEventJoypadButton button when button.Pressed => new InputEventJoypadButton { Device = button.Device, ButtonIndex = button.ButtonIndex },
            InputEventJoypadMotion axis when Math.Abs(axis.AxisValue) > 0.6 => new InputEventJoypadMotion { Device = axis.Device, Axis = axis.Axis, AxisValue = Math.Sign(axis.AxisValue) },
            _ => null,
        };
        if (candidate is null)
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        using (candidate)
        {
            InputAction[] conflicts = _input.Bindings.FindConflicts(candidate).Where(other => other != action).ToArray();
            InputEvent[] existing = _input.Bindings.CopyBindings(action);
            try
            {
                _input.Bindings.Replace(action, [.. existing.Where(binding => (binding is InputEventKey or InputEventMouseButton) != (candidate is InputEventKey or InputEventMouseButton)), candidate]);
            }
            finally
            {
                foreach (InputEvent binding in existing)
                {
                    binding.Dispose();
                }
            }

            _hint.Text = conflicts.Length == 0 ? "Binding updated." : "Binding updated. Also used by: " + string.Join(", ", conflicts);
        }

        _capture = null;
        SampleNavigation(false);
        _settings.CaptureInput();
        RefreshBindings();
        GetViewport().SetInputAsHandled();
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        bool arena = ArenaAvailable();
        if (ExitStatus() is { } exitStatus)
        {
            if (CurrentPage != MenuPage.Game)
            {
                _navigation.Open(true);
                ShowPage();
            }

            _status.Visible = true;
            _status.Text = exitStatus;
            foreach (Button button in _pages[MenuPage.Game].GetChildren().OfType<Button>())
            {
                button.Disabled = button.Text != "Quit";
            }

            if (GetViewport().GuiGetFocusOwner() is not Button { Text: "Quit" })
            {
                _pages[MenuPage.Game].GetChildren().OfType<Button>().Single(button => button.Text == "Quit").GrabFocus();
            }
        }

        _openSettings.Visible = !arena && CurrentPage == MenuPage.Closed;
        if (_wasArena && !arena)
        {
            Close();
        }

        _wasArena = arena;
        if (DiagnosticOverlayOpen())
        {
            SampleNavigation(false);
        }
        else if (_capture is null)
        {
            SampleNavigation(true);
            if (_repeatAction is { } repeat && CurrentPage != MenuPage.Closed)
            {
                _repeatDelay -= delta;
                if (_repeatDelay <= 0)
                {
                    Navigate(repeat);
                    _repeatDelay = 0.12;
                }
            }
        }

        if (_preview is not null)
        {
            _seconds -= delta;
            _confirmText.Text = $"Keep this display mode? Reverting in {Math.Ceiling(Math.Max(0, _seconds))} seconds.";
            if (_seconds <= 0)
            {
                RevertDisplay();
            }
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        _settings.SaveStatusChanged -= RefreshStatus;
        _input.GameplaySuppressed = false;
    }

    /// <summary>Receives the existing preference and input owners.</summary>
    /// <param name="settings">Preference service.</param>
    /// <param name="input">Existing input owner.</param>
    internal void Initialize(PlayerSettingsController settings, PlayerInputAdapter input)
    {
        _settings = settings;
        _input = input;
    }

    /// <summary>Connects authoritative vehicle speed to the existing preference-aware HUD.</summary>
    /// <param name="metresPerSecond">Unconverted Core speed.</param>
    /// <param name="connection">Current neutral connection diagnostics.</param>
    internal void SetVehicleTelemetry(float metresPerSecond, Networking.ConnectionDiagnostic connection = default) => _hud.SetTelemetry(metresPerSecond, connection);

    /// <summary>Suppresses only the duplicate speed line; independent FPS and ping preferences remain intact.</summary>
    /// <param name="visible">Whether the combat HUD is providing speed.</param>
    internal void SetCombatHudVisible(bool visible) => _hud.SpeedVisible = !visible;

    private static void Row(VBoxContainer parent, string text, Control control)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = text, CustomMinimumSize = new Vector2(190, 0) });
        control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(control);
        parent.AddChild(row);
    }

    private void Volume(VBoxContainer parent, string title, double value, Func<double, PlayerSettings> change)
    {
        var row = new HBoxContainer();
        var slider = new HSlider { MinValue = 0, MaxValue = 100, Step = 1, Value = value * 100, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var percent = new Label { Text = $"{value * 100:0}%", CustomMinimumSize = new Vector2(50, 0) };
        slider.ValueChanged += amount =>
        {
            percent.Text = $"{amount:0}%";
            _settings.UpdateSettings(change(amount / 100));
        };
        row.AddChild(slider);
        row.AddChild(percent);
        Row(parent, title, row);
    }

    private void Toggle(VBoxContainer parent, string text, bool value, Func<bool, PlayerSettings> change)
    {
        var toggle = new CheckButton { Text = text, ButtonPressed = value };
        toggle.Toggled += enabled => _settings.UpdateSettings(change(enabled));
        parent.AddChild(toggle);
    }

    private void PreviewDisplay()
    {
        Vector2I size = _resolution.Selected >= 0 && _resolution.Selected < _sizes.Count ? _sizes[_resolution.Selected] : new Vector2I(1280, 720);
        _preview = _settings.Current with { Fullscreen = _mode.Selected == 1, WindowWidth = size.X, WindowHeight = size.Y };
        PlayerSettingsController.ApplyDisplay(_preview);
        _seconds = 15;
        _confirmText.Text = "Keep this display mode? Reverting in 15 seconds.";
        _scroll.Hide();
        _confirmation.Show();
        _keep.GrabFocus();
    }

    private void KeepDisplay()
    {
        if (_preview is PlayerSettings preview)
        {
            _settings.UpdateSettings(_settings.Current with { Fullscreen = preview.Fullscreen, WindowWidth = preview.WindowWidth, WindowHeight = preview.WindowHeight });
            _settings.Flush();
            _preview = null;
            _confirmation.Hide();
            _scroll.Show();
            if (_mode.IsVisibleInTree())
            {
                _mode.GrabFocus();
            }
        }
    }

    private void RevertDisplay()
    {
        if (_preview is not null)
        {
            PlayerSettingsController.ApplyDisplay(_settings.Current);
            _preview = null;
            _confirmation.Hide();
            _scroll.Show();
            RefreshDisplaySelection();
            if (_mode.IsVisibleInTree())
            {
                _mode.GrabFocus();
            }
        }
    }

    private void RefreshDisplaySelection()
    {
        _mode.Selected = _settings.Current.Fullscreen ? 1 : 0;
        int index = _sizes.IndexOf(new Vector2I(_settings.Current.WindowWidth, _settings.Current.WindowHeight));
        _resolution.Selected = index >= 0 ? index : 0;
        _resolution.Disabled = _settings.Current.Fullscreen || _sizes.Count == 0;
    }

    private void RefreshBindings()
    {
        foreach ((InputAction action, Button button) in _bindings)
        {
            InputEvent[] bindings = _input.Bindings.CopyBindings(action);
            button.Text = bindings.Length == 0 ? "Unbound — select to bind" : string.Join(" / ", bindings.Select(InputBindingPreferences.Describe));
            button.TooltipText = string.Join(" / ", bindings.Select(binding => binding.AsText()));
            foreach (InputEvent binding in bindings)
            {
                binding.Dispose();
            }
        }
    }

    private void RefreshStatus() => _status.Text = _settings.SaveStatus;
}
