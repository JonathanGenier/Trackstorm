namespace Trackstorm.Client.Statistics;

/// <summary>A presentation row derived only from the existing allowlisted diagnostic text.</summary>
/// <param name="Label">Readable diagnostic label.</param>
/// <param name="Value">Unmodified value text from the current projection.</param>
internal sealed record StatisticEntry(string Label, string Value)
{
    // TS-58's compact vehicle lines predate the colon-delimited diagnostic format.
    private static readonly string[] LegacyLabels =
    [
        "Longitudinal/lateral acceleration", "Last acknowledged input", "Prediction error", "Snapshot age",
        "Corrections ≥3m", "Interpolation", "Front/rear slip", "State tick", "Handbrake",
        "Steering", "Sliding", "Position", "Vehicle", "Player", "Speed", "Life", "HP", "Trackstorm",
    ];

    /// <summary>Splits the existing compact display format without reading or caching runtime owners.</summary>
    /// <param name="section">Current credential-safe category.</param>
    /// <returns>Rows retaining every nonempty diagnostic fragment.</returns>
    internal static IEnumerable<StatisticEntry> From(StatisticSection section)
    {
        foreach (string fragment in section.Text.Split(["\n", "·", ";", "   "], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = fragment.IndexOf(':');
            if (colon > 0)
            {
                yield return new(fragment[..colon], fragment[(colon + 1)..].Trim());
                continue;
            }

            string? label = LegacyLabels.FirstOrDefault(candidate => fragment.StartsWith(candidate + " ", StringComparison.Ordinal));
            yield return label is null ? new("State", fragment) : new(label, fragment[(label.Length + 1)..].Trim());
        }
    }

    /// <summary>Matches category, label or displayed value; whitespace-only search reveals all rows.</summary>
    /// <param name="category">Owning system/category heading.</param>
    /// <param name="query">Presentation-only search text.</param>
    /// <returns>Whether this row should be visible.</returns>
    internal bool Matches(string category, string query)
    {
        string search = query.Trim();
        return category.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            Label.Contains(search, StringComparison.OrdinalIgnoreCase) || Value.Contains(search, StringComparison.OrdinalIgnoreCase);
    }
}
