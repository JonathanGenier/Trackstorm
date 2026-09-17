using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Online;

/// <summary>Trusted asynchronous fencing service. Failures never imply a grant.</summary>
internal interface ILeaseTransport : IDisposable
{
    /// <summary>Runs one conditional authenticated operation.</summary>
    /// <param name="operation">Operation name.</param>
    /// <param name="request">Expected session and fence.</param>
    /// <returns>A trusted result or no grant on failure.</returns>
    Task<AuthorityLease?> Send(string operation, LeaseRequest request);
}
