using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Arenas;

/// <summary>Validated immutable arena contract shared by spawning and scene inspection.</summary>
public sealed class ArenaConfiguration
{
    /// <summary>Required capacity of the prototype.</summary>
    public const int SpawnCount = 8;

    /// <summary>Copies and validates authored markers, bounds and surface identifiers.</summary>
    /// <param name="minimum">Inclusive lower world bounds.</param>
    /// <param name="maximum">Inclusive upper world bounds.</param>
    /// <param name="players">Stable player slots.</param>
    /// <param name="items">Stable item locations.</param>
    /// <param name="surfaces">Authored surface identifiers.</param>
    public ArenaConfiguration(Vector3 minimum, Vector3 maximum, IEnumerable<ArenaSpawn> players, IEnumerable<ArenaSpawn> items, IEnumerable<SurfaceType> surfaces)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(surfaces);
        if (!VehiclePhysicsState.IsFinite(minimum) || !VehiclePhysicsState.IsFinite(maximum) || minimum.X >= maximum.X || minimum.Y >= maximum.Y || minimum.Z >= maximum.Z)
        {
            throw new ArgumentException("Arena bounds must be finite and have positive extent.");
        }

        Minimum = minimum;
        Maximum = maximum;
        ArenaSpawn[] playerArray = players.ToArray();
        ArenaSpawn[] itemArray = items.ToArray();
        SurfaceType[] surfaceArray = surfaces.ToArray();
        if (playerArray.Length != SpawnCount || itemArray.Length != SpawnCount)
        {
            throw new ArgumentException("Arena requires exactly eight player and eight item markers.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (ArenaSpawn spawn in playerArray.Concat(itemArray))
        {
            if (spawn is null || string.IsNullOrWhiteSpace(spawn.Id) || spawn.Id != spawn.Id.Trim() || !ids.Add(spawn.Id))
            {
                throw new ArgumentException("Spawn IDs must be present, canonical and globally unique.");
            }

            if (!Contains(spawn.Position) || !float.IsFinite(spawn.Yaw))
            {
                throw new ArgumentException($"Spawn {spawn.Id} has an invalid pose or lies outside arena bounds.");
            }
        }

        for (int i = 0; i < playerArray.Length; i++)
        {
            for (int j = i + 1; j < playerArray.Length; j++)
            {
                Vector3 difference = playerArray[i].Position - playerArray[j].Position;
                if ((difference.X * difference.X) + (difference.Z * difference.Z) < 25)
                {
                    throw new ArgumentException("Player markers require five metres of horizontal clearance.");
                }
            }
        }

        if (surfaceArray.Length == 0 || surfaceArray.Any(surface => !Enum.IsDefined(surface)))
        {
            throw new ArgumentException("Arena surfaces must contain known surface identifiers.");
        }

        Players = Array.AsReadOnly(playerArray);
        Items = Array.AsReadOnly(itemArray);
        Surfaces = Array.AsReadOnly(surfaceArray);
    }

    /// <summary>Inclusive world bounds.</summary>
    public Vector3 Minimum { get; }
    /// <summary>Inclusive world bounds.</summary>
    public Vector3 Maximum { get; }
    /// <summary>Stable slot order used by the host.</summary>
    public IReadOnlyList<ArenaSpawn> Players { get; }
    /// <summary>Stable pickup locations registered by the network arena.</summary>
    public IReadOnlyList<ArenaSpawn> Items { get; }
    /// <summary>Authored supporting surface identifiers.</summary>
    public IReadOnlyList<SurfaceType> Surfaces { get; }

    /// <summary>Checks finite coordinates against the configured volume.</summary>
    /// <param name="position">World point.</param>
    /// <returns>Whether the point lies inside the volume.</returns>
    public bool Contains(Vector3 position) => VehiclePhysicsState.IsFinite(position) && position.X >= Minimum.X && position.X <= Maximum.X && position.Y >= Minimum.Y && position.Y <= Maximum.Y && position.Z >= Minimum.Z && position.Z <= Maximum.Z;

    /// <summary>Resolves an authored slot to a stationary initial vehicle state.</summary>
    /// <param name="slot">Zero-based authored slot.</param>
    /// <returns>Stationary initial physics state.</returns>
    public VehiclePhysicsState Spawn(int slot)
    {
        ArenaSpawn spawn = Players[slot];
        return new VehiclePhysicsState(spawn.Position, Quaternion.CreateFromAxisAngle(Vector3.UnitY, spawn.Yaw), Vector3.Zero, Vector3.Zero);
    }
}
