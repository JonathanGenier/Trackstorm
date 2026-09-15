using System.Globalization;
using Godot;
using Trackstorm.Core.Settings;

namespace Trackstorm.Client.Settings;

/// <summary>Minimal diagnostics consumer. Telemetry is optional; preference changes never mutate simulation.</summary>
internal sealed partial class SettingsHud : VBoxContainer
{
    private readonly Label _speed = new();
    private readonly Label _fps = new();
    private readonly Label _ping = new();
    private PlayerSettingsController _settings = null!;
    private double? _metresPerSecond;
    private double? _pingMilliseconds;

    /// <summary>Directly observable diagnostics visibility for integration verification.</summary>
    internal bool FpsVisible => _fps.Visible;

    /// <summary>Directly observable latency visibility for integration verification.</summary>
    internal bool PingVisible => _ping.Visible;

    /// <summary>Rendered speed including preferred units.</summary>
    internal string SpeedText => _speed.Text;
    /// <summary>A combat speedometer replaces the duplicate diagnostics speed while playing.</summary>
    internal bool SpeedVisible { set => _speed.Visible = value; }

    /// <inheritdoc/>
    public override void _Process(double delta) => _fps.Text = $"FPS  {Engine.GetFramesPerSecond()}";

    /// <inheritdoc/>
    public override void _ExitTree() => _settings.Changed -= Refresh;

    /// <summary>Connects only to the preference service, with no persistence dependency.</summary>
    /// <param name="settings">Preference publisher.</param>
    internal void Initialize(PlayerSettingsController settings)
    {
        _settings = settings;
        AddChild(_speed);
        AddChild(_fps);
        AddChild(_ping);
        _settings.Changed += Refresh;
        Refresh();
    }

    /// <summary>Receives presentation telemetry in simulation units; null means unavailable.</summary>
    /// <param name="metresPerSecond">Unconverted speed.</param>
    /// <param name="pingMilliseconds">Measured latency, or null offline.</param>
    internal void SetTelemetry(double? metresPerSecond, double? pingMilliseconds)
    {
        _metresPerSecond = metresPerSecond;
        _pingMilliseconds = pingMilliseconds;
        Refresh();
    }

    private void Refresh()
    {
        string unit = Hud.CombatHudView.UnitSuffix(_settings.Current.SpeedUnit);
        string speed = _metresPerSecond is double value && double.IsFinite(value)
            ? Hud.CombatHudView.ConvertSpeed(value, _settings.Current.SpeedUnit).ToString("0.0", CultureInfo.InvariantCulture) : "—";
        _speed.Text = $"Speed  {speed} {unit}";
        _fps.Visible = _settings.Current.ShowFps;
        _ping.Visible = _settings.Current.ShowPing;
        _ping.Text = _pingMilliseconds is double ping && double.IsFinite(ping) && ping >= 0
            ? $"Ping  {ping:0} ms" : "Ping  — (offline)";
    }
}
