using Trackstorm.Core.Sessions;

namespace Trackstorm.Transport.Tests;

/// <summary>In-memory protocol model for deterministic C# migration tests, never a hosted backend.</summary>
internal sealed class LeaseStore(TimeProvider time) : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (AuthorityLease Lease, long Granted)> _records = new();

    /// <inheritdoc />
    public void Dispose() => _records.Clear();

    /// <summary>Models a read-only object locator, separate from the private lease session.</summary>
    /// <param name="routingId">Opaque object identity.</param>
    /// <returns>The existing grant without modifying its fence.</returns>
    internal AuthorityLease? Resolve(string routingId)
    {
        lock (_gate)
        {
            var session = _records.Values.SingleOrDefault(entry => entry.Lease.RoutingId == routingId).Lease?.Session;
            return session is null ? null : Read(session);
        }
    }

    /// <summary>Reads the model's authoritative remaining duration.</summary>
    /// <param name="session">Opaque session.</param>
    /// <returns>Current lease or absence.</returns>
    internal AuthorityLease? Read(string session)
    {
        lock (_gate)
        {
            return _records.TryGetValue(session, out var entry) ? entry.Lease with { RemainingSeconds = Math.Max(0, AuthorityLease.DurationSeconds - time.GetElapsedTime(entry.Granted).TotalSeconds) } : null;
        }
    }

    /// <summary>Models conditional create, renewal, release and takeover.</summary>
    /// <param name="operation">Protocol operation.</param>
    /// <param name="request">Expected fence.</param>
    /// <param name="subject">Authenticated test identity.</param>
    /// <returns>Grant or conflict.</returns>
    internal AuthorityLease? Execute(string operation, LeaseRequest request, string subject)
    {
        lock (_gate)
        {
            var current = Read(request.Session);
            if (operation == "create" && current is null && request.Epoch == 0 && request.Token.Length == 0)
            {
                return Commit(new(request.Session, subject, 1, string.Empty, AuthorityLease.DurationSeconds));
            }

            if (current is null || current.Epoch != request.Epoch || current.Token != request.Token)
            {
                return null;
            }

            if (operation == "release" && current.Holder == subject)
            {
                _records[request.Session] = (current, time.GetTimestamp() - (long)(AuthorityLease.DurationSeconds * time.TimestampFrequency));
                return Read(request.Session);
            }

            if (operation == "renew" && current.Holder == subject && current.RemainingSeconds > 0)
            {
                return Commit(current);
            }

            return operation == "takeover" && current.Holder != subject && current.RemainingSeconds == 0 && current.Epoch < 9007199254740991
                ? Commit(current with { Holder = subject, Epoch = current.Epoch + 1 }) : null;
        }
    }

    private AuthorityLease Commit(AuthorityLease record)
    {
        _records[record.Session] = (record with { Token = Guid.NewGuid().ToString("N"), RoutingId = record.RoutingId ?? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(record.Session))) }, time.GetTimestamp());
        return Read(record.Session)!;
    }
}
