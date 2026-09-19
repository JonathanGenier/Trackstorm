namespace Trackstorm.Core.Matches;

/// <summary>Application Flow's completed load/sync handoff; constructing this does not perform loading.</summary>
public sealed class SynchronizedMatchContext
{
    /// <summary>Captures an already prepared match and its committed participant identities.</summary>
    /// <param name="matchId">Nonzero match generation.</param>
    /// <param name="tick">Authoritative fixed tick at handoff.</param>
    /// <param name="participants">Synchronized stable player identities.</param>
    public SynchronizedMatchContext(ulong matchId, ulong tick, IEnumerable<ulong> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);
        ulong[] players = participants.Order().ToArray();
        if (matchId == 0 || players.Length is < 1 or > 8 || players.Any(player => player == 0) || players.Distinct().Count() != players.Length)
        {
            throw new ArgumentException("Invalid synchronized match context.");
        }

        MatchId = matchId;
        Tick = tick;
        Participants = Array.AsReadOnly(players);
    }

    /// <summary>Application-owned match generation.</summary>
    public ulong MatchId { get; }
    /// <summary>Fixed tick at the completed handoff.</summary>
    public ulong Tick { get; }
    /// <summary>Detached initial roster; later admissions remain session-owned.</summary>
    public IReadOnlyList<ulong> Participants { get; }
}
