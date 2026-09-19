namespace Trackstorm.Core.Matches;

/// <summary>Mode-reported completion, independent of scoring rules and application navigation.</summary>
public sealed record MatchOutcome
{
    /// <summary>Creates a bounded diagnostic reason and optional winning participant.</summary>
    /// <param name="reason">Stable mode-defined completion identifier.</param>
    /// <param name="winner">Optional winner; draws and non-player outcomes need no winner.</param>
    public MatchOutcome(string reason, ulong? winner = null)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 64 || reason.Any(char.IsControl) || winner == 0)
        {
            throw new ArgumentException("Invalid match outcome.");
        }

        Reason = reason;
        Winner = winner;
    }

    /// <summary>Mode-owned completion identifier for diagnostics and results.</summary>
    public string Reason { get; }
    /// <summary>Optional winning participant, never a transport identity.</summary>
    public ulong? Winner { get; }
}
