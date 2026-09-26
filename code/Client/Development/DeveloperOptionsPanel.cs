using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Development;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Settings;

namespace Trackstorm.Client.Development;

/// <summary>Single Settings/F1 surface; only Apply commits its editor draft through current authority.</summary>
internal sealed partial class DeveloperOptionsPanel : VBoxContainer
{
    private readonly VBoxContainer _host = new() { Visible = false, SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly VBoxContainer _practice = new() { Visible = false };
    private ConfigsAccordion _network = null!;
    private readonly Label _availability = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _status = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _feedback = new() { Name = "ConfigFeedback", Visible = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly HBoxContainer _footerActions = new();
    private readonly HBoxContainer _configurationButtons = new();
    private readonly LineEdit _search = new() { Name = "ConfigSearch", PlaceholderText = "Search settings by label or category…", ClearButtonEnabled = true };
    private readonly Label _noResults = new() { Text = "No settings match your search.", Visible = false };
    private readonly Dictionary<string, Control> _editors = new(StringComparer.Ordinal);
    private readonly List<(ConfigsAccordion Section, List<(Label Label, Control Editor, string Search)> Rows)> _sections = new();
    private readonly DeveloperOptionsDraft _draft = new();
    private readonly List<SpinBox> _simulation = new();
    private readonly double[] _appliedSimulation = new double[5];
    private object? _owner;
    private ulong _revision;
    private ulong _authorityEpoch;
    private bool _refreshing;
    private double _elapsed;
    private double _feedbackSeconds;
    private DeveloperOptionsFeedbackState _feedbackState;
    private Button _resetButton = null!;
    private bool _configurationFooterVisible;
    private bool _hostAuthority;
    private bool _tireSaveFailed;
    private readonly Dictionary<string, LineEdit> _tireEditors = new();
    private readonly HashSet<Control> _localSections = new();
    internal Settings.PlayerSettingsController? LocalSettings { get; set; }
    private TireEffectSettings _tireBaseline = TireEffectSettings.Defaults;
    internal bool CanConfigure => Session()?.IsDeveloperHost == true || LocalSettings is not null;
    private bool TireDirty => _tireEditors.Any(pair => !float.TryParse(pair.Value.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value) || value != _tireBaseline[pair.Key]);

    /// <summary>Current production session supplied by composition.</summary>
    internal Func<DevelopmentSession?> Session { get; set; } = () => null;
    /// <summary>Current local practice authority supplied by composition.</summary>
    internal Func<VehicleArena?> Practice { get; set; } = () => null;
    /// <summary>Configuration actions mounted in the shell's fixed footer.</summary>
    internal HBoxContainer Footer { get; } = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    /// <summary>Right-side action row extended by the shell-owned Close button.</summary>
    internal HBoxContainer FooterActions => _footerActions;
    /// <summary>Whether closing needs an explicit decision, including invalid editor text.</summary>
    internal bool HasUnappliedChanges => _draft.IsDirty || NetworkDirty || TireDirty;
    private bool NetworkDirty => _simulation.Where((value, index) => value.Value != _appliedSimulation[index]).Any();

    /// <inheritdoc/>
    public override void _Ready()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Theme = new Theme();
        Theme.SetColor("font_color", "Label", Colors.White);
        _availability.AddThemeFontSizeOverride("font_size", 16);
        _status.AddThemeFontSizeOverride("font_size", 16);
        AddChild(_availability);
        AddChild(_host);
        _host.AddChild(_search);
        _search.TextChanged += _ => Filter();
        var legend = new Label { Text = "Blue: game default · Red: modified · Changes are staged until Apply Settings", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        legend.AddThemeFontSizeOverride("font_size", 14);
        _host.AddChild(legend);
        var scroll = new ScrollContainer { Name = "SettingsScroll", SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        _host.AddChild(scroll);
        var settings = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(settings);
        settings.AddChild(_noResults);
        foreach (var group in GameplayOptions.All.GroupBy(option => option.Group))
        {
            var section = new ConfigsAccordion(group.Key, () => ResetCategory(group.Key));
            settings.AddChild(section);
            if (group.Key is "Item categories" or "Item spawns")
            {
                section.Body.AddChild(new Label
                {
                    Text = group.Key == "Item categories"
                        ? "Server-wide category targets (default 2:1:1). Each player retains independent category history."
                        : "Relative weights within each category: 2 gives twice the chance of 1; 0 excludes the item. Apply Settings affects future rolls for everyone and preserves held items and history.",
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                });
            }
            var rows = ConfigurationRows(section.Body);
            var entries = new List<(Label Label, Control Editor, string Search)>();
            _sections.Add((section, entries));
            foreach (var option in group)
            {
                string text = System.Text.RegularExpressions.Regex.Replace(option.Label, "([a-z])([A-Z])", "$1 $2");
                var label = ConfigurationLabel(text);
                rows.AddChild(label);
                Control editor;
                if (option.Key == "environment.preset")
                {
                    var presets = new OptionButton();
                    foreach (var preset in Enum.GetValues<EnvironmentPreset>())
                    {
                        presets.AddItem(Arenas.EnvironmentPresentation.DisplayName(preset), (int)preset);
                    }
                    presets.ItemSelected += index => StageValue(option.Key, presets.GetItemId((int)index).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    editor = presets;
                }
                else if (option.Boolean)
                {
                    var toggle = new CheckButton();
                    toggle.Toggled += value => StageValue(option.Key, value ? "1" : "0");
                    editor = toggle;
                }
                else
                {
                    var number = new LineEdit { CustomMinimumSize = new Vector2(150, 36), MaxLength = 24, SelectAllOnFocus = true };
                    number.TextChanged += value => StageValue(option.Key, value);
                    editor = number;
                }

                editor.CustomMinimumSize = new Vector2(150, 36);
                editor.Name = option.Key.Replace('.', '_');
                editor.TooltipText = option.Label + (option.Label.EndsWith("Ticks", StringComparison.Ordinal) ? " (60 ticks = 1 second)" : string.Empty);
                _editors.Add(option.Key, editor);
                rows.AddChild(editor);
                entries.Add((label, editor, $"{group.Key} {text} {option.Label} {option.Key}"));
            }
        }

        if (LocalSettings is not null)
        {
            _tireBaseline = LocalSettings.Current.TireEffects;
            foreach (var group in TireEffectSettings.Options.GroupBy(option => option.Group))
            {
                var section = new ConfigsAccordion(group.Key, () => ResetTireCategory(group.Key));
                settings.AddChild(section);
                _localSections.Add(section);
                var rows = ConfigurationRows(section.Body);
                var entries = new List<(Label Label, Control Editor, string Search)>();
                _sections.Add((section, entries));
                foreach (var option in group)
                {
                    var label = ConfigurationLabel(option.Label);
                    var editor = new LineEdit { Name = option.Key.Replace('.', '_'), MaxLength = 24, SelectAllOnFocus = true, CustomMinimumSize = new Vector2(150, 36), TooltipText = $"{option.Key}: {option.Minimum}–{option.Maximum}. Local graphics only. Changes affect new emissions; budget changes clear existing marks." };
                    _tireEditors.Add(option.Key, editor);
                    rows.AddChild(label); rows.AddChild(editor);
                    entries.Add((label, editor, $"{group.Key} {option.Label} {option.Key}"));
                    editor.TextChanged += _ => { ColorTireValues(); UpdateFeedback(); };
                }
            }
            RenderTireValues(_tireBaseline);
        }
        _network = new ConfigsAccordion("Local network simulation", ResetNetworkCategory);
        settings.AddChild(_network);
        _network.Body.AddChild(new Label { Text = "Direct-IP only, process-wide, not saved.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        string[] names = ["Latency (ms)", "Jitter (ms)", "Loss (%)", "Reorder (%)", "Reorder delay (ms)"];
        int[] maxima = [5000, 1000, 100, 100, 5000];
        var networkRows = ConfigurationRows(_network.Body);
        var networkEntries = new List<(Label Label, Control Editor, string Search)>();
        _sections.Add((_network, networkEntries));
        for (int i = 0; i < names.Length; i++)
        {
            var label = ConfigurationLabel(names[i]);
            networkRows.AddChild(label);
            var value = new SpinBox { MinValue = 0, MaxValue = maxima[i], Step = 1, UpdateOnTextChanged = true, CustomMinimumSize = new Vector2(150, 36) };
            value.ValueChanged += _ =>
            {
                ColorNetworkValues();
                _status.Text = string.Empty;
                UpdateFeedback();
            };
            _simulation.Add(value);
            networkRows.AddChild(value);
            networkEntries.Add((label, value, "Local network simulation " + names[i]));
        }

        _resetButton = Button(Footer, "Reset to Defaults", Reset);
        DevToolsButtonPresentation.Configure(_resetButton, "reset", DevToolsButtonPresentation.Treatment.Reset);
        Footer.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var feedbackActions = new VBoxContainer();
        Footer.AddChild(feedbackActions);
        _feedback.AddThemeFontSizeOverride("font_size", 14);
        feedbackActions.AddChild(_feedback);
        feedbackActions.AddChild(_footerActions);
        _footerActions.AddChild(_configurationButtons);
        var apply = Button(_configurationButtons, "Apply Settings", () => Apply());
        DevToolsButtonPresentation.Configure(apply, "apply", DevToolsButtonPresentation.Treatment.Apply);
        var cancel = Button(_configurationButtons, "Cancel", Cancel);
        DevToolsButtonPresentation.Configure(cancel, "cancel", DevToolsButtonPresentation.Treatment.Cancel);
        AddChild(_practice);
        Button(_practice, "Reset practice arena", () => Practice()?.ResetVehicles());
        Button(_practice, "Detonate nearby", () =>
        {
            if (Practice() is { } arena)
            {
                arena.Explode(arena.Player.GlobalPosition + new Vector3(-2, -0.2f, 0.5f));
            }
        });
        AddChild(_status);
        RenderValues();
        Filter();
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        var session = Session();
        bool hostAuthority = session?.IsDeveloperHost == true;
        if (_hostAuthority != hostAuthority) { _hostAuthority = hostAuthority; Filter(); }
        _host.Visible = DeveloperTools.Enabled && CanConfigure;
        SetConfigurationFooterVisible(_host.Visible && IsVisibleInTree());
        _practice.Visible = DeveloperTools.Enabled && Practice() is not null;
        if (HasUnappliedChanges)
        {
            if (_feedbackState != DeveloperOptionsFeedbackState.Unsaved)
            {
                SetFeedback(DeveloperOptionsFeedbackState.Unsaved);
            }
        }
        else if (_feedbackState == DeveloperOptionsFeedbackState.Unsaved)
        {
            SetFeedback(DeveloperOptionsFeedbackState.None);
        }
        else if (_feedbackState is DeveloperOptionsFeedbackState.Applied or DeveloperOptionsFeedbackState.Discarded && _feedbackSeconds > 0)
        {
            _feedbackSeconds -= delta;
            if (_feedbackSeconds <= 0)
            {
                SetFeedback(DeveloperOptionsFeedbackState.None);
            }
        }

        _elapsed += delta;
        if (_elapsed < 0.25)
        {
            return;
        }

        _elapsed = 0;
        Filter();
        _availability.Text = !DeveloperTools.Enabled ? "Developer tools are disabled in this build."
            : session?.IsDeveloperHost == true ? (NetworkSimulationControl.Supported(session?.Gateway)
                ? "Host controls · Apply validates, synchronizes and saves gameplay tuning"
                : "Host controls · local network simulation unavailable for this transport")
            : LocalSettings is not null ? "Local tire graphics · Apply saves on this device. Host a session for gameplay tuning."
            : "Configs require current host authority. Host a session to access tuning and developer actions.";
        object? owner = (object?)session?.Arena?.Driver ?? session?.Lobby;
        ulong revision = session?.Arena?.Driver.Configuration.Revision ?? session?.Lobby?.Authority?.Configuration.Revision ?? 0;
        ulong epoch = session?.Lobby?.State?.AuthorityEpoch ?? 0;
        if (session?.IsDeveloperHost == true && (!ReferenceEquals(_owner, owner) || epoch != _authorityEpoch || (!_draft.IsDirty && revision != _revision)))
        {
            RefreshValues();
        }

        if (_host.Visible)
        {
            _owner = owner;
            _revision = revision;
            _authorityEpoch = epoch;
        }
    }

    /// <summary>Commits edited values once through current host authority.</summary>
    /// <returns>Whether all requested values were accepted.</returns>
    internal bool Apply()
    {
        var tireEdits = new Dictionary<string, double>();
        foreach (var (key, editor) in _tireEditors)
        {
            if (!double.TryParse(editor.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
            {
                _status.Text = $"Invalid local graphics value: {key}";
                return false;
            }
            tireEdits[key] = value;
        }
        if (!_tireBaseline.TryApply(tireEdits, out var tire, out string tireError)) { _status.Text = tireError; return false; }
        if (Session()?.IsDeveloperHost != true && LocalSettings is not null)
        {
            return ApplyTireValues(tire);
        }
        if (!_draft.TryGetEdits(out var edits, out string error))
        {
            _status.Text = error;
            UpdateFeedback();
            return false;
        }

        var session = Session();
        error = "Host authority unavailable.";
        if (session?.ConfigureDeveloperOptions(edits, out error) != true)
        {
            _status.Text = error;
            UpdateFeedback();
            return false;
        }

        RefreshValues();
        if (NetworkDirty)
        {
            var simulation = new NetworkSimulation((int)_simulation[0].Value, (int)_simulation[1].Value, (float)_simulation[2].Value, (float)_simulation[3].Value, (int)_simulation[4].Value);
            if (!NetworkSimulationControl.TryApply(session.Gateway, session.IsDeveloperHost, simulation))
            {
                _status.Text = "Gameplay tuning applied. Local network simulation unavailable; its edits remain staged.";
                UpdateFeedback();
                return false;
            }

            for (int i = 0; i < _simulation.Count; i++)
            {
                _appliedSimulation[i] = _simulation[i].Value;
            }
        }

        _status.Text = session.DeveloperSettings?.Status ?? "Tuning applied; persistence unavailable.";
        SetFeedback(DeveloperOptionsFeedbackState.Applied);
        if (session.DeveloperSettings?.LastSaveSucceeded == false)
        {
            return false;
        }

        if (LocalSettings is not null && (TireDirty || _tireSaveFailed))
        {
            if (!ApplyTireValues(tire)) { return false; }
            _status.Text = session.DeveloperSettings?.Status ?? "Local graphics saved; host persistence unavailable.";
        }
        return true;
    }

    /// <summary>Discards unapplied edits and restores currently effective values.</summary>
    internal void Cancel()
    {
        _tireBaseline = LocalSettings?.Current.TireEffects ?? TireEffectSettings.Defaults;
        RenderTireValues(_tireBaseline);
        _draft.Discard(Session()?.DeveloperConfiguration ?? GameplayConfiguration.HostedDefaults);
        RenderValues();
        for (int i = 0; i < _simulation.Count; i++)
        {
            _simulation[i].Value = _appliedSimulation[i];
        }

        _status.Text = "Unapplied changes discarded.";
        SetFeedback(DeveloperOptionsFeedbackState.Discarded);
    }

    /// <summary>Invokes the existing immediate match-start authority from the shell action.</summary>
    internal void ForceStart() => Report(Session()?.ForceDeveloperStart() == true, "Match start requested.", "Start unavailable; host and connected ready players required.");

    /// <summary>Shows Configs-only footer controls without affecting shell-owned Close.</summary>
    /// <param name="visible">Whether Configs is active with host authority.</param>
    internal void SetConfigurationFooterVisible(bool visible)
    {
        _configurationFooterVisible = visible;
        _resetButton.Visible = visible;
        _configurationButtons.Visible = visible;
        _feedback.Visible = visible && _feedbackState != DeveloperOptionsFeedbackState.None;
    }

    private static Button Button(Container parent, string text, Action pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

    private static GridContainer ConfigurationRows(Container parent)
    {
        var rows = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        rows.AddThemeConstantOverride("h_separation", 16);
        rows.AddThemeConstantOverride("v_separation", 6);
        parent.AddChild(rows);
        return rows;
    }

    private static Label ConfigurationLabel(string text) => new()
    {
        Text = text,
        CustomMinimumSize = new Vector2(280, 0),
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static void ValueColor(Control editor, bool isDefault)
    {
        Color color = new(isDefault ? "69b7ff" : "ff7979");
        foreach (string state in new[] { "font_color", "font_selected_color", "font_focus_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" })
        {
            editor.AddThemeColorOverride(state, color);
        }
    }

    private void Reset()
    {
        if (LocalSettings is not null) { RenderTireValues(TireEffectSettings.Defaults); UpdateFeedback(); }
        if (Session()?.IsDeveloperHost != true)
        {
            return;
        }

        _draft.ResetToDefaults();
        RenderValues();
        StageNetworkDefaults();

        _status.Text = "Game defaults staged. Press Apply Settings to apply them.";
        UpdateFeedback();
    }

    private void ResetCategory(string group)
    {
        if (Session()?.IsDeveloperHost != true) { return; }
        _draft.ResetCategoryToDefaults(group);
        RenderValues(group);
        CategoryResetFeedback(group);
    }

    private void ResetTireCategory(string group)
    {
        if (LocalSettings is null) { return; }
        foreach (var option in TireEffectSettings.Options.Where(option => option.Group == group))
        {
            _tireEditors[option.Key].Text = TireEffectSettings.Defaults[option.Key].ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        }
        ColorTireValues();
        CategoryResetFeedback(group);
    }

    private void ResetNetworkCategory()
    {
        if (Session()?.IsDeveloperHost != true || !NetworkSimulationControl.Supported(Session()?.Gateway)) { return; }
        StageNetworkDefaults();
        CategoryResetFeedback("Local network simulation");
    }

    private void StageNetworkDefaults()
    {
        var defaults = new NetworkSimulation();
        double[] values = [defaults.LatencyMilliseconds, defaults.JitterMilliseconds, defaults.LossPercent, defaults.ReorderPercent, defaults.ReorderMilliseconds];
        for (int i = 0; i < _simulation.Count; i++) { _simulation[i].Value = values[i]; }
    }

    private void CategoryResetFeedback(string group)
    {
        _status.Text = $"{group} defaults staged. Press Apply Settings to apply them.";
        UpdateFeedback();
    }

    private void RefreshValues()
    {
        if (Session() is not { IsDeveloperHost: true } session)
        {
            return;
        }

        _draft.Discard(session.DeveloperConfiguration);
        RenderValues();
        UpdateFeedback();
    }

    private void RenderValues(string? group = null)
    {
        _refreshing = true;
        foreach (var option in GameplayOptions.All.Where(option => group is null || option.Group == group))
        {
            string value = _draft.Get(option.Key);
            if (_editors[option.Key] is OptionButton presets)
            {
                presets.Select(presets.GetItemIndex(int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
            }
            else if (_editors[option.Key] is CheckButton toggle)
            {
                toggle.ButtonPressed = value == "1";
                toggle.Text = value == "1" ? "On" : "Off";
            }
            else
            {
                ((LineEdit)_editors[option.Key]).Text = value;
            }

            ValueColor(_editors[option.Key], _draft.IsDefault(option));
        }

        ColorNetworkValues();
        _refreshing = false;
    }

    private void ColorNetworkValues()
    {
        foreach (var value in _simulation)
        {
            ValueColor(value.GetLineEdit(), value.Value == 0);
        }
    }

    private void StageValue(string key, string value)
    {
        if (!_refreshing)
        {
            _draft.Set(key, value);
            _status.Text = string.Empty;
            var option = GameplayOptions.All.Single(option => option.Key == key);
            ValueColor(_editors[key], _draft.IsDefault(option));
            if (_editors[key] is CheckButton toggle)
            {
                toggle.Text = value == "1" ? "On" : "Off";
            }

            UpdateFeedback();
        }
    }

    private void UpdateFeedback()
    {
        if (HasUnappliedChanges)
        {
            SetFeedback(DeveloperOptionsFeedbackState.Unsaved);
        }
        else if (_feedbackState == DeveloperOptionsFeedbackState.Unsaved)
        {
            SetFeedback(DeveloperOptionsFeedbackState.None);
        }
    }

    private void SetFeedback(DeveloperOptionsFeedbackState state)
    {
        _feedbackState = state;
        _feedbackSeconds = state is DeveloperOptionsFeedbackState.Applied or DeveloperOptionsFeedbackState.Discarded ? 3 : 0;
        (_feedback.Text, Color color) = state switch
        {
            DeveloperOptionsFeedbackState.Unsaved => ("Unsaved changes", new Color("e6a23c")),
            DeveloperOptionsFeedbackState.Applied => ("Settings applied", new Color("46b85d")),
            DeveloperOptionsFeedbackState.Discarded => ("Changes discarded", new Color("ff6262")),
            _ => (string.Empty, Colors.White),
        };
        _feedback.AddThemeColorOverride("font_color", color);
        _feedback.Visible = _configurationFooterVisible && state != DeveloperOptionsFeedbackState.None;
    }

    private void Filter()
    {
        string[] words = _search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool any = false;
        foreach (var (section, rows) in _sections)
        {
            bool available = _localSections.Contains(section) || Session()?.IsDeveloperHost == true && (section != _network || NetworkSimulationControl.Supported(Session()?.Gateway));
            bool found = false;
            foreach (var row in rows)
            {
                bool match = available && words.All(word => row.Search.Contains(word, StringComparison.OrdinalIgnoreCase));
                row.Label.Visible = row.Editor.Visible = match;
                found |= match;
            }

            section.Visible = found;
            section.Reveal(words.Length > 0);
            any |= found;
        }

        _noResults.Visible = !any;
    }

    private void Report(bool success, string accepted, string rejected) => _status.Text = success ? accepted : rejected;

    private bool ApplyTireValues(TireEffectSettings tire)
    {
        LocalSettings!.UpdateSettings(LocalSettings.Current with { TireEffects = tire });
        _tireBaseline = tire;
        bool saved = LocalSettings.Flush();
        _tireSaveFailed = !saved;
        _status.Text = LocalSettings.SaveStatus;
        SetFeedback(DeveloperOptionsFeedbackState.Applied);
        return saved;
    }

    private void RenderTireValues(TireEffectSettings settings)
    {
        foreach (var (key, editor) in _tireEditors) { editor.Text = settings[key].ToString("G", System.Globalization.CultureInfo.InvariantCulture); }
        ColorTireValues();
    }

    private void ColorTireValues()
    {
        foreach (var (key, editor) in _tireEditors)
        {
            ValueColor(editor, float.TryParse(editor.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value) && value == TireEffectSettings.Defaults[key]);
        }
    }

}
