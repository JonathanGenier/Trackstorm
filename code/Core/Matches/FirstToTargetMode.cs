namespace Trackstorm.Core.Matches;

/// <summary>Existing combat mode's completion rule, separate from reusable lifecycle transitions.</summary>
internal static class FirstToTargetMode
{
    /// <summary>Reports the existing authoritative end condition without choosing a lifecycle transition.</summary>
    /// <param name="credited">Score after the current authoritative kill.</param>
    /// <param name="target">Validated winning threshold.</param>
    /// <returns>Completion at the target, otherwise no outcome.</returns>
    internal static MatchOutcome? Evaluate(PlayerScore credited, int target) => credited.Kills >= target ? new MatchOutcome("kill-target", credited.Player) : null;
}
