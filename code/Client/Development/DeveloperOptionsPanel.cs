using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Development;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Client.Development;

/// <summary>Single Settings/F1 surface; only Apply commits its editor draft through current authority.</summary>
internal sealed partial class DeveloperOptionsPanel : VBoxContainer
{
    private readonly VBoxContainer _host = new() { Visible = false };
    private readonly VBoxContainer _practice = new() { Visible = false };
    private readonly VBoxContainer _network = new() { Visible = false };
    private readonly Label _availability = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _status = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _diagnostics = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Dictionary<string, Control> _editors = new(StringComparer.Ordinal);
    private readonly DeveloperOptionsDraft _draft = new();
    private readonly List<SpinBox> _simulation = new();
    private object? _owner;
    private ulong _revision;
    private ulong _authorityEpoch;
    private bool _refreshing;
    private double _elapsed;

    /// <summary>Current production session supplied by composition.</summary>
    internal Func<DevelopmentSession?> Session { get; set; } = () => null;
    /// <summary>Current local practice authority supplied by composition.</summary>
    internal Func<VehicleArena?> Practice { get; set; } = () => null;
    /// <summary>Credential-free identity diagnostics supplied by composition.</summary>
    internal Func<string> IdentityDiagnostics { get; set; } = () => "EOS unavailable.";

    /// <inheritdoc/>
    public override void _Ready()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _availability.AddThemeFontSizeOverride("font_size", 16);
        _status.AddThemeFontSizeOverride("font_size", 16);
        _diagnostics.AddThemeFontSizeOverride("font_size", 16);
        AddChild(_availability);
        AddChild(_status);
        AddChild(_host);
        var force = Button(_host, "FORCE START MATCH", () => Report(Session()?.ForceDeveloperStart() == true, "Match start requested.", "Start unavailable; host and connected ready players required."));
        force.Name = "ForceStart";
        force.CustomMinimumSize = new Vector2(0, 64);
        force.AddThemeColorOverride("font_color", new Color("ffb45c"));
        force.AddThemeFontSizeOverride("font_size", 24);
        var items = new HBoxContainer();
        _host.AddChild(items);
        foreach (var item in new[] { HeldItem.Wrench, HeldItem.Missile })
        {
            Button(items, $"Give {item}", () => Report(Session()?.GiveDeveloperItem(item) == true, $"{item} granted.", "Grant rejected: enter an arena with a living host and an empty slot."));
        }

        Button(_host, "Apply Settings", Apply);
        Button(_host, "Discard Changes", () =>
        {
            if (Session()?.IsDeveloperHost == true)
            {
                RefreshValues();
                _status.Text = "Unapplied changes discarded. " + Session()?.DeveloperSettings?.Status;
            }
        });
        Button(_host, "Reset to Defaults", () =>
        {
            if (Session()?.IsDeveloperHost == true)
            {
                _draft.ResetToDefaults();
                RenderValues();
                _status.Text = "Production defaults staged. Press Apply Settings to apply and save them.";
            }
        });
        var hint = new Label { Text = "Apply Settings updates live tuning and saves it on this host. Discard Changes restores active values. Reset to Defaults stages production defaults until Apply Settings.\nNew respawn/pickup delays and missile lifetime affect future events. Max HP preserves health percentage. Seed restarts item selection. Countdown edits update the running countdown.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        hint.AddThemeFontSizeOverride("font_size", 16);
        _host.AddChild(hint);
        foreach (var group in GameplayOptions.All.GroupBy(option => option.Group))
        {
            _host.AddChild(new Label { Text = group.Key.ToUpperInvariant() });
            foreach (var option in group)
            {
                var row = new HBoxContainer();
                row.AddChild(new Label { Text = System.Text.RegularExpressions.Regex.Replace(option.Label, "([a-z])([A-Z])", "$1 $2"), SizeFlagsHorizontal = SizeFlags.ExpandFill });
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
                row.AddChild(editor);
                _host.AddChild(row);
            }
        }

        _host.AddChild(_network);
        _network.AddChild(new Label { Text = "LOCAL NETWORK SIMULATION\nDirect-IP: affects all sockets in this process. Not saved.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        string[] names = ["Latency (ms)", "Jitter (ms)", "Loss (%)", "Reorder (%)", "Reorder delay (ms)"];
        int[] maxima = [5000, 1000, 100, 100, 5000];
        for (int i = 0; i < names.Length; i++)
        {
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = names[i], SizeFlagsHorizontal = SizeFlags.ExpandFill });
            var value = new SpinBox { MinValue = 0, MaxValue = maxima[i], Step = 1, CustomMinimumSize = new Vector2(150, 36) };
            _simulation.Add(value);
            row.AddChild(value);
            _network.AddChild(row);
        }

        Button(_network, "Apply local network simulation", () =>
        {
            var session = Session();
            var simulation = new NetworkSimulation((int)_simulation[0].Value, (int)_simulation[1].Value, (float)_simulation[2].Value, (float)_simulation[3].Value, (int)_simulation[4].Value);
            Report(NetworkSimulationControl.TryApply(session?.Gateway, session?.IsDeveloperHost == true, simulation), "Local network simulation applied.", "Network simulation is unsupported or host authority is unavailable.");
        });
        AddChild(_practice);
        Button(_practice, "Reset practice arena", () => Practice()?.ResetVehicles());
        Button(_practice, "Detonate nearby", () =>
        {
            if (Practice() is { } arena)
            {
                arena.Explode(arena.Player.GlobalPosition + new Vector3(-2, -0.2f, 0.5f));
            }
        });
        AddChild(_diagnostics);
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        var session = Session();
        _host.Visible = session?.IsDeveloperHost == true;
        _practice.Visible = DeveloperTools.Enabled && Practice() is not null;
        _network.Visible = NetworkSimulationControl.Supported(session?.Gateway);
        _diagnostics.Visible = DeveloperTools.Enabled;
        _elapsed += delta;
        if (_elapsed < 0.25 || !IsVisibleInTree())
        {
            return;
        }

        _elapsed = 0;
        _availability.Text = !DeveloperTools.Enabled ? "Developer tools are disabled in this build."
            : _host.Visible ? (_network.Visible ? "Host controls · F1 closes this page" : "Host controls · network simulation unavailable for this transport")
            : _practice.Visible ? "Local practice tools. Host a multiplayer session for gameplay tuning."
            : "Read-only diagnostics. Host a session to access tuning and developer actions.";
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

        _diagnostics.Text = IdentityDiagnostics() + "\n" + DeveloperDiagnostics.Capture(session);
    }

    /// <summary>Commits edited values once through current host authority.</summary>
    internal void Apply()
    {
        if (!_draft.TryGetEdits(out var edits, out string error))
        {
            _status.Text = error;
            return;
        }

        var session = Session();
        error = "Host authority unavailable.";
        if (session?.ConfigureDeveloperOptions(edits, out error) == true)
        {
            RefreshValues();
            _status.Text = session.DeveloperSettings?.Status ?? "Tuning applied; persistence unavailable.";
        }
        else
        {
            _status.Text = error;
        }
    }

    private static Button Button(Container parent, string text, Action pressed)
    {
        var button = new Button { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
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
            }
            else
            {
                ((LineEdit)_editors[option.Key]).Text = value;
            }
        }

        _refreshing = false;
    }

    private void StageValue(string key, string value)
    {
        if (!_refreshing)
        {
            _draft.Set(key, value);
        }
    }

    private void Report(bool success, string accepted, string rejected) => _status.Text = success ? accepted : rejected;
}
