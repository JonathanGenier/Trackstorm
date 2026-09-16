using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Core.Input;
using Trackstorm.Core.Settings;

namespace Trackstorm.Client.Settings;

/// <summary>Local preference editor with native binding capture and timed display confirmation.</summary>
internal sealed partial class SettingsPanel : CanvasLayer
{
    private readonly PanelContainer _panel = new();
    private readonly Label _status = new();
    private readonly Label _hint = new();
    private readonly ConfirmationDialog _confirmation = new() { Title = "Keep display settings?", OkButtonText = "Keep", CancelButtonText = "Revert" };
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

    /// <summary>Actual diagnostics bounds for runtime layout verification.</summary>
    internal Rect2 DiagnosticsBounds => _hud.GetGlobalRect();

    /// <inheritdoc/>
    public override void _Ready()
    {
        Layer = 3;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(root);
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_background);
        _background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var open = new Button { Text = "Settings", Position = new Vector2(24, 24), CustomMinimumSize = new Vector2(140, 42) };
        _background.AddChild(open);
        open.Pressed += () => SetOpen(!_panel.Visible);
        var hud = new SettingsHud { Name = "Diagnostics", AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -224, OffsetRight = -16, OffsetTop = 8, MouseFilter = Control.MouseFilterEnum.Ignore };
        hud.Initialize(_settings);
        _hud = hud;
        _background.AddChild(hud);
        root.AddChild(_panel);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("24272d"), CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8 });
        _panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _panel.OffsetLeft = 20;
        _panel.OffsetTop = 20;
        _panel.OffsetRight = -20;
        _panel.OffsetBottom = -20;
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "top", "right", "bottom" })
        {
            margin.AddThemeConstantOverride("margin_" + side, 18);
        }

        _panel.AddChild(margin);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        margin.AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(column);
        Heading(column, "Player settings", 26);
        column.AddChild(new Label { Text = "Saved on this device. Display changes require confirmation.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        Heading(column, "Audio");
        Volume(column, "Master", _settings.Current.MasterVolume, value => _settings.Current with { MasterVolume = value });
        Volume(column, "Music", _settings.Current.MusicVolume, value => _settings.Current with { MusicVolume = value });
        Volume(column, "SFX", _settings.Current.SfxVolume, value => _settings.Current with { SfxVolume = value });
        Heading(column, "Display");
        _mode.AddItem("Windowed");
        _mode.AddItem("Fullscreen (desktop resolution)");
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
        var display = new Button { Text = "Preview display change", Disabled = DisplayServer.GetName() == "headless" };
        display.Pressed += PreviewDisplay;
        column.AddChild(display);
        AddChild(_confirmation);
        _confirmation.Confirmed += KeepDisplay;
        _confirmation.Canceled += RevertDisplay;
        Heading(column, "HUD and diagnostics");
        var units = new OptionButton();
        units.AddItem("km/h");
        units.AddItem("mph");
        units.Selected = (int)_settings.Current.SpeedUnit;
        units.ItemSelected += index => _settings.UpdateSettings(_settings.Current with { SpeedUnit = (SpeedUnit)index });
        Row(column, "Speed units", units);
        Toggle(column, "Show FPS", _settings.Current.ShowFps, value => _settings.Current with { ShowFps = value });
        Toggle(column, "Show Ping", _settings.Current.ShowPing, value => _settings.Current with { ShowPing = value });
        Heading(column, "Controls");
        Toggle(column, "Invert steering axis", _settings.Current.InvertSteering, value => _settings.Current with { InvertSteering = value });
        var deadZone = new SpinBox { MinValue = 0, MaxValue = 0.99, Step = 0.01, Value = _settings.Current.DeadZone };
        deadZone.ValueChanged += value => _settings.UpdateSettings(_settings.Current with { DeadZone = value });
        Row(column, "Analog dead zone", deadZone);
        _hint.Text = "Select a binding, then press a key or gamepad control. Shared bindings are allowed.";
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_hint);
        foreach (InputAction action in Enum.GetValues<InputAction>())
        {
            var controls = new HBoxContainer();
            var button = new Button { Name = $"Binding_{action}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true };
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
        column.AddChild(_status);
        var retry = new Button { Text = "Save now / retry" };
        retry.Pressed += () => _settings.Flush();
        column.AddChild(retry);
        var close = new Button { Text = "Done" };
        close.Pressed += () => SetOpen(false);
        column.AddChild(close);
        _settings.SaveStatusChanged += RefreshStatus;
        RefreshStatus();
        _panel.Hide();
    }

    /// <inheritdoc/>
    public override void _Input(InputEvent @event)
    {
        if (_capture is not InputAction action)
        {
            return;
        }

        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            _capture = null;
            _hint.Text = "Binding capture cancelled.";
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
        _settings.CaptureInput();
        RefreshBindings();
        GetViewport().SetInputAsHandled();
    }

    /// <inheritdoc/>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (_panel.Visible && @event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            SetOpen(false);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_preview is not null)
        {
            _seconds -= delta;
            _confirmation.DialogText = $"Keep this display mode? Reverting in {Math.Ceiling(Math.Max(0, _seconds))} seconds.";
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

    private static void Heading(VBoxContainer parent, string text, int size = 20)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        parent.AddChild(label);
    }

    private static void Row(VBoxContainer parent, string text, Control control)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = text, CustomMinimumSize = new Vector2(160, 0) });
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

    private void SetOpen(bool open)
    {
        _panel.Visible = open;
        _background.Visible = !open;
        _input.GameplaySuppressed = open;
        _input.Observe();
        if (open)
        {
            _mode.GrabFocus();
        }
        else
        {
            _capture = null;
            RevertDisplay();
            _settings.Flush();
        }
    }

    private void PreviewDisplay()
    {
        Vector2I size = _resolution.Selected >= 0 && _resolution.Selected < _sizes.Count ? _sizes[_resolution.Selected] : new Vector2I(1280, 720);
        _preview = _settings.Current with { Fullscreen = _mode.Selected == 1, WindowWidth = size.X, WindowHeight = size.Y };
        PlayerSettingsController.ApplyDisplay(_preview);
        _seconds = 15;
        _confirmation.DialogText = "Keep this display mode? Reverting in 15 seconds.";
        _confirmation.PopupCentered();
    }

    private void KeepDisplay()
    {
        if (_preview is PlayerSettings preview)
        {
            _settings.UpdateSettings(_settings.Current with { Fullscreen = preview.Fullscreen, WindowWidth = preview.WindowWidth, WindowHeight = preview.WindowHeight });
            _settings.Flush();
            _preview = null;
        }
    }

    private void RevertDisplay()
    {
        if (_preview is not null)
        {
            PlayerSettingsController.ApplyDisplay(_settings.Current);
            _preview = null;
            _confirmation.Hide();
            RefreshDisplaySelection();
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
