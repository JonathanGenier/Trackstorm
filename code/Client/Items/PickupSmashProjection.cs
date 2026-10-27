using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>Bounded, match-owned claim watermarks; recovery installs history without replaying it.</summary>
internal sealed class PickupSmashProjection(IEnumerable<string> markers, bool freshHost = false)
{
    private readonly Dictionary<string, ulong> _tokens = markers.ToDictionary(id => id, _ => 0ul, StringComparer.Ordinal);
    private ulong _revision;
    private bool _initialized = freshHost;

    /// <summary>Accepts complete trusted state once and returns only newly committed pickup tokens.</summary>
    internal bool Observe(ItemPublication state, bool seed, out IReadOnlyList<ItemSpawnState> claims)
    {
        claims = Array.Empty<ItemSpawnState>();
        if (!seed && _initialized && state.Revision <= _revision) return false;
        bool baseline = seed || !_initialized;
        _initialized = true;
        _revision = state.Revision;
        var fresh = new List<ItemSpawnState>();
        foreach (var spawn in state.Spawns)
        {
            if (!_tokens.TryGetValue(spawn.Id, out ulong token)) continue;
            if (!baseline && spawn.Token > token) fresh.Add(spawn);
            _tokens[spawn.Id] = baseline ? spawn.Token : Math.Max(token, spawn.Token);
        }
        claims = fresh;
        return true;
    }
}
