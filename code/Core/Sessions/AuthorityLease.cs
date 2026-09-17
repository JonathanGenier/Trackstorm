namespace Trackstorm.Core.Sessions;

/// <summary>Coordination-only exclusive grant; contains no gameplay state or election decision.</summary>
/// <param name="Session">Opaque coordination session shared only with admitted peers.</param>
/// <param name="Holder">Authenticated provider-neutral subject.</param>
/// <param name="Epoch">Trackstorm's proposed authority epoch.</param>
/// <param name="Token">Unique fencing token, replaced on renewal and takeover.</param>
/// <param name="RemainingSeconds">Service-observed remaining permission, never a client deadline.</param>
public sealed record AuthorityLease(string Session, string Holder, ulong Epoch, string Token, double RemainingSeconds)
{
    /// <summary>Service lease length. Clients deliberately stop earlier.</summary>
    public const double DurationSeconds = 10;
    /// <summary>Conservative client lifetime measured from request start, including transit and suspension.</summary>
    public const double ClientDurationSeconds = 8;
}
