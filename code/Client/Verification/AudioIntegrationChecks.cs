using Godot;
using Trackstorm.Client.Audio;
using Trackstorm.Client.Input;
using Trackstorm.Client.Settings;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Settings;

namespace Trackstorm.Client.Verification;

/// <summary>Native stream, bus, playlist-completion and arena ownership verification.</summary>
public sealed partial class AudioIntegrationChecks : Node
{
    private int _assertions;

    /// <inheritdoc/>
    public override void _Ready() => CallDeferred(MethodName.Run);

    /// <summary>Uses real imported streams and native completion signals, then exits.</summary>
    public async void Run()
    {
        try
        {
            var input = new PlayerInput();
            AddChild(input);
            input.SetPhysicsProcess(false);
            var settings = new PlayerSettingsController();
            settings.Initialize(input.Adapter, ProjectSettings.GlobalizePath("res://.godot/audio-check-settings.json"));
            AddChild(settings);
            foreach (AudioCue cue in Enum.GetValues<AudioCue>())
            {
                AudioStream stream = GD.Load<AudioStream>(AudioCatalog.Path(cue));
                Require(stream.GetLength() > 0.01, $"Imported playable stream: {cue}");
            }

            for (int index = 0; index < 3; index++)
            {
                var song = GD.Load<AudioStreamMP3>(AudioCatalog.Song(index));
                Require(song.GetLength() > 170 && !song.Loop, $"Song {index + 1} is complete and does not loop independently.");
            }

            settings.UpdateSettings(new PlayerSettings { MasterVolume = 0.5, MusicVolume = 0.25, SfxVolume = 0 });
            Require(Math.Abs(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Master")) + 6.0206) < 0.01, "Existing Master settings apply.");
            Require(Math.Abs(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Music")) + 12.0412) < 0.01, "Music gain is independent.");
            Require(AudioServer.IsBusMute(AudioServer.GetBusIndex("SFX")), "SFX mute applies to its descendants.");
            foreach (string child in new[] { "Vehicle", "Weapons", "UI" })
            {
                Require(AudioServer.GetBusSend(AudioServer.GetBusIndex(child)) == "SFX", $"{child} feeds SFX.");
            }

            foreach (string child in new[] { "Music", "SFX" })
            {
                Require(AudioServer.GetBusSend(AudioServer.GetBusIndex(child)) == "Master", $"{child} feeds Master.");
            }

            settings.UpdateSettings(new PlayerSettings());
            var arena = new ArenaAudio();
            AddChild(arena);
            Require(!arena.MusicPlaying && arena.TrackIndex == -1, "Arena entry before active match is silent for music.");
            var active = new MatchState(10, 1, 5, MatchPhase.Active, null, null, []);
            arena.ApplyMatch(active);
            Require(arena.MusicPlaying && arena.TrackIndex is >= 0 and < 3, "Active state starts one valid track.");
            AudioStreamPlayer player = arena.GetChildren().OfType<AudioStreamPlayer>().Single(node => node.Bus == "Music");
            for (int step = 0; step < 4; step++)
            {
                int index = arena.TrackIndex;
                arena.ApplyMatch(active);
                Require(arena.TrackIndex == index, "Repeated match state does not restart music.");
                player.Seek((float)player.Stream.GetLength() - 0.12f);
                for (int wait = 0; wait < 100 && arena.TrackIndex == index; wait++)
                {
                    await ToSignal(GetTree().CreateTimer(0.02), SceneTreeTimer.SignalName.Timeout);
                }

                Require(arena.TrackIndex == (index + 1) % 3, "Real MP3 completion advances sequentially, including wraparound.");
            }

            arena.ApplyMatch(new MatchState(20, 2, 5, MatchPhase.Waiting, null, null, []));
            Require(!arena.MusicPlaying && arena.TrackIndex == -1, "Leaving active state stops and resets music.");
            arena.NextSong();
            Require(arena.TrackIndex == -1, "Late completion cannot restart a stopped match.");
            arena.ApplyMatch(new MatchState(30, 3, 5, MatchPhase.Active, null, null, []));
            arena.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!IsInstanceValid(arena) && !IsInstanceValid(player), "Arena exit frees music and every owned voice.");
            await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
            GD.Print($"Audio integration passed: {_assertions} assertions.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private void Require(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException(description);
        }

        _assertions++;
    }
}
