using System.Globalization;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Settings;

namespace Trackstorm.Client.Settings;

/// <summary>Minimal diagnostics consumer. Telemetry is optional; preference changes never mutate simulation.</summary>
internal sealed partial class SettingsHud : VBoxContainer
{
    private readonly Label _speed = new();
    private readonly Label _fps = new();
    private readonly Label _ping = new();
    private readonly FrameRateSampler _frames = new();
    private PlayerSettingsController _settings = null!;
    private double? _metresPerSecond;
    private ConnectionDiagnostic _connection;

    /// <summary>Directly observable diagnostics visibility for integration verification.</summary>
    internal bool FpsVisible => _fps.Visible;

    /// <summary>Directly observable latency visibility for integration verification.</summary>
    internal bool PingVisible => _ping.Visible;

    /// <summary>Rendered speed including preferred units.</summary>
    internal string SpeedText => _speed.Text;
    /// <summary>A combat speedometer replaces the duplicate diagnostics speed while playing.</summary>
    internal bool SpeedVisible { set => _speed.Visible = value; }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        double? previous = _frames.FramesPerSecond;
        _frames.Add(delta);
        if (previous != _frames.FramesPerSecond)
        {
            Refresh();
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree() => _settings.Changed -= Refresh;

    /// <summary>Connects only to the preference service, with no persistence dependency.</summary>
    /// <param name="settings">Preference publisher.</param>
    internal void Initialize(PlayerSettingsController settings)
    {
        _settings = settings;
        AddChild(_speed);
        var counters = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        counters.AddThemeConstantOverride("separation", 12);
        AddChild(counters);
        foreach (var label in new[] { _fps, _ping })
        {
            label.AddThemeFontSizeOverride("font_size", 14);
            label.AddThemeColorOverride("font_shadow_color", Colors.Black);
            label.AddThemeConstantOverride("shadow_offset_y", 1);
            counters.AddChild(label);
        }

        _settings.Changed += Refresh;
        Refresh();
    }

    /// <summary>Receives presentation telemetry in simulation units; null means unavailable.</summary>
    /// <param name="metresPerSecond">Unconverted speed.</param>
    /// <param name="connection">Fresh provider-neutral connection projection.</param>
    internal void SetTelemetry(double? metresPerSecond, ConnectionDiagnostic connection)
    {
        bool changed = connection != _connection || (_speed.Visible && metresPerSecond != _metresPerSecond);
        _metresPerSecond = metresPerSecond;
        _connection = connection;
        if (changed)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        string unit = Hud.CombatHudView.UnitSuffix(_settings.Current.SpeedUnit);
        string speed = _metresPerSecond is double value && double.IsFinite(value)
            ? Hud.CombatHudView.ConvertSpeed(value, _settings.Current.SpeedUnit).ToString("0.0", CultureInfo.InvariantCulture) : "—";
        _speed.Text = $"Speed  {speed} {unit}";
        var view = DiagnosticsView.Create(_settings.Current, _frames.FramesPerSecond, _connection);
        _fps.Visible = view.FpsVisible;
        _ping.Visible = view.PingVisible;
        _fps.Text = view.Fps;
        _ping.Text = view.Ping;
    }
}
