using Godot;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Networking;

/// <summary>Incremental match-only loading, separate from reusable frontend initialization.</summary>
internal sealed class MatchResourceLoader
{
    private static readonly string[] Resources =
    [
        "res://assets/items/materials/DamageFlash.gdshader",
        "res://assets/audio/project/music/Welcome to the Carnage Circus Arena.mp3",
        "res://assets/audio/project/music/Welcome to the Carnage Circus Arena 2.mp3",
        "res://assets/audio/project/music/Welcome to the Carnage Circus Arena 3.mp3",
        "res://assets/audio/freesound/arena/ambience.wav",
        "res://assets/audio/freesound/combat/death.wav",
        "res://assets/audio/freesound/combat/destruction.wav",
        "res://assets/audio/freesound/combat/end.wav",
        "res://assets/audio/freesound/combat/explosion.wav",
        "res://assets/audio/freesound/combat/fire.wav",
        "res://assets/audio/freesound/vehicle/high.wav",
        "res://assets/audio/freesound/vehicle/idle.wav",
        "res://assets/audio/freesound/vehicle/low.wav",
        "res://assets/audio/freesound/vehicle/skid.wav",
        "res://assets/audio/kenney/impact/impactMetal_heavy_000.ogg",
        "res://assets/audio/kenney/impact/impactMetal_light_000.ogg",
        "res://assets/audio/kenney/impact/impactMetal_medium_000.ogg",
        "res://assets/audio/kenney/impact/impactMetal_medium_001.ogg",
        "res://assets/audio/kenney/scifi/engineCircular_000.ogg",
        "res://assets/audio/kenney/scifi/forceField_000.ogg",
        "res://assets/audio/kenney/scifi/forceField_001.ogg",
        "res://assets/audio/kenney/scifi/forceField_002.ogg",
    ];
    private readonly string[] _paths;
    private readonly List<Resource> _retained = new();
    private int _index;
    private bool _requested;

    /// <summary>Chooses only the authoritative map and match-specific resources.</summary>
    /// <param name="map">Supported authoritative map.</param>
    internal MatchResourceLoader(MatchMap map)
    {
        _paths = Resources.Append(map == MatchMap.OldMap ? "res://scenes/arena/prototype_arena.tscn" : Arenas.ActiveMap.ScenePath).ToArray();
    }

    /// <summary>All required resources have loaded successfully.</summary>
    internal bool Complete => _index == _paths.Length;
    /// <summary>Normalized completed resource progress.</summary>
    internal double Progress => (double)_index / _paths.Length;
    /// <summary>Validated selected map resource.</summary>
    internal PackedScene? MapScene { get; private set; }

    /// <summary>Requests or polls one resource without blocking on an unfinished load.</summary>
    internal void Advance()
    {
        if (Complete)
        {
            return;
        }

        string path = _paths[_index];
        if (!_requested)
        {
            // Rematches usually reuse assets still held by the retiring arena. Acquire that
            // reference on the main thread; do not send an already-live managed resource
            // through another worker-thread reference-count/GC-handle handoff.
            if (ResourceLoader.GetCachedRef(path) is { } cached)
            {
                Retain(cached);
                return;
            }

            // Keep dependency loads on the request's worker. Godot 4.7.2's distributed
            // dependency path leaves zero-reference LoadTokens behind for the oval's
            // three external resources. The request remains asynchronous; polling and
            // LoadThreadedGet still own its completion before retaining the resource.
            if (ResourceLoader.LoadThreadedRequest(path, useSubThreads: false) != Error.Ok)
            {
                throw new InvalidOperationException("Could not request a required match resource.");
            }

            _requested = true;
            return;
        }

        var status = ResourceLoader.LoadThreadedGetStatus(path);
        if (status == ResourceLoader.ThreadLoadStatus.InProgress)
        {
            return;
        }

        if (status != ResourceLoader.ThreadLoadStatus.Loaded)
        {
            throw new InvalidOperationException("A required match resource could not be loaded.");
        }

        Resource resource = ResourceLoader.LoadThreadedGet(path) ?? throw new InvalidOperationException("Missing match resource.");
        Retain(resource);
    }

    /// <summary>Releases a cancelled asynchronous request once its worker finishes, without blocking frames.</summary>
    internal bool DiscardPending(bool wait = false)
    {
        if (!_requested) return true;
        var status = ResourceLoader.LoadThreadedGetStatus(_paths[_index]);
        if (!wait && status == ResourceLoader.ThreadLoadStatus.InProgress) return false;
        if (status is ResourceLoader.ThreadLoadStatus.Loaded or ResourceLoader.ThreadLoadStatus.InProgress)
            ResourceLoader.LoadThreadedGet(_paths[_index]);
        _requested = false;
        return true;
    }

    private void Retain(Resource resource)
    {
        _retained.Add(resource);
        if (_index == _paths.Length - 1)
        {
            MapScene = resource as PackedScene ?? throw new InvalidOperationException("Invalid selected map scene.");
        }

        _index++;
        _requested = false;
    }
}
