using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Development;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Client.Development;

/// <summary>Single Settings/F1 surface; only Apply commits its editor draft through current authority.</summary>
internal sealed partial class DeveloperOptionsPanel : VBoxContainer
{
    private readonly VBoxContainer _host = new() { Visible = false, SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly VBoxContainer _practice = new() { Visible = false };
    private readonly VBoxContainer _network = new() { Visible = false };
    private readonly Label _availability = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _status = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly LineEdit _search = new() { Name = "ConfigSearch", PlaceholderText = "Search settings by label or category…", ClearButtonEnabled = true };
    private readonly Label _noResults = new() { Text = "No settings match your search.", Visible = false };
    private readonly Dictionary<string, Control> _editors = new(StringComparer.Ordinal);
    private readonly List<(Control Section, List<(Label Label, Control Editor, string Search)> Rows)> _sections = new();
    private readonly DeveloperOptionsDraft _draft = new();
    private readonly List<SpinBox> _simulation = new();
    private readonly double[] _appliedSimulation = new double[5];
    private object? _owner;
    private ulong _revision;
    private ulong _authorityEpoch;
    private bool _refreshing;
    private double _elapsed;

    /// <summary>Current production session supplied by composition.</summary>
    internal Func<DevelopmentSession?> Session { get; set; } = () => null;
    /// <summary>Current local practice authority supplied by composition.</summary>
    internal Func<VehicleArena?> Practice { get; set; } = () => null;
    /// <summary>Configuration actions mounted in the shell's fixed footer.</summary>
    internal HBoxContainer Footer { get; } = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    /// <summary>Whether closing needs an explicit decision, including invalid editor text.</summary>
    internal bool HasUnappliedChanges => _draft.IsDirty || NetworkDirty;
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
        var force = Button(_host, "FORCE START MATCH", () => Report(Session()?.ForceDeveloperStart() == true, "Match start requested.", "Start unavailable; host and connected ready players required."));
        force.Name = "ForceStart";
        force.CustomMinimumSize = new Vector2(0, 48);
        force.AddThemeColorOverride("font_color", new Color("ffb45c"));
        force.AddThemeFontSizeOverride("font_size", 24);
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
            var section = new VBoxContainer();
            settings.AddChild(section);
            section.AddChild(new Label { Text = group.Key.ToUpperInvariant() });
            var rows = ConfigurationRows(section);
            var entries = new List<(Label Label, Control Editor, string Search)>();
            _sections.Add((section, entries));
            foreach (var option in group)
            {
                string text = System.Text.RegularExpressions.Regex.Replace(option.Label, "([a-z])([A-Z])", "$1 $2");
                var label = ConfigurationLabel(text);
                rows.AddChild(label);
                Control editor;
                if (option.Boolean)
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

                editor.Name = option.Key.Replace('.', '_');
                editor.TooltipText = option.Label + (option.Label.EndsWith("Ticks", StringComparison.Ordinal) ? " (60 ticks = 1 second)" : string.Empty);
                _editors.Add(option.Key, editor);
                rows.AddChild(editor);
                entries.Add((label, editor, $"{group.Key} {text} {option.Label} {option.Key}"));
            }
        }

