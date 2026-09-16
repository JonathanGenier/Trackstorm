using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Client.Networking;

/// <summary>Selects transport diagnostics for presentation without assigning player identities.</summary>
internal static class TransportDiagnostics
{
    /// <summary>Projects only the current connection. No value survives provider, session or peer replacement.</summary>
    /// <param name="gateway">Current session's transport.</param>
    /// <param name="lobby">Current admission and recovery state.</param>
    /// <returns>Detached current values; recovery never exposes latency.</returns>
    internal static ConnectionDiagnostic Capture(ITransportGateway? gateway, LobbyNetworkDriver? lobby)
    {
        if (gateway is null || lobby is null || lobby.LeaveComplete || lobby.Failure.Length > 0)
        {
            return default;
        }

        if (lobby.Reconnecting || lobby.NeedsArenaCheckpoint)
        {
            return new(ConnectionDiagnosticState.Reconnecting, default, gateway.Name);
        }

        if (lobby.Authority is not null)
        {
            // The host has no upstream connection. Do not label an arbitrary client's RTT as local ping.
            return new(ConnectionDiagnosticState.Unavailable, default, gateway.Name);
        }

        if (!gateway.Connections.TryGetValue(lobby.ServerPeer, out var state) || state == TransportConnectionState.Disconnected)
        {
            return new(ConnectionDiagnosticState.Disconnected, default, gateway.Name);
        }

        return state == TransportConnectionState.Connected && lobby.State is not null
            ? new(ConnectionDiagnosticState.Connected, gateway.GetStatistics(lobby.ServerPeer), gateway.Name)
            : new(ConnectionDiagnosticState.Connecting, default, gateway.Name);
    }
}
