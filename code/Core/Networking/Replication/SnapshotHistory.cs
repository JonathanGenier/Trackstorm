namespace Trackstorm.Core.Networking.Replication;

/// <summary>Bounded replicated snapshot data with strict session and serial/tick ordering.</summary>
public sealed class SnapshotHistory
{
    /// <summary>One second at the 20 Hz snapshot rate.</summary>
    public const int Capacity = 20;
    private readonly List<WorldSnapshot> _snapshots = new();
    private readonly ulong _session;

    /// <summary>Creates data storage scoped to exactly one negotiated host session.</summary>
    /// <param name="session">Host session identity.</param>
    public SnapshotHistory(ulong session)
    {
        ArgumentOutOfRangeException.ThrowIfZero(session);
        _session = session;
    }

    /// <summary>Ordered immutable data retained for remote interpolation.</summary>
    public IReadOnlyList<WorldSnapshot> Snapshots => _snapshots.AsReadOnly();

    /// <summary>Accepts only a strictly newer snapshot and discards data beyond the bounded history.</summary>
    /// <param name="snapshot">Validated decoded authoritative state.</param>
    /// <returns>False for another session, stale, duplicate or ambiguous ordering.</returns>
    public bool Add(WorldSnapshot snapshot)
    {
        if (snapshot.Session != _session || (_snapshots.Count > 0 &&
            (snapshot.Tick <= _snapshots[^1].Tick || !NetworkSequence.IsNewer(unchecked((uint)snapshot.Tick), unchecked((uint)_snapshots[^1].Tick)))))
        {
            return false;
        }

        _snapshots.Add(snapshot);
        if (_snapshots.Count > Capacity)
        {
            _snapshots.RemoveAt(0);
        }

        return true;
    }
}
