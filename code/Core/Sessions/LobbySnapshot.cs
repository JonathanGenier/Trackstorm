namespace Trackstorm.Core.Sessions;

/// <summary>Validated detached session state, published atomically to every peer.</summary>
public sealed class LobbySnapshot
{
    /// <summary>Validates the complete serializable lobby boundary.</summary>
    /// <param name="session">Nonzero session lifetime.</param>
    /// <param name="revision">Monotonic state revision.</param>
    /// <param name="match">Vehicle generation, advanced for every start.</param>
    /// <param name="phase">Current shared phase.</param>
    /// <param name="players">Connected and reserved stable players, including the current host.</param>
    /// <param name="currentHostId">Stable player holding authority.</param>
    /// <param name="authorityEpoch">Monotonic authority fence.</param>
    /// <param name="departed">Match participants whose reconnect/admission slots were permanently released.</param>
    public LobbySnapshot(ulong session, ulong revision, ulong match, SessionPhase phase, IEnumerable<SessionPlayer> players, ulong currentHostId = 1, ulong authorityEpoch = 1, IEnumerable<MatchParticipant>? departed = null)
    {
        SessionPlayer[] copy = players.Take(9).ToArray();
        MatchParticipant[] history = (departed ?? []).Take(Matches.MatchState.MaximumPlayers + 1).ToArray();
        if (session == 0 || revision == 0 || match < session || !Enum.IsDefined(phase) ||
            (phase == SessionPhase.Arena && match == session) || copy.Length is < 1 or > 8 ||
            copy.Any(player => player is null || (phase == SessionPhase.Lobby && (!player.Connected || player.RetainedHost)) || player.Id == 0 || player.Generation == 0 || (!player.Connected && player.Ready) || player.Name != PlayerName.Sanitize(player.Name)) ||
            copy.Select(player => player.Id).Distinct().Count() != copy.Length || authorityEpoch == 0 || !copy.Any(player => player.Id == currentHostId && player.Connected))
        {
            throw new ArgumentException("Invalid lobby state.");
        }

        if (history.Length + copy.Length > Matches.MatchState.MaximumPlayers || (phase == SessionPhase.Lobby && history.Length != 0) ||
            history.Any(player => player is null || player.Id == 0 || player.Name != PlayerName.Sanitize(player.Name)) ||
            history.Select(player => player.Id).Concat(copy.Select(player => player.Id)).Distinct().Count() != history.Length + copy.Length)
        {
            throw new ArgumentException("Invalid retained match participants.");
        }

        Session = session;
        Revision = revision;
        Match = match;
        Phase = phase;
        Players = Array.AsReadOnly(copy.OrderBy(player => player.Id).ToArray());
        Departed = Array.AsReadOnly(history.OrderBy(player => player.Id).ToArray());
        CurrentHostId = currentHostId;
        AuthorityEpoch = authorityEpoch;
    }

    /// <summary>Stable session lifetime.</summary>
    public ulong Session { get; }
    /// <summary>Stable player currently holding gameplay authority, independently of provider ownership.</summary>
    public ulong CurrentHostId { get; }
    /// <summary>Monotonic authority fence within the logical session.</summary>
    public ulong AuthorityEpoch { get; }
    /// <summary>Strictly increasing accepted mutation revision.</summary>
    public ulong Revision { get; }
    /// <summary>Fresh vehicle protocol generation per match.</summary>
    public ulong Match { get; }
    /// <summary>Shared lobby or arena state.</summary>
    public SessionPhase Phase { get; }
    /// <summary>Explicit continuity policy for the current phase.</summary>
    public SessionReconnectPolicy ReconnectPolicy => Phase == SessionPhase.Lobby ? SessionReconnectPolicy.FreshJoin : SessionReconnectPolicy.RetainedResume;
    /// <summary>Complete immutable connected roster.</summary>
    public IReadOnlyList<SessionPlayer> Players { get; }
    /// <summary>Offline match history; never grants admission, ownership or reconnect authorization.</summary>
    public IReadOnlyList<MatchParticipant> Departed { get; }
    /// <summary>Development policy: one through eight connected players, all explicitly ready.</summary>
    public bool CanStart => Phase == SessionPhase.Lobby && Players.All(player => player.Connected && player.Ready);
}
