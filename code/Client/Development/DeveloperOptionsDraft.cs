using System.Globalization;
using Trackstorm.Core.Development;

namespace Trackstorm.Client.Development;

/// <summary>Editor-only text and pending changes; has no gameplay or persistence authority.</summary>
internal sealed class DeveloperOptionsDraft
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private GameplayConfiguration _baseline = GameplayConfiguration.HostedDefaults;
    private bool _replaceAll;
    private readonly HashSet<string> _resetGroups = new(StringComparer.Ordinal);

    /// <summary>Starts with an editable production preset before a session is attached.</summary>
    internal DeveloperOptionsDraft() => Discard(GameplayConfiguration.HostedDefaults);

    /// <summary>Whether edits must survive automatic runtime refresh.</summary>
    internal bool IsDirty => GameplayOptions.All.Any(option => !Matches(option, _baseline));

    /// <summary>Compares parsed editor values with the canonical hosted preset, independently of edit history.</summary>
    /// <param name="option">Catalog setting and its actual numeric type.</param>
    /// <returns>Whether the staged value equals the canonical game default.</returns>
    internal bool IsDefault(GameplayOption option) => Matches(option, GameplayConfiguration.HostedDefaults);

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
    }

    /// <summary>Discards pending changes in favor of the current authoritative values.</summary>
    /// <param name="current">Current accepted gameplay configuration.</param>
    internal void Discard(GameplayConfiguration current)
    {
        _baseline = current;
        Populate(current);
        _replaceAll = false;
        _resetGroups.Clear();
    }

    /// <summary>Stages the complete production preset until the user explicitly applies it.</summary>
    internal void ResetToDefaults()
    {
        Populate(GameplayConfiguration.HostedDefaults);
        _replaceAll = true;
    }

    /// <summary>Stages one complete catalog category without disturbing other pending text.</summary>
    /// <param name="group">Existing catalog category to reset.</param>
    internal void ResetCategoryToDefaults(string group)
    {
        Populate(GameplayConfiguration.HostedDefaults, GameplayOptions.All.Where(option => option.Group == group));
        _resetGroups.Add(group);
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

            // A reset includes every requested category field, even if authority changed after opening.
            if (_replaceAll || _resetGroups.Contains(option.Group) || !Matches(option, _baseline))
            {
                edits.Add(option.Key, value);
            }
        }

        return true;
    }

    private bool Matches(GameplayOption option, GameplayConfiguration configuration)
    {
        return double.TryParse(Get(option.Key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            && double.IsFinite(value)
            && (option.Integral || option.DoublePrecision ? value : (float)value) == option.Read(configuration);
    }

    private void Populate(GameplayConfiguration configuration, IEnumerable<GameplayOption>? options = null)
    {
        foreach (var option in options ?? GameplayOptions.All)
        {
            double value = option.Read(configuration);
            _values[option.Key] = option.Integral || option.DoublePrecision ? value.ToString("G", CultureInfo.InvariantCulture) : ((float)value).ToString("G", CultureInfo.InvariantCulture);
        }
    }
}