        settings.AddChild(_network);
        _network.AddChild(new Label { Text = "LOCAL NETWORK SIMULATION · Direct-IP only, process-wide, not saved.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        string[] names = ["Latency (ms)", "Jitter (ms)", "Loss (%)", "Reorder (%)", "Reorder delay (ms)"];
        int[] maxima = [5000, 1000, 100, 100, 5000];
        var networkRows = ConfigurationRows(_network);
        var networkEntries = new List<(Label Label, Control Editor, string Search)>();
        _sections.Add((_network, networkEntries));
        for (int i = 0; i < names.Length; i++)
        {
            var label = ConfigurationLabel(names[i]);
            networkRows.AddChild(label);
            var value = new SpinBox { MinValue = 0, MaxValue = maxima[i], Step = 1, UpdateOnTextChanged = true, CustomMinimumSize = new Vector2(150, 36) };
            value.ValueChanged += _ => ColorNetworkValues();
            _simulation.Add(value);
            networkRows.AddChild(value);
            networkEntries.Add((label, value, "Local network simulation " + names[i]));
        }

        Button(Footer, "Reset to Defaults", Reset);
        Footer.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        Button(Footer, "Apply Settings", () => Apply());
        Button(Footer, "Cancel", Cancel);
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
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        var session = Session();
        _host.Visible = session?.IsDeveloperHost == true;
        Footer.Visible = _host.Visible && IsVisibleInTree();
        _practice.Visible = DeveloperTools.Enabled && Practice() is not null;
        _elapsed += delta;
        if (_elapsed < 0.25)
        {
            return;
        }

        _elapsed = 0;
        Filter();
        _availability.Text = !DeveloperTools.Enabled ? "Developer tools are disabled in this build."
            : _host.Visible ? (NetworkSimulationControl.Supported(session?.Gateway)
                ? "Host controls · Apply validates, synchronizes and saves gameplay tuning"
                : "Host controls · local network simulation unavailable for this transport")
            : _practice.Visible ? "Local practice tools. Host a multiplayer session for gameplay tuning."
            : "Configs require current host authority. Host a session to access tuning and developer actions.";
        object? owner = (object?)session?.Arena?.Driver ?? session?.Lobby;
        ulong revision = session?.Arena?.Driver.Configuration.Revision ?? session?.Lobby?.Authority?.Configuration.Revision ?? 0;
        ulong epoch = session?.Lobby?.State?.AuthorityEpoch ?? 0;
        if (_host.Visible && (!ReferenceEquals(_owner, owner) || epoch != _authorityEpoch || (!_draft.IsDirty && revision != _revision)))
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
        if (!_draft.TryGetEdits(out var edits, out string error))
        {
            _status.Text = error;
            return false;
        }

        var session = Session();
        error = "Host authority unavailable.";
        if (session?.ConfigureDeveloperOptions(edits, out error) != true)
        {
            _status.Text = error;
            return false;
        }

        RefreshValues();
        if (NetworkDirty)
        {
            var simulation = new NetworkSimulation((int)_simulation[0].Value, (int)_simulation[1].Value, (float)_simulation[2].Value, (float)_simulation[3].Value, (int)_simulation[4].Value);
            if (!NetworkSimulationControl.TryApply(session.Gateway, session.IsDeveloperHost, simulation))
            {
                _status.Text = "Gameplay tuning applied. Local network simulation unavailable; its edits remain staged.";
                return false;
            }

            for (int i = 0; i < _simulation.Count; i++)
            {
                _appliedSimulation[i] = _simulation[i].Value;
            }
        }

        _status.Text = session.DeveloperSettings?.Status ?? "Tuning applied; persistence unavailable.";
        return session.DeveloperSettings?.LastSaveSucceeded != false;
    }

    /// <summary>Discards unapplied edits and restores currently effective values.</summary>
    internal void Cancel()
    {
        _draft.Discard(Session()?.DeveloperConfiguration ?? GameplayConfiguration.HostedDefaults);
        RenderValues();
        for (int i = 0; i < _simulation.Count; i++)
        {
            _simulation[i].Value = _appliedSimulation[i];
        }

        _status.Text = "Unapplied changes discarded.";
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
        if (Session()?.IsDeveloperHost != true)
        {
            return;
        }

        _draft.ResetToDefaults();
        RenderValues();
        foreach (var value in _simulation)
        {
            value.Value = 0;
        }

        _status.Text = "Game defaults staged. Press Apply Settings to apply them.";
    }

    private void RefreshValues()
    {
        if (Session() is not { IsDeveloperHost: true } session)
        {
            return;
        }

        _draft.Discard(session.DeveloperConfiguration);
        RenderValues();
    }

    private void RenderValues()
    {
        _refreshing = true;
        foreach (var option in GameplayOptions.All)
        {
            string value = _draft.Get(option.Key);
            if (_editors[option.Key] is CheckButton toggle)
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
            var option = GameplayOptions.All.Single(option => option.Key == key);
            ValueColor(_editors[key], _draft.IsDefault(option));
            if (_editors[key] is CheckButton toggle)
            {
                toggle.Text = value == "1" ? "On" : "Off";
            }
        }
    }

    private void Filter()
    {
        string[] words = _search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool any = false;
        foreach (var (section, rows) in _sections)
        {
            bool available = section != _network || NetworkSimulationControl.Supported(Session()?.Gateway);
            bool found = false;
            foreach (var row in rows)
            {
                bool match = available && words.All(word => row.Search.Contains(word, StringComparison.OrdinalIgnoreCase));
                row.Label.Visible = row.Editor.Visible = match;
                found |= match;
            }

            section.Visible = found;
            any |= found;
        }

        _noResults.Visible = !any;
    }

    private void Report(bool success, string accepted, string rejected) => _status.Text = success ? accepted : rejected;
}
