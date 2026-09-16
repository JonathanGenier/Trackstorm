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
        return $"Transport: {session.Gateway?.Name ?? "none"}; connection: {connection.State}\n" +
            $"Session: {lobby?.State?.Session.ToString() ?? "none"}; phase: {lobby?.State?.Phase.ToString() ?? "none"}; host: {(lobby?.State is null ? "none" : "Player 1")}\n" +
            $"Failure: {(lobby?.Failure.Length > 0 || arena?.Driver.Failure.Length > 0 ? "failed" : "none")}; RTT: {samples}\n" +
            $"Quality in/out: {connection.Statistics.IncomingQuality?.ToString("P1") ?? "N/A"} / {connection.Statistics.OutgoingQuality?.ToString("P1") ?? "N/A"}\n" +
            $"Reconnect: {lobby?.Reconnecting == true}; grace: {lobby?.State?.GraceTicks.ToString() ?? "N/A"} ticks; resume checkpoint pending: {lobby?.NeedsArenaCheckpoint == true}\n" +
            $"Connection generation: {lobby?.Generation ?? 0}; configuration revision: {arena?.Driver.Configuration.Revision ?? 0}\n" +
            "AuthorityEpoch / migration checkpoint: unavailable until host migration is integrated.\n" +
            (arena?.DeveloperDiagnostics ?? "Arena prediction: unavailable.");
    }
}
