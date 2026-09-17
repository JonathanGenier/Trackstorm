using Trackstorm.Client.Online;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Transport.Tests;

/// <summary>Deterministic transport to the test-only lease model, without HTTP scheduling.</summary>
internal sealed class LeaseTransport(LeaseStore store, string subject) : ILeaseTransport
{
    /// <summary>Suppresses service access.</summary>
    internal bool Offline { get; set; }
    /// <summary>Holds completion after the server has processed a request.</summary>
    internal bool Delay { get; set; }
    /// <summary>The held response completion.</summary>
    internal TaskCompletionSource<AuthorityLease?>? Pending { get; private set; }
    /// <summary>Server result awaiting delivery.</summary>
    internal AuthorityLease? Response { get; private set; }

    /// <inheritdoc/>
    public Task<AuthorityLease?> Send(string operation, LeaseRequest request)
    {
        Response = Offline ? null : operation == "read" ? store.Read(request.Session) : store.Execute(operation, request, subject);
        if (Delay)
        {
            Pending = new();
            return Pending.Task;
        }

        return Task.FromResult(Response);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}
