namespace Trackstorm.Client.Online;

/// <summary>Platform-owned native resources retained until completion or terminal platform release.</summary>
internal sealed class EosPendingHandles : IDisposable
{
    private readonly HashSet<Lease> _pending = new();
    private bool _disposed;

    /// <summary>Releases pending caller-owned handles while the platform is valid, before Platform.Release.</summary>
    public void Dispose()
    {
        _disposed = true;
        foreach (var lease in _pending.ToArray())
        {
            lease.Dispose();
        }
    }

    /// <summary>Retains a release operation independently of the replaceable provider.</summary>
    /// <param name="release">Releases one caller-owned SDK handle.</param>
    /// <returns>An idempotent completion lease.</returns>
    internal IDisposable Retain(Action release)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var lease = new Lease(this, release);
        _pending.Add(lease);
        return lease;
    }

    private sealed class Lease(EosPendingHandles owner, Action release) : IDisposable
    {
        public void Dispose()
        {
            if (owner._pending.Remove(this))
            {
                release();
            }
        }
    }
}
