using Trackstorm.Client.Development;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Statistics;

/// <summary>Reads current scene owners on demand. No credentials, raw failures or mutation callbacks cross this boundary.</summary>
internal static class RuntimeStatistics
{
    /// <summary>Captures diagnostic projections from the current owner instances.</summary>
    /// <param name="session">Current multiplayer owner, including lobby-only state.</param>
    /// <param name="practice">Optional isolated practice owner.</param>
    /// <param name="selected">Previously selected stable identity.</param>
    /// <param name="identityDiagnostics">Credential-safe online identity and coordination state.</param>
    /// <returns>New display data, discarded on the next refresh.</returns>
    internal static StatisticView Capture(DevelopmentSession? session, VehicleArena? practice, ulong selected, string identityDiagnostics = "EOS unavailable.")
    {
        var lobby = session?.Lobby;
        var roster = lobby?.State;
        var arena = session?.Arena;
        var driver = arena?.Driver;
        IReadOnlyList<VehicleSnapshot> vehicles = practice?.Simulation.State.Vehicles ?? driver?.Host?.World.State.Vehicles ?? driver?.Latest?.Vehicles.Select(value => value.State).ToArray() ?? [];
        ulong tick = practice?.Simulation.State.Tick ?? driver?.Host?.World.State.Tick ?? driver?.Latest?.Tick ?? 0;
        ulong local = practice?.Player.VehicleId ?? lobby?.LocalPlayerId ?? driver?.LocalVehicleId ?? 0;
        ulong[] players = (roster?.Players.Select(value => value.Id) ?? vehicles.Select(value => value.VehicleId)).Order().ToArray();
        selected = players.Contains(selected) ? selected : players.Contains(local) ? local : players.FirstOrDefault();
        IReadOnlyList<ItemSlot>? slots = driver?.Host?.Items.Slots ?? driver?.ItemState?.Slots;
        IReadOnlyList<ItemSpawnState>? spawns = driver?.Host?.Spawns?.States ?? driver?.ItemState?.Spawns;
        var missiles = driver?.Host?.Items.Missiles ?? driver?.ItemState?.Missiles;
        var mines = driver?.Host?.Items.Mines ?? driver?.ItemState?.Mines;
        var patches = driver?.Host?.Items.Patches ?? driver?.ItemState?.Patches;
        var match = driver?.Host?.World.State.Match ?? driver?.Match;
        string role = practice is not null ? "LOCAL PRACTICE" : lobby is null ? "NO SESSION" : lobby.Authority is not null ? "HOST" : "CLIENT";
        string connection = practice is not null ? "Local only" : lobby?.Authority is not null ? "Hosting (no upstream connection)" : session?.Diagnostics.State.ToString() ?? "Unavailable";
        string sessionText = $"Role: {role} · Connection: {connection}\nSession phase: {roster?.Phase.ToString() ?? "Unavailable"} · Session: {roster?.Session.ToString() ?? "Unavailable"}\n" +
            $"Host: {(roster is not null ? "Player 1" : practice is not null ? "Local authority" : "Unavailable")}\n" +
            $"Connected players: {(roster is not null ? $"{roster.Players.Count(value => value.Connected)}/{ArenaConfiguration.SpawnCount}" : practice is not null ? "1 local driver" : "Unavailable")} · Vehicles: {vehicles.Count}/{ArenaConfiguration.SpawnCount}\n" +
            $"World tick: {(practice is not null || driver?.Latest is not null ? tick.ToString() : "Unavailable")} · Configuration revision: {driver?.Configuration.Revision.ToString() ?? "Unavailable"}\n" +
            $"Authority epoch: {roster?.AuthorityEpoch.ToString() ?? "Unavailable"} · Host migration: {lobby?.Migration?.Diagnostics ?? "Unavailable"}";
        string arenaText = $"Arena: {(practice is not null || arena is not null ? "Industrial yard" : "Unavailable")}\n" + MatchText(match, tick) +
            $"\nActive Proxy Mines: {mines?.Count.ToString() ?? "Unavailable"}\nActive oil patches: {patches?.Count.ToString() ?? "Unavailable"}\nActive projectiles: {missiles?.Count.ToString() ?? "Unavailable"}\n" +
            (spawns is null ? "Item spawns: unavailable" : $"Available spawns: {spawns.Count(value => value.Available)}/{spawns.Count}\n" + string.Join("\n", spawns.Select(value => $"{value.Id}: {(value.Available ? "Available" : "Cooldown " + VehicleStatistics.Remaining(value.NextActivationTick, tick))}")));
        string networkText = $"Transport: {session?.Gateway?.Name ?? "Unavailable"}\nReplication: {(driver is null ? "Unavailable" : driver.Failure.Length > 0 ? "Failed" : driver.IsActive ? "Active" : "Suspended / awaiting authority")}\n" +
            $"Snapshot age: {NetworkVehicleArena.FormatSnapshotAge(driver?.SnapshotAge)}\nInterpolation delay: {(driver?.Host is null && driver?.History is not null ? $"{arena!.InterpolationDelay:0} ms" : "Unavailable")}\n" +
            $"Reconnect: {lobby?.Reconnecting.ToString() ?? "Unavailable"} · Awaiting arena checkpoint: {lobby?.NeedsArenaCheckpoint.ToString() ?? "Unavailable"}\nReconnect policy: {roster?.ReconnectPolicy.ToString() ?? "Unavailable"} · Connection generation: {lobby?.Generation.ToString() ?? "Unavailable"}";
        string diagnosticsText = identityDiagnostics + "\n" + DeveloperDiagnostics.Capture(session);
        SurfaceIdentity? detectedSurface = practice?.Player.DetectedSurface ?? (arena is not null && arena.Bodies.TryGetValue(local, out var localBody) ? localBody.DetectedSurface : null);
        StatisticSection[] global = [new("Local surface / material", "Detected surface: " + SurfaceIdentityResolver.Describe(detectedSurface) + "\nSource: local native support"), new("Session / authority", sessionText), new("Arena / match / spawning", arenaText), new("Networking / synchronization", networkText), new("Network Diagnostics", diagnosticsText)];
        if (selected == 0)
        {
            return new(players, 0, global, [new("Player / vehicle", "No player or vehicle available.")]);
        }

        var player = VehicleStatistics.Capture(vehicles.SingleOrDefault(value => value.VehicleId == selected), slots, tick).ToList();
        var balances = driver?.Host?.Spawns?.Balances ?? driver?.ItemState?.Balances;
        var balance = balances?.SingleOrDefault(b => b.Player == selected);
        player.Add(new("Item category balance", balances is null ? "Unavailable" :
            $"Total pickups: {balance?.Total ?? 0} · Last category: {balance?.SelectedCategory?.ToString() ?? "None"} · Last item: {balance?.SelectedItem.ToString() ?? "None"}\n" +
            string.Join("\n", ItemRegistry.Categories.Select(c => $"{c.Identity}: credit {balance?.Credits[c.Identity] ?? 0:0.######} · pickups {balance?.Counts[c.Identity] ?? 0}"))));
        var member = roster?.Players.SingleOrDefault(value => value.Id == selected);
        int? ping = roster is null || lobby is null ? null : lobby.Latency.Get(roster, selected);
        var rank = match is null ? null : MatchRanking.Create(match, players).SingleOrDefault(value => value.PlayerId == selected);
        string playerText = $"Player {selected}{(selected == local ? " (local)" : string.Empty)}\nConnected: {member?.Connected.ToString() ?? "Unavailable"} · Ready: {member?.Ready.ToString() ?? "Unavailable"}\n" +
            $"RTT: {(ping is { } ms ? $"{ms} ms" : "Unavailable")}\nRank: {rank?.Rank.ToString() ?? "Unavailable"} · Kills: {rank?.Kills.ToString() ?? "Unavailable"} · Deaths: {rank?.Deaths.ToString() ?? "Unavailable"}";
        player.Insert(0, new("Player / session", playerText));
        var prediction = selected == local ? driver?.Prediction : null;
        uint? acknowledgement = driver?.Host is not null && selected == local ? null : driver?.Latest?.Vehicles.SingleOrDefault(value => value.State.VehicleId == selected)?.AcknowledgedInput;
        var quality = selected == local ? session?.Diagnostics.Statistics : null;
        int? hardSnaps = selected == local && prediction is not null && arena!.Bodies.TryGetValue(selected, out var body) ? body.Smoothing.HardSnaps : null;
        string predictionText = $"Prediction error: {(prediction is not null ? $"{prediction.PredictionError:0.000} m (local client)" : "Unavailable (no local client prediction)")}\n" +
            $"Local corrections >=3m: {hardSnaps?.ToString() ?? "Unavailable"}\nLast acknowledged input: {acknowledgement?.ToString() ?? "Unavailable"}\n" +
            $"Incoming/outgoing quality: {quality?.IncomingQuality?.ToString("P1") ?? "Unavailable"} / {quality?.OutgoingQuality?.ToString("P1") ?? "Unavailable"}\n" +
            $"Estimated incoming/outgoing loss: {quality?.IncomingLoss?.ToString("P1") ?? "Unavailable"} / {quality?.OutgoingLoss?.ToString("P1") ?? "Unavailable"}";
        player.Add(new("Player networking / prediction", predictionText));
        return new(players, selected, global, player);
    }

    private static string MatchText(MatchState? match, ulong tick) => match is null ? "Match rules: unavailable in this context" :
        $"Match: {match.Phase} · First to {match.KillTarget} kills · Winner: {match.Winner?.ToString() ?? "None"}\n" +
        (match.CountdownAtTick is { } deadline ? "Countdown: " + VehicleStatistics.Remaining(deadline, tick) : "Match time limit: none (kill-target rules)");
}
