namespace Trackstorm.Core.Sessions;

/// <summary>Validated detached session state, published atomically to every peer.</summary>
public sealed class LobbySnapshot
{
    /// <summary>Validates the complete serializable lobby boundary.</summary>
    /// <param name="session">Nonzero session lifetime.</param>
    /// <param name="revision">Monotonic state revision.</param>
    /// <param name="match">Vehicle generation, advanced for every start.</param>
    /// <param name="phase">Current shared phase.</param>
    /// <param name="players">Connected players including host identity one.</param>
    /// <param name="graceTicks">Reservation duration at 60 Hz.</param>
    public LobbySnapshot(ulong session, ulong revision, ulong match, SessionPhase phase, IEnumerable<SessionPlayer> players, ulong graceTicks = 1800)
    {
        SessionPlayer[] copy = players.Take(9).ToArray();
        if (session == 0 || revision == 0 || graceTicks is 0 or > 216000 || match < session || !Enum.IsDefined(phase) ||
            (phase == SessionPhase.Arena && match == session) || copy.Length is < 1 or > 8 ||
            copy.Any(player => player is null || player.Id == 0 || player.Generation == 0 || (!player.Connected && player.Ready) || player.Name != PlayerName.Sanitize(player.Name)) ||
            copy.Select(player => player.Id).Distinct().Count() != copy.Length || !copy.Any(player => player.Id == 1))
        {
            throw new ArgumentException("Invalid lobby state.");
        }

        Session = session;
        Revision = revision;
        Match = match;
        Phase = phase;
        Players = Array.AsReadOnly(copy.OrderBy(player => player.Id).ToArray());
        GraceTicks = graceTicks;
    }

    /// <summary>Stable session lifetime.</summary>
    public ulong Session { get; }
    /// <summary>Authoritative reconnect reservation duration in 60 Hz ticks.</summary>
    public ulong GraceTicks { get; }
    /// <summary>Strictly increasing accepted mutation revision.</summary>
    public ulong Revision { get; }
    /// <summary>Fresh vehicle protocol generation per match.</summary>
    public ulong Match { get; }
    /// <summary>Shared lobby or arena state.</summary>
    public SessionPhase Phase { get; }
    /// <summary>Complete immutable connected roster.</summary>
    public IReadOnlyList<SessionPlayer> Players { get; }
    /// <summary>Development policy: one through eight connected players, all explicitly ready.</summary>
    public bool CanStart => Phase == SessionPhase.Lobby && Players.All(player => player.Ready && player.Connected);
}
