namespace Trackstorm.Core.Development;

/// <summary>Explicit stable setting identity and typed access to an existing configuration property.</summary>
public sealed class GameplayOption
{
    private readonly Func<GameplayConfiguration, double, GameplayConfiguration> _set;
    private readonly Func<GameplayConfiguration, double> _get;

    /// <summary>Binds a stable setting identifier to its real configuration property.</summary>
    /// <param name="key">Stable setting identifier.</param>
    /// <param name="group">Presentation category.</param>
    /// <param name="label">Human-readable setting label.</param>
    /// <param name="integral">Whether fractional values are forbidden.</param>
    /// <param name="get">Typed accessor into the owning configuration.</param>
    /// <param name="set">Typed immutable candidate update.</param>
    /// <param name="doublePrecision">Whether a fractional owning property uses binary64 rather than binary32.</param>
    internal GameplayOption(string key, string group, string label, bool integral, Func<GameplayConfiguration, double> get, Func<GameplayConfiguration, double, GameplayConfiguration> set, bool doublePrecision = false)
    {
        Key = key;
        Group = group;
        Label = label;
        Integral = integral;
        DoublePrecision = doublePrecision;
        _get = get;
        _set = set;
    }

    /// <summary>Stable persistent and UI setting identity.</summary>
    public string Key { get; }
    /// <summary>Presentation category for this gameplay setting.</summary>
    public string Group { get; }
    /// <summary>Human-readable label for the existing owning property.</summary>
    public string Label { get; }
    /// <summary>Whether this setting requires an integer.</summary>
    public bool Integral { get; }
    /// <summary>Whether fractional editor values retain binary64 precision.</summary>
    public bool DoublePrecision { get; }
    /// <summary>Whether this setting has exactly two possible values.</summary>
    public bool Boolean => Key == "respawn.clear_held_item_on_death";

    /// <summary>Reads the actual effective owning configuration.</summary>
    /// <returns>Current numeric value from the owning configuration property.</returns>
    /// <param name="configuration">Validated effective gameplay tuning.</param>
    public double Read(GameplayConfiguration configuration) => _get(configuration);

    /// <summary>Produces a candidate; the complete configuration must be validated before publication.</summary>
    /// <returns>Immutable candidate containing the requested value.</returns>
    /// <param name="configuration">Validated effective gameplay tuning.</param>
    /// <param name="value">Requested finite value.</param>
    internal GameplayConfiguration Set(GameplayConfiguration configuration, double value)
    {
        if (!double.IsFinite(value) || (Integral && Math.Truncate(value) != value) || (Boolean && value is not (0 or 1)))
        {
            throw new ArgumentException("A finite value of the setting's declared type is required.");
        }

        return _set(configuration, value);
    }
}
