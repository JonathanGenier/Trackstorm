using System.Globalization;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Core.Development;

/// <summary>Reads the exported category/key format into validated edits without mutating a session.</summary>
public static class ConfigurationChangesImport
{
    /// <summary>Maximum UTF-8 file size accepted by the import UI and maximum text length accepted here.</summary>
    public const int MaximumLength = 65536;

    /// <summary>Overlays only listed keys on the current configuration for validation; rejects the entire invalid file.</summary>
    public static bool TryParse(string text, GameplayConfiguration current, out Dictionary<string, double> edits, out string version, out string error)
    {
        edits = new(StringComparer.Ordinal);
        version = string.Empty;
        error = "Invalid Trackstorm config export.";
        if (text.Length > MaximumLength) { error = "Config file exceeds 64 KiB."; return false; }
        var lines = text.TrimStart('\uFEFF').ReplaceLineEndings("\n").Split('\n');
        var parsed = new Dictionary<string, double>(StringComparer.Ordinal);
        var groups = new HashSet<string>(StringComparer.Ordinal);
        string? group = null;
        int header = 0;
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            if (line.Length == 0) continue;
            error = $"Invalid config export at line {index + 1}.";
            if (header == 0)
            {
                if (line != "Trackstorm Config Changes") return false;
                header++;
            }
            else if (header == 1)
            {
                if (!line.StartsWith("Version: ", StringComparison.Ordinal) || !GameVersion.TryParse(line[9..].Trim(), out var parsedVersion)) return false;
                version = parsedVersion!.ToString();
                header++;
            }
            else if (header == 2)
            {
                if (!line.StartsWith("Exported: ", StringComparison.Ordinal) ||
                    !DateTimeOffset.TryParseExact(line[10..].Trim(), "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return false;
                header++;
            }
            else if (line.StartsWith('[') && line.EndsWith(']'))
            {
                group = line[1..^1];
                if (!GameplayOptions.All.Any(option => option.Group == group) || !groups.Add(group))
                { error = $"Unknown or repeated category at line {index + 1}."; return false; }
            }
            else
            {
                int separator = line.IndexOf('=');
                if (separator < 1 || group is null) return false;
                string key = line[..separator].Trim();
                var option = GameplayOptions.All.SingleOrDefault(option => option.Key == key);
                if (option is null || option.Group != group)
                { error = $"Unknown setting or incorrect category at line {index + 1}."; return false; }
                if (!double.TryParse(line[(separator + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                    !double.IsFinite(value) || !parsed.TryAdd(key, value))
                { error = $"Invalid or repeated value at line {index + 1}."; return false; }
            }
        }
        if (header != 3) { error = "Missing config export header, version or timestamp."; return false; }
        if (!GameplayOptions.TryApply(current, parsed, out _, out error)) return false;
        edits = parsed;
        return true;
    }
}
