using System.Globalization;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Settings;

namespace Trackstorm.Client.Settings;

/// <summary>Pure presentation of timing, connection state and independent local preferences.</summary>
internal readonly record struct DiagnosticsView(bool FpsVisible, bool PingVisible, string Fps, string Ping)
{
    /// <summary>Formats current values without retaining any sample.</summary>
    /// <param name="settings">Local visibility flags.</param>
    /// <param name="fps">Completed frame-time average.</param>
    /// <param name="connection">Current lifecycle and optional statistics.</param>
    /// <returns>Compact labels and independent visibility.</returns>
    internal static DiagnosticsView Create(PlayerSettings settings, double? fps, ConnectionDiagnostic connection)
    {
        string frameRate = fps is double value && double.IsFinite(value) && value >= 0
            ? value.ToString("0", CultureInfo.InvariantCulture) : "—";
        string ping = connection.State switch
        {
            ConnectionDiagnosticState.Connecting => "connecting",
            ConnectionDiagnosticState.Reconnecting => "reconnecting",
            ConnectionDiagnosticState.Disconnected => "disconnected",
            ConnectionDiagnosticState.Connected => Hud.PingFormatter.Format(connection.Statistics.PingMilliseconds),
            _ => Hud.PingFormatter.Format(null)
        };
        return new(settings.ShowFps, settings.ShowPing, "FPS  " + frameRate, "Ping  " + ping);
    }
}
