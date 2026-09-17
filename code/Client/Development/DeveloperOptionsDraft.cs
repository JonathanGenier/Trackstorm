using System.Globalization;
using Trackstorm.Core.Development;

namespace Trackstorm.Client.Development;

/// <summary>Editor-only text and pending changes; has no gameplay or persistence authority.</summary>
internal sealed class DeveloperOptionsDraft
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private GameplayConfiguration _baseline = GameplayConfiguration.HostedDefaults;
    private bool _replaceAll;

    /// <summary>Starts with an editable production preset before a session is attached.</summary>
    internal DeveloperOptionsDraft() => Discard(GameplayConfiguration.HostedDefaults);

    /// <summary>Whether edits must survive automatic runtime refresh.</summary>
    internal bool IsDirty { get; private set; }

    /// <summary>Reads the pending text for a catalog key.</summary>
    /// <param name="key">Stable gameplay setting key.</param>
    /// <returns>Uncommitted editor text.</returns>
    internal string Get(string key) => _values[key];

    /// <summary>Stages editor input without interpreting or committing it.</summary>
    /// <param name="key">Stable gameplay setting key.</param>
    /// <param name="value">Uncommitted editor text.</param>
    internal void Set(string key, string value)
    {
        _values[key] = value;
        IsDirty = true;
    }

    /// <summary>Discards pending changes in favor of the current authoritative values.</summary>
    /// <param name="current">Current accepted gameplay configuration.</param>
    internal void Discard(GameplayConfiguration current)
    {
        _baseline = current;
        Populate(current);
        _replaceAll = false;
        IsDirty = false;
    }

    /// <summary>Stages the complete production preset until the user explicitly applies it.</summary>
    internal void ResetToDefaults()
    {
        Populate(GameplayConfiguration.HostedDefaults);
        _replaceAll = true;
        IsDirty = true;
    }

    /// <summary>Parses a request for the existing authority path; owning Core rules still validate it.</summary>
    /// <param name="edits">Requested values to send through authoritative validation.</param>
    /// <param name="error">Safe feedback for unparseable editor input.</param>
    /// <returns>Whether every field could be parsed.</returns>
    internal bool TryGetEdits(out Dictionary<string, double> edits, out string error)
    {
        edits = new(StringComparer.Ordinal);
        error = string.Empty;
        foreach (var option in GameplayOptions.All)
        {
            if (!double.TryParse(Get(option.Key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                error = $"Invalid value: {option.Group} / {option.Label}.";
                return false;
            }

            // Reset replaces the whole preset, including fields changed by authority after the editor opened.
            if (_replaceAll || option.Read(_baseline) != value)
            {
                edits.Add(option.Key, value);
            }
        }

        return true;
    }

    private void Populate(GameplayConfiguration configuration)
    {
        foreach (var option in GameplayOptions.All)
        {
            _values[option.Key] = option.Read(configuration).ToString(option.Integral ? "G17" : "G9", CultureInfo.InvariantCulture);
        }
    }
}
