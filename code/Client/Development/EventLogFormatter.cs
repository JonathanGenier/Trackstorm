using System.Globalization;
using System.Text;
using Trackstorm.Core.Events;

namespace Trackstorm.Client.Development;

/// <summary>Read-only wording over shared structured outcomes; contains no gameplay detection.</summary>
internal static class EventLogFormatter
{
    /// <summary>Formats one event without interpreting live gameplay state.</summary>
    /// <param name="entry">Immutable structured outcome.</param>
    /// <returns>Timestamp, category, origin and message.</returns>
    internal static string Format(RuntimeEvent entry)
    {
        var result = new StringBuilder();
        void Append(string value) => result.Append(value);
        Write(entry, Append, Append, Append, Append);
        return result.ToString();
    }

    /// <summary>Emits literal text with semantic styling boundaries, never markup or name matching.</summary>
    /// <param name="entry">Immutable structured outcome.</param>
    /// <param name="text">Normal message and origin text.</param>
    /// <param name="timestamp">Elapsed timestamp text.</param>
    /// <param name="category">Category text.</param>
    /// <param name="player">Captured actor or target name.</param>
    internal static void Write(RuntimeEvent entry, Action<string> text, Action<string> timestamp, Action<string> category, Action<string> player)
    {
        string actor = entry.ActorName;
        string target = entry.TargetName;
        string Number(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? "--";
        string source = entry.Cause.Length == 0 ? string.Empty : $" from {entry.Cause}";
        void ActorSuffix()
        {
            if (actor.Length > 0)
            {
                text(" (");
                player(actor);
                text(")");
            }
        }

        ulong milliseconds = entry.Milliseconds;
        timestamp($"[{milliseconds / 60000:00}:{(milliseconds / 1000) % 60:00}.{milliseconds % 1000:000}]");
        text(" ");
        category($"[{entry.Category}]");
        text($" [{(entry.Local ? "Local" : "Host")} #{entry.Sequence}] ");
        switch (entry.Category)
        {
            case EventCategory.Damage:
                player(target);
                text($" took {Number(entry.Amount)} damage{source}");
                ActorSuffix();
                text($" — HP {Number(entry.RemainingHP)}/{Number(entry.MaximumHP)}");
                break;
            case EventCategory.Healing:
                player(target);
                text($" healed {Number(entry.Amount)}{source} — HP {Number(entry.RemainingHP)}/{Number(entry.MaximumHP)}");
                break;
            case EventCategory.Developer when entry.Kind == "Setting changed":
                player(actor);
                text($" changed {entry.Context}: {Number(entry.PreviousValue)} → {Number(entry.Amount)}");
                break;
            case EventCategory.Lifecycle when entry.Kind == "Kill":
                player(actor);
                text(" killed ");
                player(target);
                text(source);
                break;
            case EventCategory.Lifecycle:
                player(target);
                text($" {entry.Kind.ToLowerInvariant()}{source}");
                ActorSuffix();
                break;
            default:
                player(actor);
                text($"{(actor.Length > 0 ? " " : string.Empty)}{entry.Kind}");
                if (target.Length > 0)
                {
                    text(" — ");
                    player(target);
                }

                text($"{(entry.Cause.Length > 0 ? $" — {entry.Cause}" : string.Empty)}{(entry.Context.Length > 0 ? $" — {entry.Context}" : string.Empty)}{(entry.Amount.HasValue ? $" ({Number(entry.Amount)})" : string.Empty)}");
                break;
        }
    }
}
