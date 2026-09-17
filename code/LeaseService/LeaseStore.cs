using System.Text.Json;
using Trackstorm.Core.Sessions;

namespace Trackstorm.LeaseService;

/// <summary>Single-process durable conditional grants. An exclusive file lock prohibits multiple writers.</summary>
internal sealed class LeaseStore : IDisposable
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly TimeProvider _time;
    private readonly FileStream _lock;
    private readonly Dictionary<string, AuthorityLease> _records;
    private readonly Dictionary<string, long> _renewed = new();
    private readonly long _started;

    /// <summary>Opens the exclusive durable ledger and starts restart quarantine.</summary>
    /// <param name="path">Operator-owned persistent ledger path.</param>
    /// <param name="time">Monotonic service clock.</param>
    internal LeaseStore(string path, TimeProvider? time = null)
    {
        _path = Path.GetFullPath(path);
        _time = time ?? TimeProvider.System;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _lock = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            _records = File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, AuthorityLease>>(File.ReadAllBytes(_path)) ?? throw new InvalidDataException("Invalid lease ledger.")
                : new();
            if (_records.Any(pair => pair.Key != pair.Value.Session || !ValidSession(pair.Key) || pair.Value.Epoch == 0 || string.IsNullOrWhiteSpace(pair.Value.Holder) || !Guid.TryParseExact(pair.Value.Token, "N", out _)))
            {
                throw new InvalidDataException("Invalid lease ledger.");
            }

            _started = _time.GetTimestamp();
        }
        catch
        {
            _lock.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _lock.Dispose();

    /// <summary>Reads current service-clock expiry.</summary>
    /// <param name="session">Opaque session.</param>
    /// <returns>Current grant or absence.</returns>
    internal AuthorityLease? Read(string session)
    {
        lock (_gate)
        {
            return ValidSession(session) && _records.TryGetValue(session, out var record) ? Snapshot(record) : null;
        }
    }

    /// <summary>Serializes conditional state changes before acknowledging a grant.</summary>
    /// <param name="operation">Requested operation.</param>
    /// <param name="request">Expected fence.</param>
    /// <param name="subject">Verified authentication subject.</param>
    /// <returns>Committed grant or rejection.</returns>
    internal AuthorityLease? Execute(string operation, LeaseRequest request, string subject)
    {
        if (!ValidSession(request.Session) || request.Token is null || string.IsNullOrWhiteSpace(subject) || subject.Length > 256)
        {
            return null;
        }

        lock (_gate)
        {
            _records.TryGetValue(request.Session, out var current);
            if (operation == "create")
            {
                if (request.Epoch != 0 || request.Token.Length != 0 || current is not null || _records.Count >= 10000)
                {
                    return null;
                }

                return Commit(new(request.Session, subject, 1, Guid.NewGuid().ToString("N"), AuthorityLease.DurationSeconds));
            }

            if (current is null || current.Epoch != request.Epoch || current.Token != request.Token)
            {
                return null;
            }

            if (operation == "renew" && current.Holder == subject && _renewed.ContainsKey(request.Session) && Snapshot(current).RemainingSeconds > 0)
            {
                // A changed token makes continuing old-host progress observable without trusting cached expiry.
                return Commit(current with { Token = Guid.NewGuid().ToString("N") });
            }

            if (operation == "release" && current.Holder == subject)
            {
                _renewed[request.Session] = _time.GetTimestamp() - (long)(AuthorityLease.DurationSeconds * _time.TimestampFrequency);
                return Snapshot(current);
            }

            if (operation == "takeover" && current.Holder != subject && current.Epoch < ulong.MaxValue && Snapshot(current).RemainingSeconds == 0)
            {
                return Commit(current with { Holder = subject, Epoch = current.Epoch + 1, Token = Guid.NewGuid().ToString("N") });
            }

            return null;
        }
    }

    private static bool ValidSession(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private AuthorityLease Snapshot(AuthorityLease record)
    {
        // Following restart, never trust a persisted wall clock. Quarantine for the full old lease.
        long boundary = _renewed.GetValueOrDefault(record.Session, _started);
        return record with { RemainingSeconds = Math.Max(0, AuthorityLease.DurationSeconds - _time.GetElapsedTime(boundary).TotalSeconds) };
    }

    private AuthorityLease Commit(AuthorityLease record)
    {
        var updated = new Dictionary<string, AuthorityLease>(_records) { [record.Session] = record };
        using (var file = new FileStream(_path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(file, updated);
            file.Flush(true);
        }

        File.Move(_path + ".tmp", _path, true);
        _records[record.Session] = record;
        _renewed[record.Session] = _time.GetTimestamp();
        return Snapshot(record);
    }
}
