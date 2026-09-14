using System.Text;

namespace Trackstorm.Client.Online;

/// <summary>Presentation-safe lobby names: 1–48 Unicode scalars after normalization.</summary>
internal static class LobbyName
{
    /// <summary>Maximum number of Unicode scalars in a canonical lobby name.</summary>
    internal const int MaximumLength = 48;

    /// <summary>Normalizes untrusted lobby text, discards markup and controls, and bounds Unicode length.</summary>
    /// <param name="value">Untrusted text or serialized verifier.</param>
    /// <returns>The validated result, or an explicit failure/absence.</returns>
    internal static string Sanitize(string? value)
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

            if (!Rune.IsLetterOrDigit(rune) && rune.Value is not ('-' or '_' or '\''))
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

        return result.ToString().Trim();
    }
}
