namespace Trackstorm.Client.Online;

/// <summary>Atomic refresh and keyed updates prevent duplicates; filtering is local and ordinal.</summary>
internal sealed class LobbyBrowser
{
    private readonly Dictionary<string, OnlineLobby> _lobbies = new(StringComparer.Ordinal);

    /// <summary>Case-insensitive lobby-name filter, or a provider refresh request.</summary>
    internal string Search { get; set; } = string.Empty;

    /// <summary>Compatible rows ordered by ordinal name then logical lobby ID.</summary>
    internal IReadOnlyList<LobbyRow> Rows => _lobbies.Values.Where(lobby => lobby.Compatible && lobby.Name.Contains(Search, StringComparison.OrdinalIgnoreCase))
        .OrderBy(lobby => lobby.Name, StringComparer.OrdinalIgnoreCase).ThenBy(lobby => lobby.Id, StringComparer.Ordinal).Select(lobby => lobby.Row).ToArray();

    /// <summary>Atomically replaces the previous refresh results.</summary>
    /// <param name="lobbies">Provider results or the owned native lobby interface.</param>
    internal void Replace(IEnumerable<OnlineLobby> lobbies)
    {
        _lobbies.Clear();
        foreach (var lobby in lobbies)
        {
            Update(lobby);
        }
    }

    /// <summary>Updates the existing logical lobby without replacing its identity.</summary>
    /// <param name="lobby">Current client-only lobby metadata.</param>
    internal void Update(OnlineLobby lobby) => _lobbies[lobby.Id] = lobby;

    /// <summary>Resolves the latest discovery result by its logical identity.</summary>
    /// <param name="id">Logical EOS lobby identity.</param>
    /// <returns>The validated result, or an explicit failure/absence.</returns>
    internal OnlineLobby? Find(string id) => _lobbies.GetValueOrDefault(id);
}
