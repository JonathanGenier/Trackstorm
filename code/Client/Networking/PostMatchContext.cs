using Trackstorm.Core.Matches;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Networking;

/// <summary>Detached Application Flow handoff, fenced to one session and match generation.</summary>
internal sealed record PostMatchContext(LobbySnapshot Roster, FinalMatchResults Results)
{
    /// <summary>Rejects presentation or actions against a replacement session/match.</summary>
    internal bool Matches(LobbySnapshot? current) => current is { Phase: SessionPhase.Arena } &&
        current.Session == Roster.Session && current.Match == Roster.Match;

    /// <summary>Live connectivity/names may change; authoritative ranks and statistics never do.</summary>
    internal SessionPlayer Participant(ulong id, LobbySnapshot? current)
    {
        var roster = Matches(current) ? current! : Roster;
        return roster.Players.FirstOrDefault(player => player.Id == id)
            ?? new SessionPlayer(id, roster.Departed.FirstOrDefault(player => player.Id == id)?.Name
                ?? Roster.Players.FirstOrDefault(player => player.Id == id)?.Name
                ?? Roster.Departed.FirstOrDefault(player => player.Id == id)?.Name ?? $"Player {id}", false, false);
    }
}
