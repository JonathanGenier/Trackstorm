namespace Trackstorm.Core.Networking.Transport;

/// <summary>A provider-neutral target. The selected adapter validates its opaque address and session context.</summary>
public sealed record TransportEndpoint
{
    private TransportEndpoint(string address, string? session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        if (address.Length > 256 || session?.Length > 256 || (session is not null && string.IsNullOrWhiteSpace(session)))
        {
            throw new ArgumentException("Transport endpoint values must contain 1–256 characters.");
        }

        Address = address;
        Session = session;
    }

    /// <summary>Opaque peer identity or numeric development endpoint, interpreted only by the adapter.</summary>
    public string Address { get; }
    /// <summary>Opaque online session context; absent for Direct-IP.</summary>
    public string? Session { get; }
    /// <summary>Creates a development IP target. Native address validation remains adapter-owned.</summary>
    /// <param name="address">Numeric address and port interpreted by the Direct-IP adapter.</param>
    /// <returns>A development endpoint.</returns>
    public static TransportEndpoint DirectIp(string address) => new(address, null);
    /// <summary>Creates a peer target scoped to an online session, without importing a provider's identity type.</summary>
    /// <param name="peer">Opaque provider identity.</param>
    /// <param name="session">Opaque session context.</param>
    /// <returns>A session-scoped peer endpoint.</returns>
    public static TransportEndpoint PeerSession(string peer, string session) => new(peer, session ?? throw new ArgumentNullException(nameof(session)));
    /// <inheritdoc/>
    public override string ToString() => Session is null ? "Direct-IP endpoint" : "Online peer/session endpoint";
}
