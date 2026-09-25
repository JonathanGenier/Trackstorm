using Godot;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Audio;

/// <summary>Arena-owned audio presentation. Teardown owns every voice and all playlist state.</summary>
internal sealed partial class ArenaAudio : Node3D
{
    private readonly AudioEventProjection _events = new();
    private readonly ArenaPlaylist _playlist = new(Random.Shared.Next);
    private readonly AudioStreamPlayer _music = new() { Bus = "Music", VolumeDb = -14 };
    private readonly Dictionary<ulong, VehicleAudio> _vehicles = new();
    private readonly Dictionary<ulong, AudioStreamPlayer3D> _rockets = new();
    private readonly List<Node> _voices = new();
    private MatchState? _match;

    /// <summary>Number of submitted voices for native verification.</summary>
    internal int CueCount { get; private set; }
    /// <summary>Current playlist position for native verification.</summary>
    internal int TrackIndex => _playlist.Index;
    /// <summary>Whether the native music player is running.</summary>
    internal bool MusicPlaying => _music.Playing;

    /// <inheritdoc/>
    public override void _Ready()
    {
        AudioBuses.Ensure();
        _events.Cue += Play;
        AddChild(_music);
        _music.Finished += NextSong;
        if (_playlist.Start())
        {
            PlaySong();
        }

        var ambience = new AudioStreamPlayer { Stream = LoopStream(AudioCue.Ambience), Bus = "SFX", VolumeDb = -27 };
        AddChild(ambience);
        ambience.Play();
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        _events.Cue -= Play;
        _music.Finished -= NextSong;
        _playlist.Stop();
        StopPlayers(this);
    }

    /// <summary>Creates a caller-owned spatial loop routed to its category bus.</summary>
    /// <param name="cue">Selected presentation category.</param>
    /// <returns>The selected presentation value.</returns>
    internal static AudioStreamPlayer3D Loop3D(AudioCue cue) => new()
    {
        Stream = LoopStream(cue),
        Bus = AudioRouting.Bus(cue),
        MaxDistance = 70,
        UnitSize = 9,
        VolumeDb = -80,
        MaxPolyphony = 1,
    };

    /// <summary>Binds personal feedback to the local vehicle identity.</summary>
    /// <param name="localVehicle">Host-assigned local vehicle identity.</param>
    internal void Initialize(ulong localVehicle) => _events.LocalVehicle = localVehicle;

    /// <summary>Presents the existing local-authority practice blast once at its origin.</summary>
    /// <param name="position">Accepted practice blast position.</param>
    internal void PracticeExplosion(Vector3 position) => Play(AudioCue.Explosion, Vehicles.VehicleBody.ToCore(position));

    /// <summary>Consumes confirmed vehicle boundaries, optionally reseeding historical state.</summary>
    /// <param name="states">Confirmed vehicle states.</param>
    /// <param name="seed">Suppress historical one-shots while restoring the current boundary.</param>
    internal void ApplyVehicles(IEnumerable<VehicleSnapshot> states, bool seed = false) => _events.Vehicles(states, seed);

    /// <summary>Updates reconstructable continuous emitters from displayed authoritative state.</summary>
    /// <param name="states">Confirmed vehicle states.</param>
    /// <param name="position">Displayed arena-space position provider.</param>
    /// <param name="tick">Latest authoritative world tick.</param>
    internal void Follow(IEnumerable<VehicleSnapshot> states, Func<ulong, Vector3> position, ulong tick)
    {
        VehicleSnapshot[] snapshots = states.ToArray();
        var ids = snapshots.Select(state => state.VehicleId).ToHashSet();
        _events.Prune(ids);
        foreach (ulong id in _vehicles.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            _vehicles[id].QueueFree();
            _vehicles.Remove(id);
        }

        foreach (VehicleSnapshot state in snapshots)
        {
            if (!_vehicles.TryGetValue(state.VehicleId, out VehicleAudio? emitter))
            {
                emitter = new VehicleAudio();
                AddChild(emitter);
                _vehicles.Add(state.VehicleId, emitter);
            }

            emitter.Follow(state, position(state.VehicleId));
        }

        _events.Countdown(_match, tick);
    }

