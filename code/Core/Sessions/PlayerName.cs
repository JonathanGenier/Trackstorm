using System.Text;

namespace Trackstorm.Core.Sessions;

/// <summary>Deterministic display policy shared by host validation and serialized state.</summary>
public static class PlayerName
{
    /// <summary>Maximum displayed Unicode scalar count.</summary>
    public const int MaximumLength = 24;

    /// <summary>Retains letters, numbers, spaces, hyphens and underscores; collapses whitespace.</summary>
    /// <param name="value">Untrusted name; blank results use Player.</param>
    /// <returns>A bounded plain-text name without control, markup or directional characters.</returns>
    public static string Sanitize(string? value)
    {
        var result = new StringBuilder();
        int count = 0;
        bool space = false;
        foreach (Rune rune in (value ?? string.Empty).EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                space = result.Length > 0;
                continue;
            }

            if (!Rune.IsLetterOrDigit(rune) && rune.Value is not '-' and not '_')
            {
                continue;
            }

            if (space && count < MaximumLength)
            {
                result.Append(' ');
                count++;
            }

            space = false;
            if (count == MaximumLength)
            {
                break;
            }

            result.Append(rune.ToString());
            count++;
        }

        string name = result.ToString().TrimEnd();
        return name.Length == 0 ? "Player" : name;
    }
}
