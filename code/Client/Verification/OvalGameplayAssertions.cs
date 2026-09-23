using Godot;
using Trackstorm.Client.Arenas;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Arenas;

namespace Trackstorm.Client.Verification;

/// <summary>Checks the actual map selection used by production entry, resume and migration harnesses.</summary>
internal static class OvalGameplayAssertions
{
    /// <summary>Fails if a normal session includes old map content or authority uses different markers.</summary>
    /// <param name="arena">An ordinary live network arena.</param>
    internal static void Verify(NetworkVehicleArena arena)
    {
        VerifyMap(arena.Map, arena.MapConfiguration);
        if (arena.Driver.Host is { } host && !host.World.Arena.Players.SequenceEqual(arena.MapConfiguration.Players))
        {
            throw new InvalidOperationException("Host authority lost the scene-owned oval spawn contract.");
        }

        if (arena.Driver.ObserveProps is not null || arena.Driver.PropSnapshot is not null || arena.Pickups.ActiveCount != arena.Driver.ItemState?.Spawns.Count(spawn => spawn.Available) || !arena.Audio.MusicPlaying)
        {
            throw new InvalidOperationException("Oval must have no legacy props and must present authoritative pickups and must preserve arena music.");
        }
    }

    /// <summary>Checks that the loaded map and its plain-data contract agree without old spawn coordinates.</summary>
    /// <param name="map">Loaded reusable scene.</param>
    /// <param name="configuration">Contract supplied to the gameplay simulation.</param>
    internal static void VerifyMap(Node3D map, ArenaConfiguration configuration)
    {
        var authored = ActiveMap.ReadConfiguration(map);
        if (map.SceneFilePath != ActiveMap.ScenePath || !configuration.Players.SequenceEqual(authored.Players) || configuration.Items.Count != 20 || !configuration.Items.SequenceEqual(authored.Items) ||
            configuration.Players.Any(marker => PrototypeArena.Configuration.Players.Any(old => old.Position == marker.Position)) ||
            map.FindChildren("*", "RigidBody3D", true, false).Count != 0 || map.FindChildren("*", "CombatArena", true, false).Count != 0)
        {
            throw new InvalidOperationException("Normal gameplay must use the oval's eight authored grid slots without old-map content.");
        }
    }
}