    /// <summary>Presents item outcomes and synchronizes bounded projectile travel loops.</summary>
    /// <param name="state">Confirmed gameplay boundary.</param>
    /// <param name="seed">Suppress historical one-shots while restoring the current boundary.</param>
    internal void ApplyItems(ItemPublication state, bool seed = false)
    {
        _events.Items(state, seed);
        foreach (ulong id in _rockets.Keys.Except(state.Missiles.Where(missile => missile.Launched).Select(missile => missile.Id)).ToArray())
        {
            _rockets[id].Stop();
            _rockets[id].Stream = null;
            _rockets[id].QueueFree();
            _rockets.Remove(id);
        }

        foreach (MissileState missile in state.Missiles.Where(missile => missile.Launched))
        {
            if (!_rockets.TryGetValue(missile.Id, out AudioStreamPlayer3D? rocket))
            {
                rocket = Loop3D(AudioCue.MissileTravel);
                rocket.VolumeDb = -22;
                AddChild(rocket);
                _rockets.Add(missile.Id, rocket);
                rocket.Play();
            }

            rocket.Position = Vehicles.VehicleBody.ToGodot(missile.Position);
        }
    }

    /// <summary>Updates match cues independently of the arena-owned playlist.</summary>
    /// <param name="state">Confirmed gameplay boundary.</param>
    /// <param name="seed">Suppress historical one-shots while restoring the current boundary.</param>
    internal void ApplyMatch(MatchState state, bool seed = false)
    {
        if (!seed && _match is not null && state.Revision <= _match.Revision)
        {
            return;
        }

        _events.Match(state, seed);
        _match = state;
    }

    /// <summary>Handles native stream completion; stopped playlists ignore late callbacks.</summary>
    internal void NextSong()
    {
        if (_playlist.Advance())
        {
            PlaySong();
        }
    }

    private static AudioStream LoopStream(AudioCue cue)
    {
        AudioStream stream = (AudioStream)GD.Load<AudioStream>(AudioCatalog.Path(cue)).Duplicate();
        if (stream is AudioStreamWav wav)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = (int)Math.Round(wav.GetLength() * wav.MixRate);
        }
        else if (stream is AudioStreamOggVorbis ogg)
        {
            ogg.Loop = true;
        }

        return stream;
    }

    private static void StopPlayers(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            StopPlayers(child);
            if (child is AudioStreamPlayer player)
            {
                player.Stop();
                player.Stream = null;
            }
            else if (child is AudioStreamPlayer3D spatial)
            {
                spatial.Stop();
                spatial.Stream = null;
            }
        }
    }

    private void PlaySong()
    {
        _music.Stream = GD.Load<AudioStream>(AudioCatalog.Song(_playlist.Index));
        _music.Play();
    }

    private void Play(AudioCue cue, System.Numerics.Vector3 position)
    {
        // Finished voices are removed immediately; the cap also bounds reliable catch-up bursts.
        if (_voices.Count >= 40)
        {
            return;
        }

        AudioStream stream = GD.Load<AudioStream>(AudioCatalog.Path(cue));
        bool local = cue is AudioCue.Kill or AudioCue.Death or AudioCue.Countdown or AudioCue.MatchStart or AudioCue.MatchEnd or AudioCue.EndSting;
        if (local)
        {
            var voice = new AudioStreamPlayer { Stream = stream, Bus = AudioRouting.Bus(cue), VolumeDb = -12 };
            AddChild(voice);
            _voices.Add(voice);
            voice.Finished += () => Retire(voice);
            voice.Play();
        }
        else
        {
            var voice = new AudioStreamPlayer3D { Stream = stream, Bus = AudioRouting.Bus(cue), Position = Vehicles.VehicleBody.ToGodot(position), MaxDistance = 80, UnitSize = 12, VolumeDb = -12 };
            if (cue == AudioCue.MachineGunFire) { voice.PitchScale = 1.8f; voice.VolumeDb = -16; voice.MaxDistance = 35; }
            AddChild(voice);
            _voices.Add(voice);
            voice.Finished += () => Retire(voice);
            voice.Play();
        }

        CueCount++;
        if (cue == AudioCue.HeavyCollision || cue == AudioCue.Destruction)
        {
            Play(AudioCue.Collision, position);
        }
    }

    private void Retire(Node voice)
    {
        if (voice is AudioStreamPlayer player)
        {
            player.Stream = null;
        }
        else if (voice is AudioStreamPlayer3D spatial)
        {
            spatial.Stream = null;
        }

        _voices.Remove(voice);
        voice.QueueFree();
    }
}
