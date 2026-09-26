using System.Globalization;
using System.Text;

namespace Trackstorm.Core.Development;

/// <summary>Human-readable canonical-default differences from one confirmed session boundary.</summary>
public static class ConfigurationChangesExport
{
    /// <summary>Formats stable category/key order; caller owns the destination and timestamp.</summary>
    public static string Format(GameplayConfiguration configuration, string version, DateTimeOffset timestamp)
    {
        configuration.Validate();
        var text = new StringBuilder("Trackstorm Config Changes\n");
        text.Append("Version: ").Append(version).Append('\n');
        text.Append("Exported: ").Append(timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        foreach (var group in GameplayOptions.All.Where(option => option.Read(configuration) != option.Read(GameplayConfiguration.HostedDefaults)).GroupBy(option => option.Group))
        {
            text.Append('\n').Append('[').Append(group.Key).Append("]\n");
            foreach (var option in group)
            {
                double value = option.Read(configuration);
                string formatted = option.Integral || option.DoublePrecision ? value.ToString("R", CultureInfo.InvariantCulture) : ((float)value).ToString("R", CultureInfo.InvariantCulture);
                text.Append(option.Key).Append('=').Append(formatted).Append('\n');
            }
        }
        return text.ToString();
    }
}
