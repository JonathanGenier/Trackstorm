using System.Globalization;
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
        string actor = entry.ActorName;
        string target = entry.TargetName;
        string Number(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? "--";
        string source = entry.Cause.Length == 0 ? string.Empty : $" from {entry.Cause}";
        string message = entry.Category switch
        {
            EventCategory.Damage => $"{target} took {Number(entry.Amount)} damage{source}{(actor.Length == 0 ? string.Empty : $" ({actor})")} — HP {Number(entry.RemainingHP)}/{Number(entry.MaximumHP)}",
            EventCategory.Healing => $"{target} healed {Number(entry.Amount)}{source} — HP {Number(entry.RemainingHP)}/{Number(entry.MaximumHP)}",
            EventCategory.Developer when entry.Kind == "Setting changed" => $"{actor} changed {entry.Context}: {Number(entry.PreviousValue)} → {Number(entry.Amount)}",
            EventCategory.Lifecycle when entry.Kind == "Kill" => $"{actor} killed {target}{source}",
            EventCategory.Lifecycle => $"{target} {entry.Kind.ToLowerInvariant()}{source}{(actor.Length == 0 ? string.Empty : $" ({actor})")}",
            _ => $"{actor}{(actor.Length > 0 ? " " : string.Empty)}{entry.Kind}{(target.Length > 0 ? $" — {target}" : string.Empty)}{(entry.Cause.Length > 0 ? $" — {entry.Cause}" : string.Empty)}{(entry.Context.Length > 0 ? $" — {entry.Context}" : string.Empty)}{(entry.Amount.HasValue ? $" ({Number(entry.Amount)})" : string.Empty)}",
        };
        ulong milliseconds = entry.Milliseconds;
        return $"[{milliseconds / 60000:00}:{(milliseconds / 1000) % 60:00}.{milliseconds % 1000:000}] [{entry.Category}] [{(entry.Local ? "Local" : "Host")} #{entry.Sequence}] {message}";
    }
}
