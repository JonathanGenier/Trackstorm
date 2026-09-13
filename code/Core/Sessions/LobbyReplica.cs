namespace Trackstorm.Core.Sessions;

/// <summary>Core-owned acceptance of ordered host publications without local mutation of lobby truth.</summary>
public sealed class LobbyReplica
{
    /// <summary>Last accepted complete host state.</summary>
    public LobbySnapshot? State { get; private set; }
    /// <summary>Host-assigned identity, immutable for this connection lifetime.</summary>
    public ulong PlayerId { get; private set; }

    /// <summary>Accepts a complete state only from the established host and current session lifetime.</summary>
    /// <param name="state">Validated incoming snapshot.</param>
    /// <param name="playerId">Recipient identity from the host.</param>
    /// <param name="sender">Actual transport sender.</param>
    /// <param name="server">Established server connection.</param>
    /// <returns>Whether the publication was committed.</returns>
    public bool Accept(LobbySnapshot state, ulong playerId, ulong sender, ulong server)
    {
        if (server == 0 || sender != server || !state.Players.Any(player => player.Id == playerId) ||
            (State is not null && (state.Session != State.Session || playerId != PlayerId || state.Revision <= State.Revision || state.Match < State.Match)))
        {
            return false;
        }

        State = state;
        PlayerId = playerId;
        return true;
    }
}
