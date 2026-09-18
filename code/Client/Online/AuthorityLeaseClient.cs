using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Online;

/// <summary>Owner-thread fencing lifecycle. Only successful writes create local simulation permission.</summary>
internal sealed class AuthorityLeaseClient(ILeaseTransport transport, string subject, TimeProvider time) : IDisposable
{
    private Task<AuthorityLease?>? _pending;
    private string _operation = string.Empty;
    private long _requested;
    private long? _granted;
    private long? _polled;
    private long? _readAt;
    private ulong _retiredEpoch;
    private AuthorityLease? _record;
    private string? _session;
    private bool _create;
    private bool _release;
    private bool _observing;

    /// <summary>Latest observed old-host renewal, used to reject stale pre-partition checkpoints.</summary>
    internal long? HostProgressAt { get; private set; }

    /// <summary>Read-only service locator from an accepted response; never the private lease key.</summary>
    internal string? RoutingId => _record?.RoutingId;

    /// <summary>Credential-free lease lifecycle status.</summary>
    internal string Status => _granted.HasValue ? $"lease held; epoch {_record!.Epoch}; local validity {Math.Max(0, AuthorityLease.ClientDurationSeconds - time.GetElapsedTime(_granted.Value).TotalSeconds):0.0}s" : _pending is not null ? "request pending" : _retiredEpoch != 0 ? "authority retired" : "waiting for lease";

    private bool Fresh => _observing && _readAt.HasValue && time.GetElapsedTime(_readAt.Value).TotalSeconds < 2;

    /// <inheritdoc />
    public void Dispose() => transport.Dispose();

    /// <summary>Pumps serial service requests and owner-thread completions.</summary>
    /// <param name="session">Private coordination session.</param>
    /// <param name="create">Initial host bootstrap permission.</param>
    /// <param name="epoch">Current Core epoch.</param>
    /// <param name="observe">Host-loss or migration state requires trusted service observations.</param>
    internal void Poll(string? session, bool create, ulong epoch, bool observe)
    {
        if (session is null)
        {
            return;
        }

        if (_session != session)
        {
            if (_session is not null)
            {
                throw new InvalidOperationException("A lease client belongs to one coordination session.");
            }

            _session = session;
            _create = create;
        }

        if (observe != _observing)
        {
            _observing = observe;
            _readAt = null;
            _polled = null;
        }

        _ = Available(epoch);
        if (_pending is { IsCompleted: true })
        {
            var response = _pending.IsCompletedSuccessfully ? _pending.Result : null;
            _pending = null;
            if (response is not null && time.GetElapsedTime(_requested).TotalSeconds < 3 && response.Session == session && response.Epoch > 0 && response.Token is { Length: 32 } && !string.IsNullOrWhiteSpace(response.Holder) && response.RemainingSeconds is >= 0 and <= AuthorityLease.DurationSeconds)
            {
                if (response.Holder != subject && response.Epoch == epoch && _record?.Token != response.Token)
                {
                    HostProgressAt = _requested;
                }

                _record = response;
                _readAt = time.GetTimestamp();
                if (_operation is "create" or "renew" or "takeover" && response.RemainingSeconds > 0 && response.Holder == subject && response.Epoch > _retiredEpoch && time.GetElapsedTime(_requested).TotalSeconds < AuthorityLease.ClientDurationSeconds)
                {
                    _granted = _requested;
                }
            }
        }

        if (_pending is not null || (_polled.HasValue && time.GetElapsedTime(_polled.Value).TotalSeconds < (_granted.HasValue ? 2 : 1)))
        {
            return;
        }

        if (_release && _record is { } relinquished && relinquished.Holder == subject && relinquished.Epoch <= _retiredEpoch)
        {
            _release = false;
            Begin("release", new(session, relinquished.Epoch, relinquished.Token));
        }
        else if (_record is null && _create && epoch > _retiredEpoch)
        {
            _create = false;
            Begin("create", new(session, 0, string.Empty));
        }
        else if (_record is { } record && record.Holder == subject && record.Epoch == epoch && record.Epoch > _retiredEpoch && (_granted.HasValue || create))
        {
            Begin("renew", new(session, record.Epoch, record.Token));
        }
        else if (observe)
        {
            Begin("read", new(session, 0, string.Empty));
        }
    }

    /// <summary>Checks and irrevocably expires local simulation permission.</summary>
    /// <param name="epoch">Current authority epoch.</param>
    /// <returns>Whether the local grant is valid.</returns>
    internal bool Available(ulong epoch)
    {
        if (_granted.HasValue && time.GetElapsedTime(_granted.Value).TotalSeconds >= AuthorityLease.ClientDurationSeconds)
        {
            _retiredEpoch = Math.Max(_retiredEpoch, _record?.Epoch ?? epoch);
            _granted = null;
        }

        return _granted.HasValue && _record?.Epoch == epoch && _record.Holder == subject && epoch > _retiredEpoch;
    }

    /// <summary>Stops renewal immediately; an in-flight renewal cannot revive this authority.</summary>
    /// <param name="epoch">Retired local epoch.</param>
    internal void Release(ulong epoch)
    {
        _retiredEpoch = Math.Max(_retiredEpoch, epoch);
        _granted = null;
        _create = false;
        _release = true;
        _polled = null;
    }

    /// <summary>Requires a fresh authoritative observation of old-holder expiry.</summary>
    /// <param name="epoch">Old epoch.</param>
    /// <param name="holder">Old authenticated holder.</param>
    /// <returns>Whether old permission has expired.</returns>
    internal bool Expired(ulong epoch, string holder) => Fresh && _record is { } record && record.Epoch == epoch && record.Holder == holder && record.RemainingSeconds == 0;

    /// <summary>Attempts atomic acquisition only after agreement selects this process.</summary>
    /// <param name="oldEpoch">Expected prior epoch.</param>
    /// <returns>Whether the successor grant is held.</returns>
    internal bool Acquire(ulong oldEpoch)
    {
        if (Available(checked(oldEpoch + 1)))
        {
            return true;
        }

        if (_pending is null && Fresh && _record is { } record && record.Epoch == oldEpoch && record.RemainingSeconds == 0 && record.Holder != subject)
        {
            Begin("takeover", new(record.Session, oldEpoch, record.Token));
        }

        return false;
    }

    /// <summary>Validates successor routing against the trusted service.</summary>
    /// <param name="epoch">Successor epoch.</param>
    /// <param name="holder">Elected subject.</param>
    /// <returns>Whether the service confirms this successor.</returns>
    internal bool Confirms(ulong epoch, string holder) => Fresh && _record is { } record && record.Epoch == epoch && record.Holder == holder && record.RemainingSeconds > 0;

    private void Begin(string operation, LeaseRequest request)
    {
        _operation = operation;
        _requested = time.GetTimestamp();
        _polled = _requested;
        _pending = transport.Send(operation, request);
    }
}
