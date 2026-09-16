using System.Globalization;

namespace Trackstorm.Client.Hud;

/// <summary>Shared numeric latency presentation for the HUD and per-player standings.</summary>
internal static class PingFormatter
{
    /// <summary>Formats the publication's bounded milliseconds; missing or invalid samples remain unavailable.</summary>
    /// <param name="milliseconds">Fresh host-published round-trip latency.</param>
    /// <returns>The same compact value for both displays.</returns>
    internal static string Format(int? milliseconds) => milliseconds is >= 0 and <= 60000
        ? milliseconds.Value.ToString(CultureInfo.InvariantCulture) + " ms" : "--";
}
