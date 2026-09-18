using Trackstorm.Client.Networking;
using Trackstorm.Client.Online;

namespace Trackstorm.Client.Development;

/// <summary>Allowlisted diagnostic projection. Credentials, raw identity, metadata and exceptions are never formatted.</summary>
internal static class DeveloperDiagnostics
{
    /// <summary>Formats only lifecycle state and a one-way identity fingerprint.</summary>
    /// <returns>Safe identity lifecycle and one-way fingerprint.</returns>
    /// <param name="state">Current safe identity lifecycle.</param>
    /// <param name="identity">Optional online identity, formatted only as a one-way fingerprint.</param>
    internal static string Identity(OnlineIdentityState state, OnlineProductUserId? identity) =>
        $"EOS: {state}; identity: {identity?.ToString() ?? "unavailable"}";

    /// <summary>Projects allowlisted session diagnostics without authentication material.</summary>
    /// <returns>Allowlisted current runtime diagnostics.</returns>
    /// <param name="session">Current session owner.</param>
    internal static string Capture(DevelopmentSession? session)
    {
        if (session is null)
        {
            return "No multiplayer session.";
        }

        var lobby = session.Lobby;
        var arena = session.Arena;
        var connection = session.Diagnostics;
        string samples = lobby?.State is { } roster
            ? string.Join(", ", roster.Players.Select(player => $"P{player.Id}: {(lobby.Latency.Get(roster, player.Id) is int ping ? $"{ping} ms" : "N/A")}")) : "N/A";
        string rosterState = lobby?.State is { } state
            ? string.Join(", ", state.Players.Select(player => $"P{player.Id} connected={player.Connected} ready={player.Ready} generation={player.Generation}")) : "N/A";
        return $"Transport: {session.Gateway?.Name ?? "none"}; connection: {connection.State}\n" +
            $"Local PlayerId: {lobby?.LocalPlayerId ?? 0}; CurrentHostId: {lobby?.State?.CurrentHostId ?? 0}; role: {(lobby?.State is null ? "UNASSIGNED" : lobby.LocalPlayerId == lobby.State.CurrentHostId ? "HOST" : "CLIENT")}\n" +
            $"Session: {lobby?.State?.Session.ToString() ?? "none"}; match generation: {lobby?.State?.Match ?? 0}; phase: {lobby?.State?.Phase.ToString() ?? "none"}\n" +
            $"Failure: {(lobby?.Failure.Length > 0 || arena?.Driver.Failure.Length > 0 ? "failed" : "none")}; RTT: {samples}\n" +
            $"Trackstorm roster: {rosterState}\n" +
            $"Quality in/out: {connection.Statistics.IncomingQuality?.ToString("P1") ?? "N/A"} / {connection.Statistics.OutgoingQuality?.ToString("P1") ?? "N/A"}\n" +
            $"Reconnect: {lobby?.Reconnecting == true}; policy: {lobby?.State?.ReconnectPolicy.ToString() ?? "N/A"}; resume checkpoint pending: {lobby?.NeedsArenaCheckpoint == true}\n" +
            $"Connection generation: {lobby?.Generation ?? 0}; configuration revision: {arena?.Driver.Configuration.Revision ?? lobby?.Authority?.Configuration.Revision ?? lobby?.Migration?.ConfigurationRevision ?? 0}\n" +
            $"AuthorityEpoch: {lobby?.State?.AuthorityEpoch ?? 0}; migration: {lobby?.Migration?.Diagnostics ?? "unavailable"}\n" +
            (arena?.DeveloperDiagnostics ?? "Arena prediction: unavailable.");
    }
}
