using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Client.Networking;

/// <summary>Selects transport diagnostics for presentation without assigning player identities.</summary>
internal static class TransportDiagnostics
{
    /// <summary>Finds the first available ping among connected peers.</summary>
    /// <param name="gateway">The optional active transport gateway.</param>
    /// <returns>A sampled ping, or null when no connected peer has an available sample.</returns>
    internal static int? GetPing(ITransportGateway? gateway)
    {
        if (gateway is null)
        {
            return null;
        }

        foreach (var peer in gateway.Connections)
        {
            if (peer.Value == TransportConnectionState.Connected &&
                gateway.GetStatistics(peer.Key).PingMilliseconds is int ping)
            {
                return ping;
            }
        }

        return null;
    }
}
